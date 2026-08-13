using GitIt.Core;

namespace GitIt.Desktop;

/// <summary>Thin UI adapter: analysis, confidence, lineage, diff, and people all remain owned by GitIt.Core.</summary>
public sealed record DesktopAnalysisSession(GitItAnalysisResult Analysis, IReadOnlyDictionary<string, OfficeDocumentProfile> Profiles, LineageResult Lineage);

public sealed class DesktopAnalysisAdapter
{
    public DesktopAnalysisSession Analyze(string folder)
    {
        var scan = new OfficeScanner().Scan(folder);
        var analysis = new ProjectAnalyzer().Analyze(folder, includeEdgeDiffs: true);
        var lineage = new LineageInferer().Infer(scan.Documents);
        return new DesktopAnalysisSession(analysis, scan.Documents.ToDictionary(profile => profile.Path, StringComparer.OrdinalIgnoreCase), lineage);
    }
}
