using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Spreadsheet;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Word = DocumentFormat.OpenXml.Wordprocessing;

namespace GitIt.Core;

public sealed class OfficeScanner
{
    private static readonly IReadOnlyDictionary<string, OfficeFileKind> Kinds = new Dictionary<string, OfficeFileKind>(StringComparer.OrdinalIgnoreCase)
    {
        [".docx"] = OfficeFileKind.Docx, [".xlsx"] = OfficeFileKind.Xlsx, [".pptx"] = OfficeFileKind.Pptx,
    };

    public ScanResult Scan(string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Folder not found: {folder}");
        var documents = new List<OfficeDocumentProfile>();
        var issues = new List<ScanIssue>();
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (!Kinds.TryGetValue(Path.GetExtension(path), out var kind)) continue;
            // Office lock files are transient working-state artifacts, not document versions.
            if (Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal)) continue;
            try { documents.Add(Read(path, kind)); }
            catch (Exception ex) when (ex is IOException or OpenXmlPackageException or InvalidDataException)
            { issues.Add(new ScanIssue(path, ex.Message)); }
        }
        return new ScanResult(documents.OrderBy(d => d.Path, StringComparer.OrdinalIgnoreCase).ToArray(), issues);
    }

    public OfficeDocumentProfile Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Office file not found.", path);
        if (!Kinds.TryGetValue(Path.GetExtension(path), out var kind))
            throw new ArgumentException("Only .docx, .xlsx, and .pptx are supported in v0.0.2.", nameof(path));
        return Read(path, kind);
    }

    private static OfficeDocumentProfile Read(string path, OfficeFileKind kind) => kind switch
    {
        OfficeFileKind.Docx => ReadDocx(path), OfficeFileKind.Xlsx => ReadXlsx(path), OfficeFileKind.Pptx => ReadPptx(path), _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static OfficeDocumentProfile ReadDocx(string path)
    {
        using var package = WordprocessingDocument.Open(path, false);
        var body = package.MainDocumentPart?.Document?.Body;
        var paragraphs = body?.Descendants<Word.Paragraph>().Select((p, i) => ToParagraph(p, i)).ToArray() ?? Array.Empty<ParagraphFingerprint>();
        var tables = body?.Elements<Word.Table>().Select((t, i) => ToTable(t, i)).ToArray() ?? Array.Empty<TableFingerprint>();
        var rsids = body is null ? Array.Empty<string>() : ExtractRsids(package.MainDocumentPart!.Document!);
        var revisions = body is null ? new Dictionary<string, int>() : ExtractRevisions(body);
        var revisionAuthors = body is null ? Array.Empty<string>() : ExtractAuthors(body, new[] { "ins", "del", "moveFrom", "moveTo" });
        var commentAuthors = ExtractCommentAuthors(package.MainDocumentPart?.WordprocessingCommentsPart);
        var metadata = Metadata(package);
        var details = new DocxDetails(paragraphs, tables, rsids, revisions, revisionAuthors, commentAuthors,
            Hash(string.Join("\n", paragraphs.Select(p => p.TextHash))), Hash(string.Join("\n", paragraphs.Select(p => p.FormatHash))));
        var participants = MetadataPeople(path, metadata).Concat(revisionAuthors.Select(a => new ParticipantEvidence(a, path, "wordprocessing/document.xml", "revision-author", EvidenceStrength.Strong, "Author attribute on a tracked change.")))
            .Concat(commentAuthors.Select(a => new ParticipantEvidence(a, path, "word/comments.xml", "comment-author", EvidenceStrength.ParticipationOnly, "Comment authorship proves review participation, not document editing."))).ToArray();
        var evidence = new List<Evidence> { new("content", EvidenceStrength.Medium, 0.50, $"Read {paragraphs.Length} body paragraphs and {tables.Length} top-level tables."), new("metadata", EvidenceStrength.Medium, 0.35, "Read core package properties.") };
        if (rsids.Length > 0) evidence.Add(new Evidence("rsid", EvidenceStrength.Medium, 0.60, $"Found {rsids.Length} distinct RSID values; they are editing traces, not people."));
        if (revisions.Count > 0) evidence.Add(new Evidence("revision", EvidenceStrength.Strong, 0.85, $"Found {revisions.Values.Sum()} tracked-change elements."));
        if (commentAuthors.Length > 0) evidence.Add(new Evidence("comment", EvidenceStrength.ParticipationOnly, 0.80, $"Found {commentAuthors.Length} comment author identity value(s)."));
        var degraded = Unsupported(package, new[] { "oleObject", "vbaProject" }).ToList();
        if (rsids.Length == 0) degraded.Add("No RSID information; edit-session continuity is unavailable.");
        if (revisions.Count == 0) degraded.Add("Track Changes absent; revision continuity is unavailable.");
        if (string.IsNullOrWhiteSpace(metadata.Creator) && string.IsNullOrWhiteSpace(metadata.LastModifiedBy)) degraded.Add("Author metadata removed or absent; identity evidence is degraded.");
        if (rsids.Length == 0 && revisions.Count == 0 && string.IsNullOrWhiteSpace(metadata.Creator) && string.IsNullOrWhiteSpace(metadata.LastModifiedBy)) degraded.Add("Insufficient provenance evidence; copy/paste reconstruction is possible.");
        return Profile(path, OfficeFileKind.Docx, metadata, new Dictionary<string, string>
        {
            ["paragraphs"] = paragraphs.Length.ToString(), ["tables"] = tables.Length.ToString(), ["bodyHash"] = details.BodyHash, ["styleHash"] = details.StyleHash,
            ["rsidCount"] = rsids.Length.ToString(), ["trackedRevisionCount"] = revisions.Values.Sum().ToString(),
        }, participants, evidence, degraded, details);
    }

    private static OfficeDocumentProfile ReadXlsx(string path)
    {
        using var package = SpreadsheetDocument.Open(path, false);
        var workbookPart = package.WorkbookPart;
        var sharedStrings = workbookPart?.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(s => s.InnerText).ToArray() ?? Array.Empty<string>();
        var sheets = workbookPart?.Workbook?.Sheets?.Elements<Sheet>().Select((sheet, index) => ToSheet(workbookPart, sheet, index, sharedStrings)).ToArray() ?? Array.Empty<SpreadsheetSheet>();
        var commentAuthors = workbookPart?.WorksheetParts.SelectMany(part => part.WorksheetCommentsPart?.Comments?.Authors?.Elements<DocumentFormat.OpenXml.Spreadsheet.Author>().Select(author => author.InnerText) ?? Enumerable.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();
        var metadata = Metadata(package);
        var unsupported = Unsupported(package, new[] { "vbaProject", "connections", "pivot", "externalLink" });
        var participants = MetadataPeople(path, metadata).Concat(commentAuthors.Select(author => new ParticipantEvidence(author, path, "xl/comments*.xml", "comment-author", EvidenceStrength.ParticipationOnly, "Spreadsheet comment authorship proves review participation, not a workbook edit."))).ToArray();
        var fingerprint = new Dictionary<string, string>
        {
            ["sheets"] = sheets.Length.ToString(), ["cells"] = sheets.Sum(s => s.Cells.Count).ToString(), ["formulas"] = sheets.Sum(s => s.Cells.Count(c => c.Formula is not null)).ToString(),
            ["sheetNamesHash"] = Hash(string.Join("|", sheets.Select(s => s.Name))), ["workbookHash"] = Hash(string.Join("|", sheets.Select(s => s.Hash))),
        };
        return Profile(path, OfficeFileKind.Xlsx, metadata, fingerprint, participants,
            new[] { new Evidence("workbook", EvidenceStrength.Medium, 0.50, $"Read {sheets.Length} worksheet(s) and {sheets.Sum(s => s.Cells.Count)} stored cell(s).") }, unsupported, xlsx: new XlsxDetails(sheets, unsupported));
    }

    private static OfficeDocumentProfile ReadPptx(string path)
    {
        using var package = PresentationDocument.Open(path, false);
        var presentationPart = package.PresentationPart;
        var slides = presentationPart?.Presentation?.SlideIdList?.Elements<SlideId>().Select((slideId, index) => ToSlide(presentationPart, slideId, index)).ToArray() ?? Array.Empty<PresentationSlide>();
        var metadata = Metadata(package);
        var unsupported = Unsupported(package, new[] { "vbaProject", "oleObject", "chart", "media", "smartart", "timing" });
        var commentAuthors = presentationPart?.CommentAuthorsPart?.CommentAuthorList?.Elements<CommentAuthor>().Select(author => author.Name?.Value).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();
        var participants = MetadataPeople(path, metadata).Concat(commentAuthors.Select(author => new ParticipantEvidence(author, path, "ppt/commentAuthors.xml", "comment-author", EvidenceStrength.ParticipationOnly, "Presentation comment authorship proves review participation, not a slide edit."))).ToArray();
        var themeHash = Hash(presentationPart?.ThemePart?.Theme?.OuterXml ?? string.Empty);
        var fingerprint = new Dictionary<string, string>
        {
            ["slides"] = slides.Length.ToString(), ["shapes"] = slides.Sum(s => s.Shapes.Count).ToString(), ["themeHash"] = themeHash,
            ["slideTextHash"] = Hash(string.Join("\n", slides.Select(s => string.Join("\n", s.Shapes.Select(shape => shape.Text))))),
        };
        return Profile(path, OfficeFileKind.Pptx, metadata, fingerprint, participants,
            new[] { new Evidence("presentation", EvidenceStrength.Medium, 0.50, $"Read {slides.Length} slide(s) and {slides.Sum(s => s.Shapes.Count)} text-capable shape(s).") }, unsupported, pptx: new PptxDetails(slides, themeHash, unsupported));
    }

    private static SpreadsheetSheet ToSheet(WorkbookPart? workbookPart, Sheet sheet, int index, IReadOnlyList<string> sharedStrings)
    {
        var worksheetPart = workbookPart?.GetPartById(sheet.Id!.Value!) as WorksheetPart;
        var stylesheet = workbookPart?.WorkbookStylesPart?.Stylesheet;
        var cells = worksheetPart?.Worksheet?.Descendants<Cell>().Select(c => ToCell(c, sharedStrings, stylesheet)).OrderBy(c => c.Address, StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<SpreadsheetCell>();
        var merges = worksheetPart?.Worksheet?.Elements<MergeCells>().SelectMany(m => m.Elements<MergeCell>()).Select(m => m.Reference?.Value ?? string.Empty).Where(s => s.Length > 0).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();
        var rowProps = worksheetPart?.Worksheet?.GetFirstChild<SheetData>()?.Elements<Row>().Where(r => r.Hidden?.Value == true || r.Height is not null).ToDictionary(r => (int)(r.RowIndex?.Value ?? 0U), r => $"hidden={r.Hidden?.Value == true};height={r.Height?.Value}") ?? new Dictionary<int, string>();
        var columnProps = new Dictionary<int, string>();
        foreach (var column in worksheetPart?.Worksheet?.Elements<Columns>().SelectMany(columns => columns.Elements<DocumentFormat.OpenXml.Spreadsheet.Column>()) ?? Enumerable.Empty<DocumentFormat.OpenXml.Spreadsheet.Column>())
        for (var i = (int)(column.Min?.Value ?? 0U); i <= (int)(column.Max?.Value ?? 0U); i++) columnProps[i] = $"hidden={column.Hidden?.Value == true};width={column.Width?.Value}";
        var name = sheet.Name?.Value ?? $"Sheet{index + 1}";
        return new SpreadsheetSheet(index, name, cells, merges, rowProps, columnProps, Hash(string.Join("|", cells.Select(c => $"{c.Address}:{c.Value}:{c.Formula}:{c.StyleSignature}"))));
    }

    private static SpreadsheetCell ToCell(Cell cell, IReadOnlyList<string> sharedStrings, Stylesheet? stylesheet)
    {
        var type = cell.DataType?.Value.ToString() ?? "NumberOrGeneral";
        var value = cell.CellValue?.Text;
        if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(value, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < sharedStrings.Count) value = sharedStrings[sharedIndex];
        if (cell.DataType?.Value == CellValues.InlineString) value = cell.InlineString?.InnerText;
        var format = stylesheet?.CellFormats?.Elements<CellFormat>().ElementAtOrDefault((int)(cell.StyleIndex?.Value ?? 0U));
        var numberFormatId = format?.NumberFormatId?.Value;
        var numberFormatCode = numberFormatId is null ? null : stylesheet?.NumberingFormats?.Elements<NumberingFormat>().FirstOrDefault(numberFormat => numberFormat.NumberFormatId?.Value == numberFormatId)?.FormatCode?.Value;
        var signature = format is null ? "default" : $"num={numberFormatId}:{numberFormatCode ?? "built-in"};font={format.FontId?.Value};fill={format.FillId?.Value};border={format.BorderId?.Value};align={format.Alignment?.OuterXml}";
        return new SpreadsheetCell(cell.CellReference?.Value ?? "(unknown)", value, cell.CellFormula?.Text, type, signature);
    }

    private static PresentationSlide ToSlide(PresentationPart? presentationPart, SlideId slideId, int index)
    {
        var slidePart = presentationPart?.GetPartById(slideId.RelationshipId!.Value!) as SlidePart;
        var shapes = slidePart?.Slide?.CommonSlideData?.ShapeTree?.Elements<Shape>().Select(ToShape).ToArray() ?? Array.Empty<SlideShape>();
        var layout = slidePart?.SlideLayoutPart?.Uri.ToString() ?? "(none)";
        return new PresentationSlide(index, layout, shapes, Hash($"{layout}|{string.Join("|", shapes.Select(s => $"{s.Name}:{s.Text}:{s.X}:{s.Y}:{s.Width}:{s.Height}:{s.FontSignature}"))}"));
    }

    private static SlideShape ToShape(Shape shape)
    {
        var transform = shape.ShapeProperties?.Transform2D;
        var runProperties = shape.TextBody?.Descendants<Drawing.RunProperties>().FirstOrDefault();
        var color = runProperties?.GetFirstChild<Drawing.SolidFill>()?.RgbColorModelHex?.Val?.Value ?? "(theme/none)";
        var font = runProperties is null ? "default" : $"size={runProperties.FontSize?.Value};bold={runProperties.Bold?.Value};color={color};latin={runProperties.GetFirstChild<Drawing.LatinFont>()?.Typeface?.Value}";
        return new SlideShape(shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Id?.Value ?? 0U,
            shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value ?? "(unnamed)", "shape", NormalizeText(shape.TextBody?.InnerText ?? string.Empty),
            transform?.Offset?.X?.Value, transform?.Offset?.Y?.Value, transform?.Extents?.Cx?.Value, transform?.Extents?.Cy?.Value, font);
    }

    private static OfficeDocumentProfile Profile(string path, OfficeFileKind kind, CommonOfficeMetadata metadata, IReadOnlyDictionary<string, string> fingerprint,
        IReadOnlyList<ParticipantEvidence> participants, IReadOnlyList<Evidence> evidence, IReadOnlyList<string> unsupported, DocxDetails? docx = null, XlsxDetails? xlsx = null, PptxDetails? pptx = null)
    {
        var info = new FileInfo(path);
        return new OfficeDocumentProfile(Path.GetFullPath(path), kind, info.Length, info.LastWriteTimeUtc, FileHash(path), metadata, fingerprint, participants, evidence, unsupported, docx, xlsx, pptx);
    }

    private static IReadOnlyList<ParticipantEvidence> MetadataPeople(string path, CommonOfficeMetadata metadata) => new[]
    {
        metadata.Creator is null ? null : new ParticipantEvidence(metadata.Creator, path, "core.xml", "creator", EvidenceStrength.Medium, "Office core property; identity is not authenticated."),
        metadata.LastModifiedBy is null ? null : new ParticipantEvidence(metadata.LastModifiedBy, path, "core.xml", "lastModifiedBy", EvidenceStrength.Medium, "Office core property; identity is not authenticated."),
    }.Where(e => e is not null).Select(e => e!).ToArray();

    private static CommonOfficeMetadata Metadata(OpenXmlPackage package) => new(package.PackageProperties.Title, package.PackageProperties.Creator, package.PackageProperties.LastModifiedBy, package.PackageProperties.Created, package.PackageProperties.Modified, package.PackageProperties.Revision);
    private static ParagraphFingerprint ToParagraph(Word.Paragraph paragraph, int index)
    {
        var text = NormalizeText(paragraph.InnerText); var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "(default)";
        return new ParagraphFingerprint(index, text, Hash(text), style, Hash($"{style}|{NormalizeFormatXml(paragraph.ParagraphProperties?.OuterXml ?? string.Empty)}|{NormalizeFormatXml(string.Join("|", paragraph.Descendants<Word.RunProperties>().Select(p => p.OuterXml)))}"));
    }
    private static TableFingerprint ToTable(Word.Table table, int index)
    {
        var rows = table.Elements<Word.TableRow>().ToArray(); var columns = rows.Length == 0 ? 0 : rows.Max(r => r.Elements<Word.TableCell>().Count());
        return new TableFingerprint(index, rows.Length, columns, Hash($"{string.Join(";", rows.Select(r => r.Elements<Word.TableCell>().Count()))}|{NormalizeText(table.InnerText)}"));
    }
    private static string[] ExtractRsids(OpenXmlElement root) => new[] { root }.Concat(root.Descendants()).SelectMany(e => e.GetAttributes()).Where(a => a.LocalName.StartsWith("rsid", StringComparison.OrdinalIgnoreCase)).Select(a => a.Value).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray();
    private static Dictionary<string, int> ExtractRevisions(OpenXmlElement root) => root.Descendants().Where(e => e.LocalName is "ins" or "del" or "moveFrom" or "moveTo").GroupBy(e => e.LocalName, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
    private static string[] ExtractAuthors(OpenXmlElement root, IEnumerable<string> names) => root.Descendants().Where(e => names.Contains(e.LocalName, StringComparer.Ordinal)).SelectMany(e => e.GetAttributes()).Where(a => a.LocalName.Equals("author", StringComparison.OrdinalIgnoreCase)).Select(a => a.Value).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray();
    private static string[] ExtractCommentAuthors(WordprocessingCommentsPart? part) => part?.Comments?.Elements<Word.Comment>().Select(c => c.Author?.Value).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();
    private static IReadOnlyList<string> Unsupported(OpenXmlPackage package, IEnumerable<string> keys) => package.Parts.Select(p => p.OpenXmlPart.ContentType).Where(contentType => keys.Any(key => contentType.Contains(key, StringComparison.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase).Select(contentType => $"Partially analyzed content detected: {contentType}").ToArray();
    internal static string NormalizeText(string value) => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
    private static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string NormalizeFormatXml(string value) => Regex.Replace(value, "\\s+w:rsid\\w+=\\\"[^\\\"]*\\\"", string.Empty, RegexOptions.IgnoreCase);
}

public sealed class SemanticDiffer
{
    public SemanticDiffResult Compare(OfficeDocumentProfile source, OfficeDocumentProfile target)
    {
        if (source.Kind != target.Kind) throw new ArgumentException("A semantic diff requires files of the same Office type.");
        return source.Kind switch { OfficeFileKind.Docx => Docx(source, target), OfficeFileKind.Xlsx => Xlsx(source, target), OfficeFileKind.Pptx => Pptx(source, target), _ => throw new ArgumentOutOfRangeException() };
    }

    private static SemanticDiffResult Docx(OfficeDocumentProfile source, OfficeDocumentProfile target)
    {
        var changes = new List<DiffChange>(); var a = source.Docx!; var b = target.Docx!;
        for (var i = 0; i < Math.Max(a.Paragraphs.Count, b.Paragraphs.Count); i++)
        {
            if (i >= a.Paragraphs.Count) { changes.Add(new DiffChange("content", $"Paragraph {i + 1}", "Paragraph added.", null, b.Paragraphs[i].Text)); continue; }
            if (i >= b.Paragraphs.Count) { changes.Add(new DiffChange("content", $"Paragraph {i + 1}", "Paragraph removed.", a.Paragraphs[i].Text)); continue; }
            if (a.Paragraphs[i].TextHash != b.Paragraphs[i].TextHash) changes.Add(new DiffChange("content", $"Paragraph {i + 1}", "Text changed.", a.Paragraphs[i].Text, b.Paragraphs[i].Text));
            if (a.Paragraphs[i].FormatHash != b.Paragraphs[i].FormatHash) changes.Add(new DiffChange("format", $"Paragraph {i + 1}", "Paragraph formatting changed.", a.Paragraphs[i].StyleId, b.Paragraphs[i].StyleId));
        }
        for (var i = 0; i < Math.Max(a.Tables.Count, b.Tables.Count); i++)
        {
            if (i >= a.Tables.Count) { changes.Add(new DiffChange("structure", $"Table {i + 1}", "Table added.", null, $"{b.Tables[i].Rows}×{b.Tables[i].Columns}")); continue; }
            if (i >= b.Tables.Count) { changes.Add(new DiffChange("structure", $"Table {i + 1}", "Table removed.", $"{a.Tables[i].Rows}×{a.Tables[i].Columns}")); continue; }
            if (a.Tables[i].Hash != b.Tables[i].Hash) changes.Add(new DiffChange("structure", $"Table {i + 1}", "Table structure or content changed.", $"{a.Tables[i].Rows}×{a.Tables[i].Columns}", $"{b.Tables[i].Rows}×{b.Tables[i].Columns}"));
        }
        return Result(source, target, changes);
    }

    private static SemanticDiffResult Xlsx(OfficeDocumentProfile source, OfficeDocumentProfile target)
    {
        var changes = new List<DiffChange>(); var a = source.Xlsx!; var b = target.Xlsx!;
        var aByName = a.Sheets.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase); var bByName = b.Sheets.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        var removed = a.Sheets.Where(s => !bByName.ContainsKey(s.Name)).ToList(); var added = b.Sheets.Where(s => !aByName.ContainsKey(s.Name)).ToList();
        var renamed = new List<(SpreadsheetSheet Before, SpreadsheetSheet After)>();
        foreach (var oldSheet in removed.ToArray())
        {
            var replacement = added.OrderByDescending(newSheet => SheetIdentitySimilarity(oldSheet, newSheet)).FirstOrDefault();
            if (replacement is not null && SheetIdentitySimilarity(oldSheet, replacement) >= .80) { renamed.Add((oldSheet, replacement)); removed.Remove(oldSheet); added.Remove(replacement); changes.Add(new DiffChange("workbook", replacement.Name, "Sheet renamed.", oldSheet.Name, replacement.Name)); }
        }
        foreach (var sheet in removed) changes.Add(new DiffChange("workbook", sheet.Name, "Sheet removed."));
        foreach (var sheet in added) changes.Add(new DiffChange("workbook", sheet.Name, "Sheet added."));
        foreach (var oldSheet in a.Sheets.Where(s => bByName.ContainsKey(s.Name)).Concat(renamed.Select(pair => pair.Before)))
        {
            var newSheet = bByName.GetValueOrDefault(oldSheet.Name) ?? renamed.Single(pair => ReferenceEquals(pair.Before, oldSheet)).After;
            if (oldSheet.Index != newSheet.Index) changes.Add(new DiffChange("workbook", oldSheet.Name, "Sheet reordered.", oldSheet.Index.ToString(), newSheet.Index.ToString()));
            CompareCells(oldSheet, newSheet, changes); CompareSheetStructure(oldSheet, newSheet, changes);
        }
        return Result(source, target, changes, a.UnsupportedFeatures.Concat(b.UnsupportedFeatures).ToArray());
    }

    private static void CompareCells(SpreadsheetSheet source, SpreadsheetSheet target, List<DiffChange> changes)
    {
        var a = source.Cells.ToDictionary(c => c.Address, StringComparer.OrdinalIgnoreCase); var b = target.Cells.ToDictionary(c => c.Address, StringComparer.OrdinalIgnoreCase);
        foreach (var address in a.Keys.Union(b.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!a.TryGetValue(address, out var before)) { changes.Add(new DiffChange("cell", $"{target.Name}!{address}", "Cell added.", null, b[address].Value)); continue; }
            if (!b.TryGetValue(address, out var after)) { changes.Add(new DiffChange("cell", $"{source.Name}!{address}", "Cell removed.", before.Value)); continue; }
            var location = $"{source.Name}!{address}";
            if (before.Value != after.Value) changes.Add(new DiffChange("cell", location, "Value changed.", before.Value, after.Value));
            if (before.Formula != after.Formula) changes.Add(new DiffChange("cell", location, "Formula changed.", before.Formula, after.Formula));
            if (before.DataType != after.DataType) changes.Add(new DiffChange("cell", location, "Type changed.", before.DataType, after.DataType));
            if (before.StyleSignature != after.StyleSignature) changes.Add(new DiffChange("format", location, "Number format, font, fill, border, or alignment changed.", before.StyleSignature, after.StyleSignature));
        }
    }

    private static void CompareSheetStructure(SpreadsheetSheet a, SpreadsheetSheet b, List<DiffChange> changes)
    {
        foreach (var row in a.RowProperties.Keys.Union(b.RowProperties.Keys).OrderBy(n => n)) if (a.RowProperties.GetValueOrDefault(row) != b.RowProperties.GetValueOrDefault(row)) changes.Add(new DiffChange("structure", $"{a.Name}!row {row}", "Hidden state or row height changed.", a.RowProperties.GetValueOrDefault(row), b.RowProperties.GetValueOrDefault(row)));
        foreach (var column in a.ColumnProperties.Keys.Union(b.ColumnProperties.Keys).OrderBy(n => n)) if (a.ColumnProperties.GetValueOrDefault(column) != b.ColumnProperties.GetValueOrDefault(column)) changes.Add(new DiffChange("structure", $"{a.Name}!column {ColumnName(column)}", "Hidden state or column width changed.", a.ColumnProperties.GetValueOrDefault(column), b.ColumnProperties.GetValueOrDefault(column)));
        var sourceRows = UsedRows(a); var targetRows = UsedRows(b); foreach (var row in targetRows.Except(sourceRows)) changes.Add(new DiffChange("structure", $"{b.Name}!row {row}", "Used row added.")); foreach (var row in sourceRows.Except(targetRows)) changes.Add(new DiffChange("structure", $"{a.Name}!row {row}", "Used row removed."));
        var sourceColumns = UsedColumns(a); var targetColumns = UsedColumns(b); foreach (var column in targetColumns.Except(sourceColumns)) changes.Add(new DiffChange("structure", $"{b.Name}!column {ColumnName(column)}", "Used column added.")); foreach (var column in sourceColumns.Except(targetColumns)) changes.Add(new DiffChange("structure", $"{a.Name}!column {ColumnName(column)}", "Used column removed."));
        if (!a.MergedCells.SequenceEqual(b.MergedCells, StringComparer.OrdinalIgnoreCase)) changes.Add(new DiffChange("structure", a.Name, "Merged cells changed.", string.Join(",", a.MergedCells), string.Join(",", b.MergedCells)));
    }

    private static SemanticDiffResult Pptx(OfficeDocumentProfile source, OfficeDocumentProfile target)
    {
        var changes = new List<DiffChange>(); var a = source.Pptx!; var b = target.Pptx!;
        var targetByHash = b.Slides.GroupBy(slide => slide.Hash, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var oldSlide in a.Slides)
        {
            var matching = targetByHash.GetValueOrDefault(oldSlide.Hash)?.FirstOrDefault();
            if (matching is not null && matching.Index != oldSlide.Index) changes.Add(new DiffChange("slides", $"Slide {oldSlide.Index + 1}", "Slide reordered.", (oldSlide.Index + 1).ToString(), (matching.Index + 1).ToString()));
        }
        for (var index = 0; index < Math.Max(a.Slides.Count, b.Slides.Count); index++)
        {
            if (index >= a.Slides.Count) { changes.Add(new DiffChange("slides", $"Slide {index + 1}", "Slide added.")); continue; }
            if (index >= b.Slides.Count) { changes.Add(new DiffChange("slides", $"Slide {index + 1}", "Slide removed.")); continue; }
            var oldSlide = a.Slides[index]; var newSlide = b.Slides[index];
            if (oldSlide.LayoutName != newSlide.LayoutName) changes.Add(new DiffChange("structure", $"Slide {index + 1}", "Layout changed.", oldSlide.LayoutName, newSlide.LayoutName));
            var oldShapes = oldSlide.Shapes.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase); var newShapes = newSlide.Shapes.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var name in oldShapes.Keys.Union(newShapes.Keys, StringComparer.OrdinalIgnoreCase))
            {
                var location = $"Slide {index + 1} / Shape {name}";
                if (!oldShapes.TryGetValue(name, out var before)) { changes.Add(new DiffChange("shape", location, "Shape added.")); continue; }
                if (!newShapes.TryGetValue(name, out var after)) { changes.Add(new DiffChange("shape", location, "Shape removed.")); continue; }
                if (before.Text != after.Text) changes.Add(new DiffChange("text", location, "Text changed.", before.Text, after.Text));
                if ((before.X, before.Y) != (after.X, after.Y)) changes.Add(new DiffChange("shape", location, "Position changed.", $"{before.X},{before.Y}", $"{after.X},{after.Y}"));
                if ((before.Width, before.Height) != (after.Width, after.Height)) changes.Add(new DiffChange("shape", location, "Size changed.", $"{before.Width}×{before.Height}", $"{after.Width}×{after.Height}"));
                if (before.FontSignature != after.FontSignature) changes.Add(new DiffChange("format", location, "Font, font size, bold, or color changed.", before.FontSignature, after.FontSignature));
            }
        }
        if (a.ThemeHash != b.ThemeHash) changes.Add(new DiffChange("structure", "Presentation", "Theme/master reference changed.", a.ThemeHash, b.ThemeHash));
        return Result(source, target, changes, a.UnsupportedFeatures.Concat(b.UnsupportedFeatures).ToArray());
    }

    private static SemanticDiffResult Result(OfficeDocumentProfile source, OfficeDocumentProfile target, List<DiffChange> changes, IReadOnlyList<string>? unsupported = null) => new(source.Path, target.Path, source.Kind, changes, new LineageScorer().Evaluate(source, target).Evidence, unsupported ?? Array.Empty<string>(), "The diff is structural and evidence-led. It does not prove a direct save history.");
    private static double SheetIdentitySimilarity(SpreadsheetSheet a, SpreadsheetSheet b) { var left = a.Cells.Select(c => c.Address).ToHashSet(StringComparer.OrdinalIgnoreCase); var right = b.Cells.Select(c => c.Address).ToHashSet(StringComparer.OrdinalIgnoreCase); var union = left.Union(right, StringComparer.OrdinalIgnoreCase).Count(); return union == 0 ? 0 : (double)left.Intersect(right, StringComparer.OrdinalIgnoreCase).Count() / union; }
    private static HashSet<int> UsedRows(SpreadsheetSheet sheet) => sheet.Cells.Select(cell => int.TryParse(new string(cell.Address.SkipWhile(char.IsLetter).ToArray()), out var row) ? row : 0).Where(row => row > 0).ToHashSet();
    private static HashSet<int> UsedColumns(SpreadsheetSheet sheet) => sheet.Cells.Select(cell => ColumnNumber(new string(cell.Address.TakeWhile(char.IsLetter).ToArray()))).Where(column => column > 0).ToHashSet();
    private static string ColumnName(int number) { var value = string.Empty; while (number > 0) { number--; value = (char)('A' + number % 26) + value; number /= 26; } return value; }
    private static int ColumnNumber(string value) => value.Aggregate(0, (number, character) => number * 26 + (char.ToUpperInvariant(character) - 'A' + 1));
}

public sealed class LineageScorer
{
    private readonly LineageWeights weights;
    public LineageScorer(LineageWeights? weights = null) => this.weights = weights ?? new LineageWeights();

    public LineageCandidate Evaluate(OfficeDocumentProfile from, OfficeDocumentProfile to)
    {
        if (from.Kind != to.Kind) return new LineageCandidate(from.Path, to.Path, 0, LineageStatus.Uncertain, new[] { new Evidence("kind", EvidenceStrength.Conflicting, 0, "Office file kinds differ.", true) }, new[] { "Different Office types cannot be direct versions." });
        if (from.FileHash == to.FileHash) return new LineageCandidate(from.Path, to.Path, 1, LineageStatus.Duplicate, new[] { new Evidence("cryptographicHash", EvidenceStrength.Strong, 1, "SHA-256 file hashes are identical.") }, Array.Empty<string>());
        var evidence = new List<Evidence>(); var warnings = new List<string>(); var confidence = 0.0;
        var content = ContentSimilarity(from, to); Add(evidence, "contentSimilarity", content, weights.ContentSimilarity, content >= .88 ? EvidenceStrength.Strong : content >= .60 ? EvidenceStrength.Medium : EvidenceStrength.Weak, $"Normalized content similarity is {content:P0}.", ref confidence);
        var structure = StructureSimilarity(from, to); Add(evidence, "structureSimilarity", structure, weights.StructureSimilarity, structure >= .90 ? EvidenceStrength.Strong : structure >= .65 ? EvidenceStrength.Medium : EvidenceStrength.Weak, $"Structure similarity is {structure:P0}.", ref confidence);
        var style = StyleSimilarity(from, to); Add(evidence, "styleSimilarity", style, weights.StyleSimilarity, style >= .90 ? EvidenceStrength.Medium : EvidenceStrength.Weak, $"Style similarity is {style:P0}.", ref confidence);
        var containment = Containment(from, to); Add(evidence, "containmentEvidence", containment, weights.Containment, containment >= .90 ? EvidenceStrength.Strong : EvidenceStrength.Weak, $"Source content retained by target is {containment:P0}.", ref confidence);
        var rsid = SharedRsids(from, to); var rsidContinuity = RsidContinuity(from, to);
        if (rsid > 0) Add(evidence, "rsidEvidence", rsidContinuity, weights.Rsid, rsidContinuity >= .90 ? EvidenceStrength.Strong : EvidenceStrength.Medium, $"Shares {rsid} RSID value(s); edit-session continuity is {rsidContinuity:P0}.", ref confidence);
        var revisions = RevisionSignal(from, to); if (revisions > 0) Add(evidence, "revisionEvidence", revisions, weights.Revision, rsid > 0 ? EvidenceStrength.Strong : EvidenceStrength.Medium, "Target carries tracked-change evidence beyond the source profile.", ref confidence);
        var metadata = MetadataSimilarity(from.Metadata, to.Metadata); if (metadata > 0) Add(evidence, "metadataEvidence", metadata, weights.Metadata, EvidenceStrength.Medium, "Creator and/or LastModifiedBy values overlap.", ref confidence);
        var filenameSimilar = NameSimilarity(from.Path, to.Path);
        if (filenameSimilar) Add(evidence, "filenameEvidence", 1, weights.Filename, EvidenceStrength.Weak, "Normalized file names match; file names are never decisive.", ref confidence);
        var before = Modified(from) <= Modified(to);
        if (before) Add(evidence, "timestampEvidence", 1, weights.Timestamp, EvidenceStrength.Weak, "Parent timestamp is not later than child timestamp.", ref confidence);
        else { evidence.Add(new Evidence("timestampEvidence", EvidenceStrength.Conflicting, 0, "Target timestamp is earlier than source timestamp; clock/copy anomalies are possible.", true)); warnings.Add("Timestamp conflicts with this direction and was not used as a hard ordering rule."); }
        confidence = Math.Round(Math.Min(confidence, .99), 2);
        var provenance = rsid > 0 || (revisions > 0 && metadata > 0);
        var corroboratedContent = content >= .93 && structure >= .80 && style >= .80 && metadata > 0 && filenameSimilar;
        var status = provenance || corroboratedContent
            ? confidence >= weights.ProbableConfidence ? LineageStatus.Probable : confidence >= weights.MinimumEdgeConfidence ? LineageStatus.Possible : LineageStatus.Uncertain
            : content >= .80 ? LineageStatus.RelatedButUnproven : LineageStatus.Uncertain;
        if (!provenance && !filenameSimilar && status is LineageStatus.Probable or LineageStatus.Possible)
        { status = LineageStatus.RelatedButUnproven; warnings.Add("Template or copy-paste sibling risk: no provenance continuity and no matching version-name stem."); }
        if (status == LineageStatus.RelatedButUnproven) warnings.Add("High content similarity without sufficient provenance evidence; no parent edge should be asserted.");
        return new LineageCandidate(from.Path, to.Path, confidence, status, evidence.Where(e => e.Score > 0 || e.IsConflict).ToArray(), warnings);
    }

    private static void Add(List<Evidence> evidence, string type, double raw, double weight, EvidenceStrength strength, string detail, ref double total)
    { if (raw <= 0) return; var score = Math.Round(raw * weight, 3); total += score; evidence.Add(new Evidence(type, strength, score, detail)); }
    private static DateTimeOffset Modified(OfficeDocumentProfile profile) => profile.Metadata.Modified ?? profile.FileModified;
    private static int SharedRsids(OfficeDocumentProfile a, OfficeDocumentProfile b) => a.Docx?.Rsids.Intersect(b.Docx?.Rsids ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase).Count() ?? 0;
    private static double RsidContinuity(OfficeDocumentProfile a, OfficeDocumentProfile b)
    {
        if (a.Docx is null || b.Docx is null) return 0;
        var source = a.Docx.Rsids.ToHashSet(StringComparer.OrdinalIgnoreCase); var target = b.Docx.Rsids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!source.IsSubsetOf(target)) return 0.35;
        var added = target.Count - source.Count;
        return added switch { 1 => 1, 2 => .72, 3 => .50, _ when added > 3 => .20, _ => .55 };
    }
    private static double RevisionSignal(OfficeDocumentProfile a, OfficeDocumentProfile b) => a.Docx is null || b.Docx is null ? 0 : b.Docx.RevisionKinds.Values.Sum() > a.Docx.RevisionKinds.Values.Sum() ? 1 : 0;
    private static double MetadataSimilarity(CommonOfficeMetadata a, CommonOfficeMetadata b)
    { var matches = 0; if (Same(a.Creator, b.Creator)) matches++; if (Same(a.LastModifiedBy, b.LastModifiedBy)) matches++; return matches / 2.0; }
    private static bool Same(string? a, string? b) => !string.IsNullOrWhiteSpace(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool NameSimilarity(string a, string b)
    { static string Stem(string value) => Regex.Replace(Path.GetFileNameWithoutExtension(value).ToLowerInvariant(), @"(?:[_\-\s]*(v|ver|version)?\d+|[_\-\s]*(final|draft|copy|revision|edited|修改|最终|终稿))+$", string.Empty).Trim('_', '-', ' '); return Stem(a).Length >= 3 && Stem(a) == Stem(b); }
    private static double ContentSimilarity(OfficeDocumentProfile a, OfficeDocumentProfile b) => Similarity(Content(a), Content(b));
    private static double Containment(OfficeDocumentProfile a, OfficeDocumentProfile b) { var left = Tokens(Content(a)); var right = Tokens(Content(b)); return left.Count == 0 ? 0 : (double)left.Intersect(right, StringComparer.OrdinalIgnoreCase).Count() / left.Count; }
    private static double StructureSimilarity(OfficeDocumentProfile a, OfficeDocumentProfile b) => a.Kind switch
    {
        OfficeFileKind.Docx => Ratio(a.Docx!.Paragraphs.Count + a.Docx.Tables.Count, b.Docx!.Paragraphs.Count + b.Docx.Tables.Count),
        OfficeFileKind.Xlsx => Ratio(a.Xlsx!.Sheets.Sum(s => s.Cells.Count), b.Xlsx!.Sheets.Sum(s => s.Cells.Count)),
        OfficeFileKind.Pptx => Ratio(a.Pptx!.Slides.Count + a.Pptx.Slides.Sum(s => s.Shapes.Count), b.Pptx!.Slides.Count + b.Pptx.Slides.Sum(s => s.Shapes.Count)), _ => 0,
    };
    private static double StyleSimilarity(OfficeDocumentProfile a, OfficeDocumentProfile b) => a.Kind switch
    {
        OfficeFileKind.Docx => a.Docx!.StyleHash == b.Docx!.StyleHash ? 1 : Similarity(string.Join("|", a.Docx.Paragraphs.Select(p => p.FormatHash)), string.Join("|", b.Docx.Paragraphs.Select(p => p.FormatHash))),
        OfficeFileKind.Xlsx => Similarity(string.Join("|", a.Xlsx!.Sheets.SelectMany(s => s.Cells).Select(c => c.StyleSignature)), string.Join("|", b.Xlsx!.Sheets.SelectMany(s => s.Cells).Select(c => c.StyleSignature))),
        OfficeFileKind.Pptx => Similarity(string.Join("|", a.Pptx!.Slides.SelectMany(s => s.Shapes).Select(shape => shape.FontSignature)), string.Join("|", b.Pptx!.Slides.SelectMany(s => s.Shapes).Select(shape => shape.FontSignature))), _ => 0,
    };
    private static string Content(OfficeDocumentProfile p) => p.Kind switch { OfficeFileKind.Docx => string.Join("\n", p.Docx!.Paragraphs.Select(x => x.Text)), OfficeFileKind.Xlsx => string.Join("\n", p.Xlsx!.Sheets.SelectMany(s => s.Cells).Select(c => $"{c.Value} {c.Formula}")), OfficeFileKind.Pptx => string.Join("\n", p.Pptx!.Slides.SelectMany(s => s.Shapes).Select(x => x.Text)), _ => string.Empty };
    private static HashSet<string> Tokens(string input) => Regex.Matches(input, @"[\p{L}\p{N}]+", RegexOptions.CultureInvariant).Select(m => m.Value.ToLowerInvariant()).Where(x => x.Length > 1).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static double Similarity(string a, string b) { var left = Tokens(a); var right = Tokens(b); if (left.Count == 0 && right.Count == 0) return 1; var union = left.Union(right, StringComparer.OrdinalIgnoreCase).Count(); return union == 0 ? 0 : (double)left.Intersect(right, StringComparer.OrdinalIgnoreCase).Count() / union; }
    private static double Ratio(int a, int b) => a == 0 && b == 0 ? 1 : Math.Min(a, b) / (double)Math.Max(a, b);
}

public sealed class LineageInferer
{
    private readonly LineageScorer scorer;
    private readonly CandidateRetriever retriever;
    public LineageInferer(LineageWeights? weights = null) { scorer = new LineageScorer(weights); retriever = new CandidateRetriever(weights); }
    public LineageResult Infer(IEnumerable<OfficeDocumentProfile> profiles)
    {
        var all = profiles.ToArray(); var duplicates = all.GroupBy(p => p.FileHash, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => new DuplicateGroup(g.OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase).First().Path, g.Key, g.Select(p => p.Path).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray())).ToArray();
        var canonical = all.Where(p => !duplicates.SelectMany(g => g.Paths.Skip(1)).Contains(p.Path, StringComparer.OrdinalIgnoreCase)).ToArray();
        var retrieved = retriever.Retrieve(canonical);
        var candidates = retrieved.Select(item =>
        {
            var deep = scorer.Evaluate(item.From, item.To);
            var retrievalEvidence = item.Evidence.Select(evidence => new Evidence($"candidateSelection:{evidence.Type}", EvidenceStrength.Weak, evidence.Score, evidence.Detail));
            return deep with { Evidence = deep.Evidence.Concat(retrievalEvidence).ToArray() };
        }).Where(candidate => candidate.Status is not LineageStatus.Uncertain).OrderByDescending(candidate => candidate.Confidence).ToArray();
        var selected = new List<LineageEdge>();
        foreach (var candidate in candidates.Where(c => c.Status is LineageStatus.Probable or LineageStatus.Possible).GroupBy(c => c.To, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderByDescending(c => c.Confidence).First()).OrderByDescending(c => c.Confidence))
        {
            var edge = new LineageEdge(candidate.From, candidate.To, candidate.Confidence, candidate.Status, candidate.Evidence, candidate.Warnings);
            if (!WouldCreateCycle(selected, edge)) selected.Add(edge);
        }
        var edges = selected.OrderBy(e => e.To, StringComparer.OrdinalIgnoreCase).ToArray();
        var linked = edges.Select(e => e.To).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var uncertain = canonical.Where(p => !linked.Contains(p.Path)).Select(p => p.Path).ToArray();
        var counts = retrieved.GroupBy(candidate => candidate.To.Path, StringComparer.OrdinalIgnoreCase).Select(group => group.Count()).OrderBy(value => value).ToArray();
        var naive = (long)canonical.Length * Math.Max(0, canonical.Length - 1);
        var stats = new CandidateRetrievalStats(naive, retrieved.Count, naive == 0 ? 0 : Math.Round(1 - retrieved.Count / (double)naive, 4), null, canonical.Length == 0 ? 0 : Math.Round(retrieved.Count / (double)canonical.Length, 2), counts.Length == 0 ? 0 : counts[(int)Math.Ceiling(counts.Length * .95) - 1]);
        return new LineageResult(edges, candidates, uncertain, duplicates, stats);
    }

    /// <summary>Benchmark-only reference path: evaluates every ordered pair with the same deep scorer.</summary>
    public LineageResult InferNaive(IEnumerable<OfficeDocumentProfile> profiles)
    {
        var all = profiles.ToArray();
        var duplicates = all.GroupBy(p => p.FileHash, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => new DuplicateGroup(g.OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase).First().Path, g.Key, g.Select(p => p.Path).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray())).ToArray();
        var canonical = all.Where(p => !duplicates.SelectMany(g => g.Paths.Skip(1)).Contains(p.Path, StringComparer.OrdinalIgnoreCase)).ToArray();
        var candidates = canonical.SelectMany(to => canonical.Where(from => !from.Path.Equals(to.Path, StringComparison.OrdinalIgnoreCase) && from.Kind == to.Kind).Select(from => scorer.Evaluate(from, to))).Where(candidate => candidate.Status is not LineageStatus.Uncertain).OrderByDescending(candidate => candidate.Confidence).ToArray();
        var selected = new List<LineageEdge>();
        foreach (var candidate in candidates.Where(c => c.Status is LineageStatus.Probable or LineageStatus.Possible).GroupBy(c => c.To, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderByDescending(c => c.Confidence).First()).OrderByDescending(c => c.Confidence))
        {
            var edge = new LineageEdge(candidate.From, candidate.To, candidate.Confidence, candidate.Status, candidate.Evidence, candidate.Warnings);
            if (!WouldCreateCycle(selected, edge)) selected.Add(edge);
        }
        var linked = selected.Select(edge => edge.To).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var naive = (long)canonical.Length * Math.Max(0, canonical.Length - 1);
        return new LineageResult(selected.OrderBy(edge => edge.To, StringComparer.OrdinalIgnoreCase).ToArray(), candidates, canonical.Where(profile => !linked.Contains(profile.Path)).Select(profile => profile.Path).ToArray(), duplicates, new CandidateRetrievalStats(naive, naive, 0, null, canonical.Length <= 1 ? 0 : canonical.Length - 1, Math.Max(0, canonical.Length - 1)));
    }

    /// <summary>Scores only possible parents of one newly added version; existing file parsing and pair scoring are not repeated.</summary>
    public LineageResult InferForNewVersion(IReadOnlyList<OfficeDocumentProfile> existing, OfficeDocumentProfile added)
    {
        var all = existing.Append(added).ToArray();
        var retrieved = retriever.Retrieve(all).Where(candidate => candidate.To.Path.Equals(added.Path, StringComparison.OrdinalIgnoreCase)).ToArray();
        var candidates = retrieved.Select(item =>
        {
            var deep = scorer.Evaluate(item.From, item.To);
            var selection = item.Evidence.Select(evidence => new Evidence($"candidateSelection:{evidence.Type}", EvidenceStrength.Weak, evidence.Score, evidence.Detail));
            return deep with { Evidence = deep.Evidence.Concat(selection).ToArray() };
        }).Where(candidate => candidate.Status != LineageStatus.Uncertain).OrderByDescending(candidate => candidate.Confidence).ToArray();
        var edge = candidates.FirstOrDefault(candidate => candidate.Status is LineageStatus.Probable or LineageStatus.Possible);
        var stats = new CandidateRetrievalStats(existing.Count, retrieved.Length, existing.Count == 0 ? 0 : Math.Round(1 - retrieved.Length / (double)existing.Count, 4), null, retrieved.Length, retrieved.Length);
        return new LineageResult(edge is null ? Array.Empty<LineageEdge>() : new[] { new LineageEdge(edge.From, edge.To, edge.Confidence, edge.Status, edge.Evidence, edge.Warnings) }, candidates, edge is null ? new[] { added.Path } : Array.Empty<string>(), Array.Empty<DuplicateGroup>(), stats);
    }
    private static bool WouldCreateCycle(IEnumerable<LineageEdge> edges, LineageEdge proposed)
    {
        if (string.Equals(proposed.From, proposed.To, StringComparison.OrdinalIgnoreCase)) return true;
        var adjacency = edges.GroupBy(edge => edge.From, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Select(edge => edge.To).ToArray(), StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); pending.Push(proposed.To);
        while (pending.Count > 0) { var current = pending.Pop(); if (!seen.Add(current)) continue; if (string.Equals(current, proposed.From, StringComparison.OrdinalIgnoreCase)) return true; foreach (var next in adjacency.GetValueOrDefault(current, Array.Empty<string>())) pending.Push(next); }
        return false;
    }
}

