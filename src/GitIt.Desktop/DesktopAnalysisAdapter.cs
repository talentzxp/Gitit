using GitIt.Core;

namespace GitIt.Desktop;

/// <summary>Thin UI adapter: analysis, confidence, lineage, diff, and people all remain owned by GitIt.Core.</summary>
public sealed record DesktopAnalysisSession(GitItAnalysisResult Analysis, IReadOnlyDictionary<string, OfficeDocumentProfile> Profiles, LineageResult Lineage);
public sealed record LocalGroupAnalysis(LineageResult Lineage, IReadOnlyDictionary<string, SemanticDiffResult> Diffs);

public sealed class DesktopAnalysisAdapter
{
    public DesktopAnalysisSession Analyze(string folder)
    {
        var scan = new OfficeScanner().Scan(folder);
        var analysis = new ProjectAnalyzer().Analyze(folder, includeEdgeDiffs: true);
        var lineage = new LineageInferer().Infer(scan.Documents);
        return new DesktopAnalysisSession(analysis, scan.Documents.ToDictionary(profile => profile.Path, StringComparer.OrdinalIgnoreCase), lineage);
    }

    /// <summary>Runs the unchanged Core candidate and lineage engine only for a user-managed group.</summary>
    public LocalGroupAnalysis AnalyzeGroup(IReadOnlyList<OfficeDocumentProfile> profiles)
    {
        var lineage = new LineageInferer().Infer(profiles);
        var pairs = lineage.Edges.Select(edge => (edge.From, edge.To))
            .Concat(lineage.Candidates.Select(candidate => (candidate.From, candidate.To)))
            .Distinct()
            .ToArray();
        var differ = new SemanticDiffer();
        var documents = profiles.ToDictionary(profile => profile.Path, StringComparer.OrdinalIgnoreCase);
        var diffs = pairs.ToDictionary(pair => PairKey(pair.From, pair.To), pair => differ.Compare(documents[pair.From], documents[pair.To]), StringComparer.OrdinalIgnoreCase);
        return new LocalGroupAnalysis(lineage, diffs);
    }

    public SemanticDiffResult Compare(OfficeDocumentProfile source, OfficeDocumentProfile target) => new SemanticDiffer().Compare(source, target);

    public static string PairKey(string source, string target) => $"{source}\u001F{target}";
}
