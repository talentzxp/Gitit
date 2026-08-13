using GitIt.Core;
using GitIt.GroundTruth;
using Xunit;

namespace GitIt.Tests;

public sealed class ReliabilityTests
{
    [Fact]
    public void Template_siblings_are_not_asserted_as_cross_city_parent_edges()
    {
        var dataset = new GroundTruthGenerator().CreateTemplateSiblingDataset();
        var result = new LineageInferer().Infer(new OfficeScanner().Scan(dataset.Root).Documents);
        var family = dataset.Versions.ToDictionary(version => version.Path, version => version.Family, StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain(result.Edges, edge => family.TryGetValue(edge.From, out var from) && family.TryGetValue(edge.To, out var to) && from != to);
    }

    [Fact]
    public void Lineage_result_is_a_dag()
    {
        var dataset = new GroundTruthGenerator().Create();
        var result = new LineageInferer().Infer(new OfficeScanner().Scan(dataset.Root).Documents);
        var graph = result.Edges.GroupBy(edge => edge.From).ToDictionary(group => group.Key, group => group.Select(edge => edge.To).ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var edge in result.Edges)
        {
            var pending = new Stack<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); pending.Push(edge.To);
            while (pending.Count > 0) { var current = pending.Pop(); Assert.NotEqual(edge.From, current); if (!seen.Add(current)) continue; foreach (var next in graph.GetValueOrDefault(current, Array.Empty<string>())) pending.Push(next); }
        }
    }

    [Fact]
    public void External_corpus_manifest_can_be_read_without_feeding_answers_to_engine()
    {
        var root = Path.Combine(Path.GetTempPath(), "gitit-corpus-manifest", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "corpus.json"), "{\"schemaVersion\":\"GitIt Real World Corpus v1\",\"families\":[]}");
        var manifest = CorpusManifestLoader.Load(root);

        Assert.Equal("GitIt Real World Corpus v1", manifest.SchemaVersion);
        Assert.Empty(manifest.Families);
    }

    [Fact]
    public void Manifest_preserves_real_world_environment_and_expected_change_annotations()
    {
        var root = Path.Combine(Path.GetTempPath(), "gitit-corpus-annotations", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "corpus.json"), """
        {"schemaVersion":"GitIt Real World Corpus v1","families":[{"id":"chain","format":"docx","versions":[{"id":"v1","file":"missing.docx","parent":null,"editor":"WPS","transfer":"WeChat","operation":"edit-and-save","expectedChanges":[{"type":"format","target":"Normal","property":"lineSpacing"}]}]}]}
        """);

        var version = Assert.Single(Assert.Single(CorpusManifestLoader.Load(root).Families).Versions);

        Assert.Equal("WPS", version.Editor);
        Assert.Equal("WeChat", version.Transfer);
        Assert.Equal("lineSpacing", Assert.Single(version.ExpectedChanges!).Property);
    }
}
