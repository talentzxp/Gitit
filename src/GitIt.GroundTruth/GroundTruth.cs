using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Spreadsheet;
using GitIt.Core;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Word = DocumentFormat.OpenXml.Wordprocessing;

namespace GitIt.GroundTruth;

public sealed record GroundTruthVersion(string Id, string Path, string Family, string? ParentId, bool ExpectsAbstention = false, bool IsDuplicate = false);
public sealed record GroundTruthDataset(string Root, IReadOnlyList<GroundTruthVersion> Versions);

/// <summary>Creates known lineage answers but exposes only Office files to the engine.</summary>
public sealed class GroundTruthGenerator
{
    public GroundTruthDataset CreateTemplateSiblingDataset(string? root = null)
    {
        root ??= Path.Combine(Path.GetTempPath(), "gitit-template-siblings", Guid.NewGuid().ToString("N"));
        var versions = new List<GroundTruthVersion>(); var time = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var city in new[] { "Nanjing", "Suzhou", "Wuxi" })
        {
            string? parent = null; var total = city == "Suzhou" ? 2 : 3;
            for (var number = 1; number <= total; number++)
            {
                var path = Path.Combine(root, "docx", $"{city}-report-v{number}.docx");
                CreateDocx(path, $"Monthly municipal template. Fixed section: scope, method, KPI, review. City-specific entity {city}. Project facts {city} depot budget marker {number}.", "TemplateTeam", "TemplateTeam", time.AddHours(number), Enumerable.Range(1, number).Select(value => $"{city[..2].ToUpperInvariant()}{value:X6}").ToArray(), number);
                var id = $"{city.ToLowerInvariant()}-{number}"; versions.Add(new GroundTruthVersion(id, path, city, parent)); parent = id;
            }
        }
        CreateTemplateXlsx(root); CreateTemplatePptx(root);
        return new GroundTruthDataset(root, versions);
    }
    public GroundTruthDataset Create(string? root = null)
    {
        root ??= Path.Combine(Path.GetTempPath(), "gitit-ground-truth", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var versions = new List<GroundTruthVersion>();
        var baseTime = new DateTime(2025, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var names = new[] { "报告.docx", "报告修改.docx", "专家修改稿.docx", "最终.docx", "最终2.docx", "最终最终版.docx", "abc123.docx" };
        string? parent = null;
        for (var number = 1; number <= 20; number++)
        {
            var id = $"main-{number:00}";
            var relative = Path.Combine("main", number <= names.Length ? names[number - 1] : $"artifact_{number:00}.docx");
            var path = Path.Combine(root, relative);
            var modified = baseTime.AddHours(number);
            if (number == 8) modified = baseTime.AddHours(2); // deliberately earlier than v7
            var creator = number == 13 ? null : "Alice";
            var lastModified = number == 13 ? null : "Alice";
            CreateDocx(path, BuildReport(number), creator, lastModified, modified, Enumerable.Range(1, number).Select(value => $"A1B2{value:X4}").ToArray(), number == 13 ? 0 : number);
            File.SetLastWriteTimeUtc(path, number == 8 ? baseTime.AddDays(4) : modified.ToUniversalTime()); // mismatch at v8
            versions.Add(new GroundTruthVersion(id, path, "main", parent));
            parent = id;
        }

        var branchParent = versions.Single(v => v.Id == "main-08");
        var expertPath = Path.Combine(root, "branches", "专家修改稿.docx");
        CreateDocx(expertPath, BuildReport(8) + "\nExpert branch: reviewed assumptions.", "Alice", "Expert", baseTime.AddHours(9), Enumerable.Range(1, 9).Select(value => $"A1B2{value:X4}").ToArray(), 10);
        versions.Add(new GroundTruthVersion("branch-expert", expertPath, "main", branchParent.Id));
        var finalPath = Path.Combine(root, "branches", "最终最终版.docx");
        CreateDocx(finalPath, BuildReport(8) + "\nAlternative branch: presentation-oriented summary.", "Alice", "Editor B", baseTime.AddHours(10), Enumerable.Range(1, 8).Select(value => $"A1B2{value:X4}").Append("BRANCH002").ToArray(), 10);
        versions.Add(new GroundTruthVersion("branch-final", finalPath, "main", branchParent.Id));

        var duplicateSource = versions.Single(v => v.Id == "main-05");
        foreach (var relative in new[] { Path.Combine("backup", "report.docx"), Path.Combine("wechat", "report.docx") })
        {
            var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.Copy(duplicateSource.Path, path);
            versions.Add(new GroundTruthVersion($"duplicate-{versions.Count}", path, "main", null, false, true));
        }

        var copiedPath = Path.Combine(root, "reconstructed", "copy-paste-rebuild.docx");
        CreateDocx(copiedPath, BuildReport(10), null, null, baseTime.AddDays(2), new[] { "FFFFEEEE" }, 0); // content relation without provenance
        versions.Add(new GroundTruthVersion("copy-paste", copiedPath, "main", null, true));

        var largeRewritePath = Path.Combine(root, "large-rewrite", "executive-rewrite.docx");
        CreateDocx(largeRewritePath, BuildLargeRewrite(), "Alice", "Alice", baseTime.AddDays(3), Enumerable.Range(1, 21).Select(value => $"A1B2{value:X4}").ToArray(), 21, tableRows: 12);
        versions.Add(new GroundTruthVersion("large-rewrite", largeRewritePath, "main", null, true));

        var unrelatedPath = Path.Combine(root, "unrelated", "abc123.docx");
        CreateDocx(unrelatedPath, "Independent procurement record. Vendor terms, delivery quantities, and payment approval.", "Other", "Other", baseTime.AddDays(1), new[] { "11223344" }, 0);
        versions.Add(new GroundTruthVersion("unrelated", unrelatedPath, "unrelated", null, true));

        CreateSpreadsheetPair(root);
        CreatePresentationPair(root);
        versions.Add(new GroundTruthVersion("xlsx-01", Path.Combine(root, "spreadsheets", "data-v1.xlsx"), "workbook", null));
        versions.Add(new GroundTruthVersion("xlsx-02", Path.Combine(root, "spreadsheets", "data-v2.xlsx"), "workbook", "xlsx-01"));
        versions.Add(new GroundTruthVersion("pptx-01", Path.Combine(root, "slides", "deck-v1.pptx"), "presentation", null));
        versions.Add(new GroundTruthVersion("pptx-02", Path.Combine(root, "slides", "deck-v2.pptx"), "presentation", "pptx-01"));
        return new GroundTruthDataset(root, versions);
    }

    public void CreatePerformanceFiles(string root, int count)
    {
        Directory.CreateDirectory(root);
        var time = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= count; i++) CreateDocx(Path.Combine(root, $"performance-{i:000}.docx"), $"Independent performance document {i}. A stable content block with unique token {i}." , "Bench", "Bench", time.AddMinutes(i), new[] { $"P{i:X8}" }, 0);
    }

    private static string BuildReport(int number) => $"Quarterly operating report. This stable context describes project scope, methods, risks, decisions, and delivery evidence. The report retains these paragraphs through the full version history. Version marker {number} changes one number while preserving the document lineage.";
    private static string BuildLargeRewrite() => string.Join("\n", Enumerable.Range(1, 18).Select(index => $"Executive rewrite section {index}. Prior chapters were removed and the content was reorganized around a new scenario, new table model, and revised operating assumptions."));
    private static void CreateDocx(string path, string content, string? creator, string? lastModifiedBy, DateTime modified, IReadOnlyList<string> rsids, int revisionCount, int tableRows = 1)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart(); var body = new Word.Body();
        foreach (var text in content.Split('\n')) body.Append(Paragraph(text, rsids.Last()));
        foreach (var rsid in rsids) body.Append(Paragraph(string.Empty, rsid));
        var table = new Word.Table();
        for (var row = 1; row <= tableRows; row++) table.Append(new Word.TableRow(new Word.TableCell(new Word.Paragraph(new Word.Run(new Word.Text($"Metric {row}")))), new Word.TableCell(new Word.Paragraph(new Word.Run(new Word.Text($"Value {row}"))))));
        body.Append(table);
        if (revisionCount > 0)
        {
            var insertion = new Word.InsertedRun { Author = "Alice", Date = modified, Id = revisionCount.ToString() };
            insertion.Append(new Word.Run(new Word.Text($"Revision session {revisionCount}."))); body.Append(insertion);
        }
        body.Append(new Word.SectionProperties()); main.Document = new Word.Document(body);
        if (revisionCount > 0)
        {
            var comments = main.AddNewPart<WordprocessingCommentsPart>();
            comments.Comments = new Word.Comments(new Word.Comment { Id = "0", Author = "Reviewer", Initials = "R", Date = modified });
        }
        document.PackageProperties.Creator = creator; document.PackageProperties.LastModifiedBy = lastModifiedBy; document.PackageProperties.Created = modified.AddHours(-1); document.PackageProperties.Modified = modified; document.PackageProperties.Revision = revisionCount.ToString();
    }
    private static Word.Paragraph Paragraph(string text, string rsid)
    {
        var paragraph = new Word.Paragraph(new Word.ParagraphProperties(new Word.ParagraphStyleId { Val = "Normal" }), new Word.Run(new Word.Text(text)));
        paragraph.SetAttribute(new OpenXmlAttribute("w", "rsidR", "http://schemas.openxmlformats.org/wordprocessingml/2006/main", rsid)); return paragraph;
    }

    private static void CreateSpreadsheetPair(string root)
    {
        CreateXlsx(Path.Combine(root, "spreadsheets", "data-v1.xlsx"), "13.24", "0.00", false);
        CreateXlsx(Path.Combine(root, "spreadsheets", "data-v2.xlsx"), "13.42", "0.0", true);
    }
    private static void CreateXlsx(string path, string value, string numberFormat, bool secondSheet)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart(); var styles = workbook.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = new Stylesheet(new NumberingFormats(new NumberingFormat { NumberFormatId = 164U, FormatCode = numberFormat }) { Count = 1U }, new Fonts(new DocumentFormat.OpenXml.Spreadsheet.Font()) { Count = 1U }, new Fills(new Fill()) { Count = 1U }, new Borders(new Border()) { Count = 1U }, new CellStyleFormats(new CellFormat()) { Count = 1U }, new CellFormats(new CellFormat { NumberFormatId = 164U, ApplyNumberFormat = true }) { Count = 1U });
        var row = new Row { RowIndex = 1U, Height = secondSheet ? 24 : 18, CustomHeight = true }; row.Append(new Cell { CellReference = "F1", CellValue = new CellValue(value), StyleIndex = 0U });
        var first = workbook.AddNewPart<WorksheetPart>(); first.Worksheet = new Worksheet(new Columns(new DocumentFormat.OpenXml.Spreadsheet.Column { Min = 6U, Max = 6U, Width = secondSheet ? 18 : 12, CustomWidth = true }), new SheetData(row), new MergeCells(new MergeCell { Reference = secondSheet ? "A1:B1" : "A1:A1" }));
        var sheets = new Sheets(new Sheet { Id = workbook.GetIdOfPart(first), SheetId = 1U, Name = "Sheet2" });
        if (secondSheet) { var extra = workbook.AddNewPart<WorksheetPart>(); extra.Worksheet = new Worksheet(new SheetData()); sheets.Append(new Sheet { Id = workbook.GetIdOfPart(extra), SheetId = 2U, Name = "Added" }); }
        workbook.Workbook = new Workbook(sheets); document.PackageProperties.Creator = "Alice";
    }

    private static void CreatePresentationPair(string root)
    {
        CreatePptx(Path.Combine(root, "slides", "deck-v1.pptx"), "Baseline title", 2400, 1000000L);
        CreatePptx(Path.Combine(root, "slides", "deck-v2.pptx"), "Revised title", 2200, 1200000L);
    }
    private static void CreateTemplateXlsx(string root)
    {
        foreach (var city in new[] { "Nanjing", "Suzhou", "Wuxi" }) CreateXlsx(Path.Combine(root, "xlsx", $"{city}-data.xlsx"), city == "Nanjing" ? "101" : city == "Suzhou" ? "202" : "303", "0.00", false);
    }
    private static void CreateTemplatePptx(string root)
    {
        foreach (var city in new[] { "Nanjing", "Suzhou", "Wuxi" }) CreatePptx(Path.Combine(root, "pptx", $"{city}-briefing.pptx"), $"{city} project briefing", 2400, 1000000L);
    }
    private static void CreatePptx(string path, string text, int fontSize, long x)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var document = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presentation = document.AddPresentationPart(); var slide = presentation.AddNewPart<SlidePart>();
        var title = new Shape(
            new NonVisualShapeProperties(new NonVisualDrawingProperties { Id = 2U, Name = "Title 1" }, new NonVisualShapeDrawingProperties(), new ApplicationNonVisualDrawingProperties()),
            new ShapeProperties(new Drawing.Transform2D(new Drawing.Offset { X = x, Y = 500000L }, new Drawing.Extents { Cx = 4000000L, Cy = 700000L })),
            new TextBody(new Drawing.BodyProperties(), new Drawing.ListStyle(), new Drawing.Paragraph(new Drawing.Run(new Drawing.RunProperties { FontSize = fontSize, Bold = true }, new Drawing.Text(text)))));
        slide.Slide = new Slide(new CommonSlideData(new ShapeTree(new NonVisualGroupShapeProperties(new NonVisualDrawingProperties { Id = 1U, Name = "" }, new NonVisualGroupShapeDrawingProperties(), new ApplicationNonVisualDrawingProperties()), new GroupShapeProperties(), title)));
        presentation.Presentation = new Presentation(new SlideIdList(new SlideId { Id = 256U, RelationshipId = presentation.GetIdOfPart(slide) }), new SlideSize { Cx = 9144000, Cy = 6858000, Type = SlideSizeValues.Screen4x3 }, new NotesSize { Cx = 6858000, Cy = 9144000 }); document.PackageProperties.Creator = "Alice";
    }
}