public sealed class PeopleAnalyzer
{
    public IReadOnlyList<ParticipantIdentity> Analyze(IEnumerable<OfficeDocumentProfile> documents)
    {
        var groups = documents.SelectMany(d => d.ParticipantEvidence).GroupBy(e => e.Value.Trim(), StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        return groups.Select((group, index) =>
        {
            var normalized = Normalize(group.Key);
            var candidates = groups.Where(other => !string.Equals(other.Key, group.Key, StringComparison.OrdinalIgnoreCase) && Normalize(other.Key) == normalized)
                .Select(other => new IdentityCandidate(other.Key, 0.95, "Case/whitespace-normalized Office identity string matches; kept separate by default."))
                .ToArray();
            return new ParticipantIdentity($"participant-{index + 1}", group.Key, group.OrderBy(e => e.DocumentVersion, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.EvidenceType, StringComparer.OrdinalIgnoreCase).ToArray(), candidates);
        }).ToArray();
    }
    private static string Normalize(string value) => Regex.Replace(value, @"\s+", "").ToUpperInvariant();
}

public sealed class ProjectAnalyzer
{
    public GitItAnalysisResult Analyze(string folder, bool includeEdgeDiffs = false)
    {
        var timer = Stopwatch.StartNew(); var memory = GC.GetTotalMemory(false); var scan = new OfficeScanner().Scan(folder); var scanMetric = new PerformanceMetric("scan-and-fingerprint", timer.Elapsed.TotalMilliseconds, Math.Max(0, GC.GetTotalMemory(false) - memory));
        timer.Restart(); memory = GC.GetTotalMemory(false); var lineage = new LineageInferer().Infer(scan.Documents); var lineageMetric = new PerformanceMetric("candidate-generation-and-lineage", timer.Elapsed.TotalMilliseconds, Math.Max(0, GC.GetTotalMemory(false) - memory));
        timer.Restart(); var changes = includeEdgeDiffs ? lineage.Edges.Select(edge => new SemanticDiffer().Compare(scan.Documents.Single(d => d.Path == edge.From), scan.Documents.Single(d => d.Path == edge.To))).ToArray() : Array.Empty<SemanticDiffResult>(); var diffMetric = new PerformanceMetric("deep-diff", timer.Elapsed.TotalMilliseconds, 0);
        var duplicatePaths = lineage.Duplicates.SelectMany(d => d.Paths.Skip(1).Select(path => (path, d.CanonicalPath))).ToDictionary(x => x.path, x => x.CanonicalPath, StringComparer.OrdinalIgnoreCase);
        var versions = scan.Documents.Select(d => new DocumentVersion(d.Path, d.Path, d.Kind, duplicatePaths.GetValueOrDefault(d.Path), d.FileHash, d.Fingerprint)).ToArray();
        var families = Families(scan.Documents, lineage).ToArray();
        var evidence = lineage.Edges.SelectMany(e => e.Evidence).Concat(scan.Documents.SelectMany(d => d.Evidence)).ToArray();
        return new GitItAnalysisResult("GitIt Analysis Result v1", new ProjectInfo(Path.GetFullPath(folder), DateTimeOffset.UtcNow, "0.0.4"), families, versions, lineage.Edges, changes, new PeopleAnalyzer().Analyze(scan.Documents), evidence, scan.Issues.Select(i => $"{i.Path}: {i.Message}").Concat(lineage.Candidates.Where(c => c.Status == LineageStatus.RelatedButUnproven).Select(c => $"Related but unproven: {c.From} -> {c.To}")).ToArray(), scan.Documents.SelectMany(d => d.UnsupportedFeatures).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), new[] { scanMetric, lineageMetric, diffMetric });
    }
    public ExplainResult Explain(string folder, string versionOrFile)
    {
        var analysis = Analyze(folder, includeEdgeDiffs: false);
        var exact = analysis.Versions.Where(value => string.Equals(value.Path, versionOrFile, StringComparison.OrdinalIgnoreCase)).ToArray();
        var matches = exact.Length > 0 ? exact : analysis.Versions.Where(value => string.Equals(Path.GetFileName(value.Path), versionOrFile, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) throw new FileNotFoundException("Version was not found in the analyzed folder.", versionOrFile);
        if (matches.Length > 1) throw new ArgumentException($"File name is ambiguous; pass its full path. Matches: {string.Join("; ", matches.Select(value => value.Path))}", nameof(versionOrFile));
        var version = matches[0];
        var family = analysis.DocumentFamilies.SingleOrDefault(value => value.VersionIds.Contains(version.Path, StringComparer.OrdinalIgnoreCase));
        var parent = analysis.Edges.SingleOrDefault(edge => string.Equals(edge.To, version.Path, StringComparison.OrdinalIgnoreCase));
        var candidates = new LineageInferer().Infer(new OfficeScanner().Scan(folder).Documents).Candidates.Where(candidate => string.Equals(candidate.To, version.Path, StringComparison.OrdinalIgnoreCase) && (parent is null || candidate.From != parent.From)).OrderByDescending(candidate => candidate.Confidence).Take(5).ToArray();
        var people = analysis.Participants.Where(person => person.Evidence.Any(evidence => string.Equals(evidence.DocumentVersion, version.Path, StringComparison.OrdinalIgnoreCase))).ToArray();
        var warnings = analysis.Warnings.Concat(analysis.UnsupportedFeatures).Where(warning => warning.Contains(Path.GetFileName(version.Path), StringComparison.OrdinalIgnoreCase) || parent?.Warnings.Contains(warning) == true).Distinct().ToArray();
        return new ExplainResult(version.Path, family?.Id, parent, candidates, people, warnings);
    }
    private static IEnumerable<DocumentFamily> Families(IReadOnlyList<OfficeDocumentProfile> documents, LineageResult lineage)
    {
        var parent = documents.ToDictionary(d => d.Path, d => d.Path, StringComparer.OrdinalIgnoreCase);
        string Find(string x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
        void Union(string a, string b) { var x = Find(a); var y = Find(b); if (x != y) parent[y] = x; }
        foreach (var edge in lineage.Edges) Union(edge.From, edge.To); foreach (var duplicate in lineage.Duplicates) foreach (var path in duplicate.Paths.Skip(1)) Union(duplicate.CanonicalPath, path);
        return documents.GroupBy(d => Find(d.Path), StringComparer.OrdinalIgnoreCase).Select((group, index) => new DocumentFamily($"family-{index + 1}", group.First().Kind, group.Select(d => d.Path).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(), group.Count() > 1 ? "lineage/duplicate/content-evidence" : "no corroborated relation")).OrderBy(f => f.Id, StringComparer.Ordinal).ToArray();
    }
}
