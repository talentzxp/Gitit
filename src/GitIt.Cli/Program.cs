using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentFormat.OpenXml.Packaging;
using GitIt.Core;

var arguments = args.ToList();
var json = arguments.Remove("--json");
var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
try
{
    if (arguments.Count == 2 && arguments[0].Equals("analyze", StringComparison.OrdinalIgnoreCase))
    {
        var result = new ProjectAnalyzer().Analyze(arguments[1]);
        Write(result, json, PrintAnalysis);
        return 0;
    }
    if (arguments.Count == 2 && arguments[0].Equals("lineage", StringComparison.OrdinalIgnoreCase))
    {
        var result = new ProjectAnalyzer().Analyze(arguments[1], includeEdgeDiffs: true);
        Write(result, json, PrintLineage);
        return 0;
    }
    if (arguments.Count == 2 && arguments[0].Equals("people", StringComparison.OrdinalIgnoreCase))
    {
        var scan = new OfficeScanner().Scan(arguments[1]);
        var people = new PeopleAnalyzer().Analyze(scan.Documents);
        Write(people, json, PrintPeople);
        return scan.Issues.Count == 0 ? 0 : 2;
    }
    if (arguments.Count == 3 && arguments[0].Equals("explain", StringComparison.OrdinalIgnoreCase))
    {
        var result = new ProjectAnalyzer().Explain(arguments[1], arguments[2]);
        Write(result, json, PrintExplain);
        return 0;
    }
    if (arguments.Count == 3 && arguments[0].Equals("diff", StringComparison.OrdinalIgnoreCase))
    {
        var scanner = new OfficeScanner();
        var result = new SemanticDiffer().Compare(scanner.Read(arguments[1]), scanner.Read(arguments[2]));
        Write(result, json, PrintDiff);
        return 0;
    }
    if (arguments.Count == 3 && arguments[0].Equals("corpus", StringComparison.OrdinalIgnoreCase) && arguments[1].Equals("validate", StringComparison.OrdinalIgnoreCase))
    {
        var result = CorpusManifestLoader.Validate(arguments[2]);
        Write(result, json, PrintCorpusValidation);
        return result.IsValid ? 0 : 2;
    }
    PrintUsage();
    return 64;
}
catch (Exception ex) when (ex is ArgumentException or DirectoryNotFoundException or FileNotFoundException or IOException or OpenXmlPackageException or InvalidDataException)
{
    Console.Error.WriteLine($"gitit: {ex.Message}");
    return 1;
}

void Write<T>(T value, bool useJson, Action<T> printer)
{
    if (useJson) Console.WriteLine(JsonSerializer.Serialize(value, jsonOptions)); else printer(value);
}

static void PrintAnalysis(GitItAnalysisResult result)
{
    Console.WriteLine($"GitIt {result.Project.EngineVersion}: {result.Versions.Count} files, {result.DocumentFamilies.Count} family/families.");
    Console.WriteLine($"Lineage edges: {result.Edges.Count}; participants: {result.Participants.Count}; unsupported warnings: {result.UnsupportedFeatures.Count}.");
    foreach (var family in result.DocumentFamilies) Console.WriteLine($"  {family.Id} [{family.Kind}]: {family.VersionIds.Count} version node(s), {family.DetectionBasis}");
    PrintLineage(result);
}

static void PrintLineage(GitItAnalysisResult result)
{
    Console.WriteLine("\nLineage graph:");
    if (result.Edges.Count == 0) Console.WriteLine("  No corroborated parent edge. This is an allowed result.");
    foreach (var edge in result.Edges)
    {
        Console.WriteLine($"  {Path.GetFileName(edge.From)} -> {Path.GetFileName(edge.To)}  {edge.Confidence:P0} ({edge.Status})");
        foreach (var evidence in edge.Evidence) Console.WriteLine($"    [{evidence.Strength}] {evidence.Type}: {evidence.Detail}");
        foreach (var warning in edge.Warnings) Console.WriteLine($"    warning: {warning}");
    }
    foreach (var warning in result.Warnings) Console.WriteLine($"  warning: {warning}");
    if (result.Changes.Count > 0) Console.WriteLine($"\nDeep diffs generated: {result.Changes.Count}.");
}

