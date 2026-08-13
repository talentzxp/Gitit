using System.Text.Json;

namespace GitIt.Core;

public sealed record CorpusManifest(string SchemaVersion, IReadOnlyList<CorpusFamily> Families);
public sealed record CorpusFamily(string Id, string Format, IReadOnlyList<CorpusVersion> Versions, string? Description = null);
/// <summary>Ground-truth-only annotation. These fields are intentionally never passed to the lineage engine.</summary>
public sealed record CorpusVersion(
    string Id,
    string File,
    string? Parent,
    string? Editor = null,
    string? EditorVersion = null,
    string? Transfer = null,
    string? Operation = null,
    IReadOnlyList<CorpusExpectedChange>? ExpectedChanges = null);
public sealed record CorpusExpectedChange(string? Type = null, string? Target = null, string? Property = null, string? Description = null);
public sealed record CorpusValidationResult(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, int FamilyCount, int VersionCount);

public static class CorpusManifestLoader
{
    public static CorpusManifest Load(string corpusRoot)
    {
        var manifest = Path.Combine(corpusRoot, "corpus.json");
        if (!File.Exists(manifest)) throw new FileNotFoundException("Expected real-world corpus manifest.", manifest);
        return JsonSerializer.Deserialize<CorpusManifest>(File.ReadAllText(manifest), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Corpus manifest could not be read.");
    }

    /// <summary>Calls the engine before resolving manifest parent IDs; parent data is only exposed to the caller after analysis.</summary>
    public static GitItAnalysisResult AnalyzeWithoutAnswers(string corpusRoot) => new ProjectAnalyzer().Analyze(corpusRoot, includeEdgeDiffs: true);

    public static CorpusValidationResult Validate(string corpusRoot)
    {
        var errors = new List<string>(); var warnings = new List<string>(); CorpusManifest manifest;
        try { manifest = Load(corpusRoot); } catch (Exception ex) { return new CorpusValidationResult(false, new[] { ex.Message }, warnings, 0, 0); }
        if (manifest.SchemaVersion != "GitIt Real World Corpus v1") errors.Add("Unsupported or missing schemaVersion.");
        var allIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var count = 0;
        foreach (var family in manifest.Families)
        {
            if (!new[] { "docx", "xlsx", "pptx" }.Contains(family.Format, StringComparer.OrdinalIgnoreCase)) errors.Add($"{family.Id}: format must be docx, xlsx, or pptx.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var paths = new Dictionary<string, CorpusVersion>(StringComparer.OrdinalIgnoreCase);
            foreach (var version in family.Versions)
            {
                count++; if (!ids.Add(version.Id) || !allIds.Add($"{family.Id}/{version.Id}")) errors.Add($"{family.Id}: duplicate version id {version.Id}.");
                var fullPath = Path.GetFullPath(Path.Combine(corpusRoot, version.File));
                if (!fullPath.StartsWith(Path.GetFullPath(corpusRoot), StringComparison.OrdinalIgnoreCase)) errors.Add($"{family.Id}/{version.Id}: file escapes corpus root.");
                else if (!File.Exists(fullPath)) errors.Add($"{family.Id}/{version.Id}: file not found: {version.File}.");
                else if (!Path.GetExtension(fullPath).Equals($".{family.Format}", StringComparison.OrdinalIgnoreCase)) errors.Add($"{family.Id}/{version.Id}: file extension does not match declared format.");
                paths[version.Id] = version;
            }
            foreach (var version in family.Versions) if (version.Parent is not null && !ids.Contains(version.Parent)) errors.Add($"{family.Id}/{version.Id}: parent {version.Parent} is not a version in this family.");
            if (HasCycle(family.Versions)) errors.Add($"{family.Id}: parent ground truth contains a cycle.");
            foreach (var hashes in paths.Values.Where(version => File.Exists(Path.Combine(corpusRoot, version.File))).GroupBy(version => Hash(Path.Combine(corpusRoot, version.File)), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1)) warnings.Add($"{family.Id}: {hashes.Count()} identical files are declared as separate versions; verify whether they should be duplicates.");
        }
        return new CorpusValidationResult(errors.Count == 0, errors, warnings, manifest.Families.Count, count);
    }
    private static bool HasCycle(IEnumerable<CorpusVersion> versions)
    {
        var map = versions.ToDictionary(version => version.Id, version => version.Parent, StringComparer.OrdinalIgnoreCase);
        foreach (var id in map.Keys) { var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var current = id; while (map.GetValueOrDefault(current) is { } parent) { if (!seen.Add(current) || parent.Equals(id, StringComparison.OrdinalIgnoreCase)) return true; current = parent; } }
        return false;
    }
    private static string Hash(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
}
