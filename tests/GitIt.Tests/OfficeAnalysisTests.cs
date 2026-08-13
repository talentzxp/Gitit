using GitIt.Core;
using GitIt.Tests.Fixtures;
using Xunit;

namespace GitIt.Tests;

public sealed class OfficeAnalysisTests
{
    [Fact]
    public void Scan_reads_all_supported_types_and_docx_evidence()
    {
        using var samples = SampleOfficeFactory.Create();
        var result = new OfficeScanner().Scan(samples.Folder);

        Assert.Empty(result.Issues);
        Assert.Equal(4, result.Documents.Count);
        Assert.Contains(result.Documents, d => d.Kind == OfficeFileKind.Xlsx && d.Fingerprint["sheets"] == "1");
        Assert.Contains(result.Documents, d => d.Kind == OfficeFileKind.Pptx && d.Fingerprint["slides"] == "0");
        var v2 = Assert.Single(result.Documents, d => d.Path == samples.V2);
        Assert.Contains("00A1", v2.Docx!.Rsids);
        Assert.Equal(1, v2.Docx.RevisionKinds["ins"]);
        Assert.Contains(v2.ParticipantEvidence, evidence => evidence.Value == "Reviewer" && evidence.EvidenceType == "revision-author");
        Assert.Contains("Reviewer", v2.Docx.CommentAuthors);
    }

    [Fact]
    public void Diff_reports_content_format_structure_and_source_evidence()
    {
        using var samples = SampleOfficeFactory.Create();
        var scanner = new OfficeScanner();
        var diff = new SemanticDiffer().Compare(scanner.Read(samples.V1), scanner.Read(samples.V2));

        Assert.Contains(diff.Changes, change => change.Category == "content");
        Assert.Contains(diff.Changes, change => change.Category == "format");
        Assert.Contains(diff.Changes, change => change.Category == "structure");
        Assert.Contains(diff.SourceEvidence, e => e.Type == "rsidEvidence");
    }

    [Fact]
    public void Lineage_selects_a_supported_parent_and_keeps_evidence()
    {
        using var samples = SampleOfficeFactory.Create();
        var scan = new OfficeScanner().Scan(samples.Folder);
        var lineage = new LineageInferer().Infer(scan.Documents);

        var edge = Assert.Single(lineage.Edges, e => e.To == samples.V2);
        Assert.Equal(samples.V1, edge.From);
        Assert.True(edge.Confidence >= 0.50);
        Assert.NotEmpty(edge.Evidence);
    }

    [Fact]
    public void Lineage_keeps_documents_unlinked_when_no_signal_reaches_threshold()
    {
        var first = Profile("alpha.docx", "alpha vocabulary only", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var second = Profile("beta.docx", "beta language unrelated", new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero));

        var result = new LineageInferer().Infer(new[] { first, second });

        Assert.Empty(result.Edges);
        Assert.Equal(2, result.UncertainDocuments.Count);
    }

    private static OfficeDocumentProfile Profile(string path, string text, DateTimeOffset modified) => new(
        path, OfficeFileKind.Docx, 0, modified, path,
        new CommonOfficeMetadata(null, null, null, null, modified, null),
        new Dictionary<string, string>(), Array.Empty<ParticipantEvidence>(), Array.Empty<Evidence>(), Array.Empty<string>(),
        new DocxDetails(new[] { new ParagraphFingerprint(0, text, "text", "Normal", "format") }, Array.Empty<TableFingerprint>(),
            Array.Empty<string>(), new Dictionary<string, int>(), Array.Empty<string>(), Array.Empty<string>(), "body", "style"));
}