static void PrintPeople(IReadOnlyList<ParticipantIdentity> people)
{
    Console.WriteLine("Participants");
    if (people.Count == 0) Console.WriteLine("  No Office identity strings found.");
    foreach (var person in people)
    {
        Console.WriteLine($"\n{person.DisplayName}");
        foreach (var evidence in person.Evidence) Console.WriteLine($"  {Path.GetFileName(evidence.DocumentVersion)}  {evidence.EvidenceType}  {evidence.Strength} — {evidence.Detail}");
    }
}

static void PrintDiff(SemanticDiffResult result)
{
    Console.WriteLine($"{result.Kind} semantic diff\n  {Path.GetFileName(result.SourcePath)} -> {Path.GetFileName(result.TargetPath)}");
    if (result.Changes.Count == 0) Console.WriteLine("  No supported semantic change detected.");
    foreach (var change in result.Changes) Console.WriteLine($"  [{change.Category}] {change.Location}: {change.Detail}{FormatValues(change.Before, change.After)}");
    foreach (var unsupported in result.UnsupportedFeatures) Console.WriteLine($"  unsupported: {unsupported}");
    Console.WriteLine($"\nAssessment: {result.Assessment}");
}

static void PrintExplain(ExplainResult result)
{
    Console.WriteLine($"File: {Path.GetFileName(result.File)}\nFamily: {result.FamilyId ?? "unlinked"}");
    if (result.MostLikelyParent is null) Console.WriteLine("Most likely parent: no corroborated parent (abstained).");
    else
    {
        Console.WriteLine($"Most likely parent: {Path.GetFileName(result.MostLikelyParent.From)}\nConfidence: {result.MostLikelyParent.Confidence:P0}\nDecision: {result.MostLikelyParent.Status}");
        foreach (var evidence in result.MostLikelyParent.Evidence) Console.WriteLine($"  [{evidence.Strength}] {evidence.Detail}");
        foreach (var warning in result.MostLikelyParent.Warnings) Console.WriteLine($"  conflict: {warning}");
    }
    if (result.Alternatives.Count > 0) { Console.WriteLine("Alternatives:"); foreach (var alternative in result.Alternatives) Console.WriteLine($"  {Path.GetFileName(alternative.From)} {alternative.Confidence:P0} ({alternative.Status})"); }
    if (result.Participants.Count > 0) { Console.WriteLine("Participants:"); foreach (var person in result.Participants) foreach (var evidence in person.Evidence) Console.WriteLine($"  {person.DisplayName}: {evidence.EvidenceType} {evidence.Strength}"); }
    foreach (var warning in result.Warnings) Console.WriteLine($"warning: {warning}");
}

static void PrintCorpusValidation(CorpusValidationResult result)
{
    Console.WriteLine($"Corpus validation: {(result.IsValid ? "VALID" : "INVALID")}");
    Console.WriteLine($"Families: {result.FamilyCount}; versions: {result.VersionCount}");
    foreach (var error in result.Errors) Console.WriteLine($"  error: {error}");
    foreach (var warning in result.Warnings) Console.WriteLine($"  warning: {warning}");
}

static string FormatValues(string? before, string? after) => before is null && after is null ? string.Empty : $" ({before ?? "∅"} -> {after ?? "∅"})";

static void PrintUsage() => Console.WriteLine("GitIt v0.0.4 - evidence-led Office document lineage engine\nUsage:\n  gitit analyze <folder> [--json]\n  gitit lineage <folder> [--json]\n  gitit people <folder> [--json]\n  gitit explain <folder> <version-or-file> [--json]\n  gitit diff <fileA> <fileB> [--json]\n  gitit corpus validate <corpus-folder> [--json]");
