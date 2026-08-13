using GitIt.Core;
using GitIt.GroundTruth;
using Xunit;

namespace GitIt.Tests;

public sealed class GroundTruthTests
{
    [Fact]
    public void Duplicate_files_are_collapsed_by_cryptographic_hash()
    {
        var dataset = new GroundTruthGenerator().Create();
        var result = new LineageInferer().Infer(new OfficeScanner().Scan(dataset.Root).Documents);

        var group = Assert.Single(result.Duplicates, duplicate => duplicate.Paths.Count == 3);
        Assert.Contains(dataset.Versions.Single(version => version.Id == "main-05").Path, group.Paths);
    }

    [Fact]
    public void Copy_paste_rebuild_is_related_but_not_asserted_as_parent_child()
    {
        var dataset = new GroundTruthGenerator().Create();
        var profiles = new OfficeScanner().Scan(dataset.Root).Documents;
        var result = new LineageInferer().Infer(profiles);
        var copied = dataset.Versions.Single(version => version.Id == "copy-paste").Path;

        Assert.DoesNotContain(result.Edges, edge => edge.To == copied);
        Assert.Contains(result.Candidates, candidate => candidate.To == copied && candidate.Status == LineageStatus.RelatedButUnproven);
    }

    [Fact]
    public void Spreadsheet_and_presentation_semantic_diffs_are_located()
    {
        var dataset = new GroundTruthGenerator().Create();
        var scanner = new OfficeScanner();
        var xlsx = new SemanticDiffer().Compare(scanner.Read(Path.Combine(dataset.Root, "spreadsheets", "data-v1.xlsx")), scanner.Read(Path.Combine(dataset.Root, "spreadsheets", "data-v2.xlsx")));
        var pptx = new SemanticDiffer().Compare(scanner.Read(Path.Combine(dataset.Root, "slides", "deck-v1.pptx")), scanner.Read(Path.Combine(dataset.Root, "slides", "deck-v2.pptx")));

        Assert.Contains(xlsx.Changes, change => change.Location == "Sheet2!F1" && change.Category == "cell");
        Assert.Contains(xlsx.Changes, change => change.Location == "Sheet2!F1" && change.Category == "format");
        Assert.Contains(pptx.Changes, change => change.Location == "Slide 1 / Shape Title 1" && change.Category == "text");
        Assert.Contains(pptx.Changes, change => change.Location == "Slide 1 / Shape Title 1" && change.Category == "format");
    }

    [Fact]
    public void Project_json_contract_keeps_analysis_and_participation_separate()
    {
        var dataset = new GroundTruthGenerator().Create();
        var analysis = new ProjectAnalyzer().Analyze(dataset.Root, includeEdgeDiffs: true);

        Assert.Equal("GitIt Analysis Result v1", analysis.SchemaVersion);
        Assert.NotEmpty(analysis.Versions);
        Assert.NotEmpty(analysis.Participants);
        Assert.NotEmpty(analysis.Performance);
        Assert.NotEmpty(analysis.Changes);
    }

    [Fact]
    public void Candidate_retrieval_retains_generated_direct_parents_with_explanations()
    {
        var dataset = new GroundTruthGenerator().Create();
        var profiles = new OfficeScanner().Scan(dataset.Root).Documents;
        var retrieved = new CandidateRetriever().Retrieve(profiles);
        var child = dataset.Versions.Single(version => version.Id == "main-10");
        var parent = dataset.Versions.Single(version => version.Id == child.ParentId);

        var candidate = Assert.Single(retrieved, item => item.From.Path == parent.Path && item.To.Path == child.Path);
        Assert.NotEmpty(candidate.Evidence);
        Assert.Contains(candidate.Evidence, evidence => evidence.Type is "sharedRsid" or "distinctiveContent" or "coarseStructureBucket");
    }

    [Fact]
    public void Empty_real_world_manifest_is_valid_but_contains_no_claimed_samples()
    {
        var root = Path.Combine(Path.GetTempPath(), "gitit-empty-corpus", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "corpus.json"), "{\"schemaVersion\":\"GitIt Real World Corpus v1\",\"families\":[]}");

        var result = CorpusManifestLoader.Validate(root);

        Assert.True(result.IsValid);
        Assert.Equal(0, result.VersionCount);
    }
}
