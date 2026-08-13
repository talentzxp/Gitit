using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Word = DocumentFormat.OpenXml.Wordprocessing;

namespace GitIt.Tests.Fixtures;

internal static class SampleOfficeFactory
{
    public static SampleOfficeSet Create()
    {
        var folder = Path.Combine(Path.GetTempPath(), "gitit-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var v1 = Path.Combine(folder, "report_v1.docx");
        var v2 = Path.Combine(folder, "report_v2.docx");
        CreateDocx(v1, "Alice", "Alice", new DateTime(2025, 1, 1, 8, 0, 0, DateTimeKind.Utc), "Baseline report", "Methods and results are stable.", "Normal", 2, false);
        CreateDocx(v2, "Alice", "Reviewer", new DateTime(2025, 1, 2, 8, 0, 0, DateTimeKind.Utc), "Baseline report", "Methods and results were revised with new evidence.", "Quote", 3, true);
        CreateXlsx(Path.Combine(folder, "data_v1.xlsx"));
        CreatePptx(Path.Combine(folder, "slides_v1.pptx"));
        return new SampleOfficeSet(folder, v1, v2);
    }

    private static void CreateDocx(string path, string creator, string lastModifiedBy, DateTime modified, string title, string bodyText, string style, int tableRows, bool trackedChange)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        var body = new Word.Body();
        body.Append(CreateParagraph(title, "Title", "00A1"));
        body.Append(CreateParagraph(bodyText, style, "00A1"));
        if (trackedChange)
        {
            var insertion = new Word.InsertedRun
            {
                Author = "Reviewer",
                Date = modified,
                Id = "1",
            };
            insertion.Append(new Word.Run(new Word.Text("Tracked addition.")));
            body.Append(insertion);
        }
        var table = new Word.Table();
        for (var i = 0; i < tableRows; i++)
        {
            table.Append(new Word.TableRow(
                new Word.TableCell(new Word.Paragraph(new Word.Run(new Word.Text($"R{i + 1}C1")))),
                new Word.TableCell(new Word.Paragraph(new Word.Run(new Word.Text($"R{i + 1}C2"))))));
        }
        body.Append(table);
        body.Append(new Word.SectionProperties());
        mainPart.Document = new Word.Document(body);
        var commentsPart = mainPart.AddNewPart<WordprocessingCommentsPart>();
        commentsPart.Comments = new Word.Comments(new Word.Comment
        {
            Id = "0",
            Author = trackedChange ? "Reviewer" : "Alice",
            Initials = "R",
            Date = modified,
        });
        document.PackageProperties.Creator = creator;
        document.PackageProperties.LastModifiedBy = lastModifiedBy;
        document.PackageProperties.Title = "GitIt reproducible sample";
        document.PackageProperties.Created = modified.AddHours(-1);
        document.PackageProperties.Modified = modified;
        document.PackageProperties.Revision = trackedChange ? "2" : "1";
    }

    private static Word.Paragraph CreateParagraph(string text, string style, string rsid)
    {
        var paragraph = new Word.Paragraph(new Word.ParagraphProperties(new Word.ParagraphStyleId { Val = style }), new Word.Run(new Word.Text(text)));
        paragraph.SetAttribute(new OpenXmlAttribute("w", "rsidR", "http://schemas.openxmlformats.org/wordprocessingml/2006/main", rsid));
        return paragraph;
    }

    private static void CreateXlsx(string path)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        worksheetPart.Worksheet = new Worksheet(new SheetData(new Row(new Cell { CellValue = new CellValue("42") })));
        workbookPart.Workbook = new Workbook(new Sheets(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1U, Name = "Data" }));
        document.PackageProperties.Creator = "Alice";
    }

    private static void CreatePptx(string path)
    {
        using var document = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presentationPart = document.AddPresentationPart();
        presentationPart.Presentation = new DocumentFormat.OpenXml.Presentation.Presentation();
        document.PackageProperties.Creator = "Alice";
    }
}

internal sealed record SampleOfficeSet(string Folder, string V1, string V2) : IDisposable
{
    public void Dispose()
    {
        if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
    }
}
