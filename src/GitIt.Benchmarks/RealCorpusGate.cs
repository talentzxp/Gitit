using System.Text.Json;
using GitIt.Core;

internal sealed record RealCorpusEdgeResult(
    string Family,
    string GroundTruth,
    string Outcome,
    string? Predicted,
    double? Confidence,
    string Detail);

internal sealed record ExpectedChangeCheck(
    string Edge,
    string Expected,
    string Outcome,
    IReadOnlyList<string> MatchedChanges);

internal sealed record ParticipantObservation(
    string Version,
    string? Creator,
    string? LastModifiedBy,
    int RsidCount,
    int RevisionAuthorCount,
    int CommentAuthorCount);

internal sealed record ParticipantTransition(
    string Edge,
    string Creator,
    string LastModifiedBy,
    string Rsid,
    string RevisionAuthor,
    string CommentAuthor);

internal sealed record RealCorpusReview(
    string CorpusRoot,
    string GateStatus,
    CorpusValidationResult Validation,
    int DocxVersions,
    int XlsxVersions,
    int PptxVersions,
    int Families,
    int KnownParentEdges,
    int CorrectEdges,
    int WrongEdges,
    int AbstainedEdges,
    int HighConfidenceWrongEdges,
    IReadOnlyList<string> Environments,
    IReadOnlyList<RealCorpusEdgeResult> Lineage,
    IReadOnlyList<ExpectedChangeCheck> DiffChecks,
    IReadOnlyList<ParticipantObservation> Participants,
    IReadOnlyList<ParticipantTransition> ParticipantTransitions,
    IReadOnlyList<string> NewRealWorldIssues,
    string Recommendation,
    string ManualActionRequired);

/// <summary>Real-Corpus Gate runner. It deliberately runs scanning/inference before loading manifest answers.</summary>
internal static class RealCorpusGate
{
    public static RealCorpusReview Run(string corpusRoot)
    {
        corpusRoot = Path.GetFullPath(corpusRoot);
        var validation = CorpusManifestLoader.Validate(corpusRoot);
        var files = Directory.Exists(corpusRoot)
            ? Directory.EnumerateFiles(corpusRoot, "*.*", SearchOption.AllDirectories).Where(IsOffice).ToArray()
            : Array.Empty<string>();
        if (!validation.IsValid || files.Length == 0)
        {
            var reason = !validation.IsValid
                ? "Manifest validation must be fixed before the gate can run."
                : "No authorized manual Office files are present.";
            return Waiting(corpusRoot, validation, files, reason);
        }

        // Engine phase: no manifest is available to the scanner, retriever, scorer, or differ.
        var scan = new OfficeScanner().Scan(corpusRoot);
        var lineage = new LineageInferer().Infer(scan.Documents);
        var profiles = scan.Documents.ToDictionary(profile => Path.GetFullPath(profile.Path), StringComparer.OrdinalIgnoreCase);

        // Answer phase begins only after all engine output has been created.
        var manifest = CorpusManifestLoader.Load(corpusRoot);
        var annotation = manifest.Families.SelectMany(family => family.Versions.Select(version => (Family: family, Version: version, Path: Path.GetFullPath(Path.Combine(corpusRoot, version.File)))))
            .ToArray();
        var expected = annotation.Where(item => item.Version.Parent is not null)
            .Select(item => (item.Family, Child: item.Version, ChildPath: item.Path, Parent: item.Family.Versions.Single(version => version.Id.Equals(item.Version.Parent, StringComparison.OrdinalIgnoreCase))))
            .Select(item => (item.Family, item.Child, item.ChildPath, ParentPath: Path.GetFullPath(Path.Combine(corpusRoot, item.Parent.File))))
            .ToArray();
        var selectedByChild = lineage.Edges.ToDictionary(edge => Path.GetFullPath(edge.To), StringComparer.OrdinalIgnoreCase);
        var results = expected.Select(item => EvaluateEdge(item.Family.Id, item.ParentPath, item.ChildPath, selectedByChild.GetValueOrDefault(item.ChildPath))).ToList();
        var rootPaths = annotation.Where(item => item.Version.Parent is null).Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        results.AddRange(lineage.Edges.Where(edge => rootPaths.Contains(Path.GetFullPath(edge.To))).Select(edge => new RealCorpusEdgeResult("root", "(no parent)", "WRONG", $"{Short(edge.From)} -> {Short(edge.To)}", edge.Confidence, "GitIt asserted a parent for a manifest root.")));

        var diffs = expected.Where(item => profiles.ContainsKey(item.ParentPath) && profiles.ContainsKey(item.ChildPath))
            .ToDictionary(item => (item.ParentPath, item.ChildPath), item => new SemanticDiffer().Compare(profiles[item.ParentPath], profiles[item.ChildPath]));
        var diffChecks = expected.SelectMany(item => CheckExpectedChanges(item.Child.ExpectedChanges, $"{Short(item.ParentPath)} -> {Short(item.ChildPath)}", diffs.GetValueOrDefault((item.ParentPath, item.ChildPath)))).ToArray();
        var observations = annotation.Where(item => profiles.ContainsKey(item.Path)).Select(item => Observe(item.Version.Id, profiles[item.Path])).ToArray();
        var transitions = expected.Where(item => profiles.ContainsKey(item.ParentPath) && profiles.ContainsKey(item.ChildPath)).Select(item => CompareParticipants($"{Short(item.ParentPath)} -> {Short(item.ChildPath)}", profiles[item.ParentPath], profiles[item.ChildPath])).ToArray();
        var environments = annotation.Select(item => DescribeEnvironment(item.Version)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        var issues = scan.Issues.Select(issue => $"Scanner: {Short(issue.Path)} — {issue.Message}")
            .Concat(scan.Documents.SelectMany(document => document.UnsupportedFeatures.Select(feature => $"Unsupported: {Short(document.Path)} — {feature}")))
            .Concat(diffChecks.Where(check => check.Outcome == "MISSED EXPECTED CHANGE").Select(check => $"Diff: {check.Edge} — {check.Expected}"))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var correct = results.Count(result => result.Outcome == "CORRECT");
        var wrong = results.Count(result => result.Outcome == "WRONG");
        var abstained = results.Count(result => result.Outcome == "ABSTAINED");
        var highWrong = results.Count(result => result.Outcome == "WRONG" && result.Confidence >= new LineageWeights().HighConfidenceThreshold);
        var insufficient = annotation.Length < 20;
        var recommendation = highWrong > 0 || diffChecks.Any(check => check.Outcome == "MISSED EXPECTED CHANGE")
            ? "FIX CORE AND REPEAT REAL CORPUS GATE"
            : insufficient ? "WAITING FOR MANUAL CORPUS" : "ENTER PC ALPHA";
        var action = recommendation == "WAITING FOR MANUAL CORPUS"
            ? "Create the first 20–30 authorized Office versions using the Word/WPS/WeChat, Excel/WPS, and PowerPoint/WPS steps in real-world-corpus/README.md; then run `dotnet run --project src/GitIt.Benchmarks -- real real-world-corpus`."
            : recommendation == "FIX CORE AND REPEAT REAL CORPUS GATE" ? "Review every wrong or missed case before making a minimal, tested Core correction; then repeat the same manual corpus." : "No manual action is required for this gate.";
        return new RealCorpusReview(corpusRoot, "COMPLETED", validation, Count(annotation, "docx"), Count(annotation, "xlsx"), Count(annotation, "pptx"), manifest.Families.Count, expected.Length, correct, wrong, abstained, highWrong, environments, results, diffChecks, observations, transitions, issues, recommendation, action);
    }

    public static void WriteReport(RealCorpusReview review)
    {
        var output = Path.Combine(Environment.CurrentDirectory, "outputs"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "REAL_CORPUS_REVIEW.md"), Markdown(review));
        File.WriteAllText(Path.Combine(output, "real-corpus-review.json"), JsonSerializer.Serialize(review, Json.Options));
    }

    public static void Print(RealCorpusReview review)
    {
        Console.WriteLine("=== GitIt Real Corpus Gate Review ===\n");
        Console.WriteLine($"Corpus: {review.GateStatus}; {review.Families} families, {review.DocxVersions} DOCX, {review.XlsxVersions} XLSX, {review.PptxVersions} PPTX.");
        Console.WriteLine($"Lineage: known {review.KnownParentEdges}; correct {review.CorrectEdges}; wrong {review.WrongEdges}; abstained {review.AbstainedEdges}; high-confidence wrong {review.HighConfidenceWrongEdges}.");
        Console.WriteLine($"Diff: detected {review.DiffChecks.Count(check => check.Outcome == "EXPECTED CHANGE DETECTED")}; missed {review.DiffChecks.Count(check => check.Outcome == "MISSED EXPECTED CHANGE")}; unannotated {review.DiffChecks.Count(check => check.Outcome == "NOT ANNOTATED")}.");
        Console.WriteLine($"Participants: {review.Participants.Count} observed versions; {review.ParticipantTransitions.Count} transitions compared.");
        Console.WriteLine($"New real-world issues: {(review.NewRealWorldIssues.Count == 0 ? "none observed" : string.Join(" | ", review.NewRealWorldIssues.Take(3)))}");
        Console.WriteLine($"Recommendation: {review.Recommendation}");
        Console.WriteLine($"Manual action required: {review.ManualActionRequired}");
    }

    private static RealCorpusReview Waiting(string root, CorpusValidationResult validation, IReadOnlyList<string> files, string reason) => new(root, "WAITING FOR MANUAL FILE CREATION", validation, files.Count(path => path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)), files.Count(path => path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)), files.Count(path => path.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase)), validation.FamilyCount, 0, 0, 0, 0, 0, Array.Empty<string>(), Array.Empty<RealCorpusEdgeResult>(), Array.Empty<ExpectedChangeCheck>(), Array.Empty<ParticipantObservation>(), Array.Empty<ParticipantTransition>(), validation.Errors.Concat(validation.Warnings).Append(reason).ToArray(), "WAITING FOR MANUAL CORPUS", "MANUAL_ACTION_REQUIRED: personally create authorized Word/WPS/WeChat, Excel/WPS, and PowerPoint/WPS files following real-world-corpus/README.md, update corpus.json, validate it, then run `dotnet run --project src/GitIt.Benchmarks -- real real-world-corpus`.");

    private static RealCorpusEdgeResult EvaluateEdge(string family, string parent, string child, LineageEdge? prediction) => prediction is null
        ? new RealCorpusEdgeResult(family, $"{Short(parent)} -> {Short(child)}", "ABSTAINED", null, null, "No asserted parent edge; this is reported separately from a wrong edge.")
        : Path.GetFullPath(prediction.From).Equals(parent, StringComparison.OrdinalIgnoreCase)
            ? new RealCorpusEdgeResult(family, $"{Short(parent)} -> {Short(child)}", "CORRECT", $"{Short(prediction.From)} -> {Short(prediction.To)}", prediction.Confidence, prediction.Status.ToString())
            : new RealCorpusEdgeResult(family, $"{Short(parent)} -> {Short(child)}", "WRONG", $"{Short(prediction.From)} -> {Short(prediction.To)}", prediction.Confidence, "A different parent was asserted for this child.");

    private static IEnumerable<ExpectedChangeCheck> CheckExpectedChanges(IReadOnlyList<CorpusExpectedChange>? expected, string edge, SemanticDiffResult? diff)
    {
        if (expected is null || expected.Count == 0) return new[] { new ExpectedChangeCheck(edge, "No structured expectedChanges annotation", "NOT ANNOTATED", Array.Empty<string>()) };
        return expected.Select(change =>
        {
            var label = string.Join(" / ", new[] { change.Type, change.Target, change.Property, change.Description }.Where(value => !string.IsNullOrWhiteSpace(value)));
            var matches = diff?.Changes.Where(actual => Matches(change, actual)).Select(actual => $"[{actual.Category}] {actual.Location}: {actual.Detail}").ToArray() ?? Array.Empty<string>();
            return new ExpectedChangeCheck(edge, label, matches.Length > 0 ? "EXPECTED CHANGE DETECTED" : "MISSED EXPECTED CHANGE", matches);
        });
    }

    private static bool Matches(CorpusExpectedChange expected, DiffChange actual)
    {
        var text = $"{actual.Category} {actual.Location} {actual.Detail} {actual.Before} {actual.After}";
        return (string.IsNullOrWhiteSpace(expected.Type) || actual.Category.Contains(expected.Type, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(expected.Target) || text.Contains(expected.Target, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(expected.Property) || text.Contains(expected.Property, StringComparison.OrdinalIgnoreCase));
    }

    private static ParticipantObservation Observe(string version, OfficeDocumentProfile profile) => new(version, profile.Metadata.Creator, profile.Metadata.LastModifiedBy, profile.Docx?.Rsids.Count ?? 0, profile.Docx?.RevisionAuthors.Count ?? 0, profile.Docx?.CommentAuthors.Count ?? 0);
    private static ParticipantTransition CompareParticipants(string edge, OfficeDocumentProfile source, OfficeDocumentProfile target) => new(edge, Same(source.Metadata.Creator, target.Metadata.Creator), Same(source.Metadata.LastModifiedBy, target.Metadata.LastModifiedBy), Overlap(source.Docx?.Rsids, target.Docx?.Rsids), Overlap(source.Docx?.RevisionAuthors, target.Docx?.RevisionAuthors), Overlap(source.Docx?.CommentAuthors, target.Docx?.CommentAuthors));
    private static string Same(string? left, string? right) => string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right) ? "not present" : string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ? "preserved" : "changed or removed";
    private static string Overlap(IReadOnlyList<string>? left, IReadOnlyList<string>? right) => left is null && right is null ? "not applicable" : left is { Count: > 0 } && right is { Count: > 0 } && left.Intersect(right, StringComparer.OrdinalIgnoreCase).Any() ? "survived" : "not observed";
    private static string DescribeEnvironment(CorpusVersion version) => $"editor={version.Editor ?? "unknown"}; version={version.EditorVersion ?? "unknown"}; transfer={version.Transfer ?? "unknown"}; operation={version.Operation ?? "unknown"}";
    private static int Count(IEnumerable<(CorpusFamily Family, CorpusVersion Version, string Path)> annotation, string format) => annotation.Count(item => item.Family.Format.Equals(format, StringComparison.OrdinalIgnoreCase));
    private static bool IsOffice(string path) => new[] { ".docx", ".xlsx", ".pptx" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    private static string Short(string path) => Path.GetFileName(path);

    private static string Markdown(RealCorpusReview review) => $"# GitIt Real Corpus Gate Review\n\n## Corpus overview\n\n- Gate status: {review.GateStatus}\n- DOCX: {review.DocxVersions} versions\n- XLSX: {review.XlsxVersions} versions\n- PPTX: {review.PptxVersions} versions\n- Families: {review.Families}; known parent edges: {review.KnownParentEdges}\n\n## Environment\n\n{Lines(review.Environments, "- No manual environment was recorded.")}\n\n## Lineage results\n\n- Correct: {review.CorrectEdges}\n- Wrong: {review.WrongEdges}\n- Abstained: {review.AbstainedEdges}\n- High-confidence wrong: {review.HighConfidenceWrongEdges}\n\n{Lines(review.Lineage.Select(result => $"- **{result.Outcome}** | ground truth `{result.GroundTruth}` | GitIt `{result.Predicted ?? "Uncertain"}`{(result.Confidence is null ? string.Empty : $" ({result.Confidence:P0})")} | {result.Detail}"), "- No real lineage edge was evaluated.")}\n\n## Diff validation\n\n{Lines(review.DiffChecks.Select(check => $"- **{check.Outcome}** | {check.Edge} | expected: {check.Expected}{(check.MatchedChanges.Count == 0 ? string.Empty : $" | detected: {string.Join("; ", check.MatchedChanges)}")}"), "- No expected changes were available for review.")}\n\n## Participant evidence\n\n{Lines(review.Participants.Select(item => $"- {item.Version}: Creator={item.Creator ?? "none"}; LastModifiedBy={item.LastModifiedBy ?? "none"}; RSIDs={item.RsidCount}; revision authors={item.RevisionAuthorCount}; comment authors={item.CommentAuthorCount}."), "- No real participant evidence was observed.")}\n\n### Transition survival\n\n{Lines(review.ParticipantTransitions.Select(item => $"- {item.Edge}: Creator {item.Creator}; LastModifiedBy {item.LastModifiedBy}; RSID {item.Rsid}; revision author {item.RevisionAuthor}; comment author {item.CommentAuthor}."), "- No real transitions were compared.")}\n\n## New real-world issues\n\n{Lines(review.NewRealWorldIssues.Select(issue => $"- {issue}"), "- None observed. This does not mean real-world validation passed.")}\n\n## Current three most dangerous scenarios\n\n1. Provenance stripped after content reconstruction or privacy cleanup.\n2. Word/WPS cross-editor save rewriting OOXML traces.\n3. Independently created files derived from a highly similar template.\n\n## Core decision\n\nCritical before Alpha: {(review.Recommendation == "FIX CORE AND REPEAT REAL CORPUS GATE" ? "Investigate high-confidence wrong lineage or missed annotated change." : "None established by this gate run.")}\n\nNice-to-have: richer expected-change annotations after the first manual run.\n\nKnown limitation: abstention is an allowed result when provenance is insufficient.\n\n## Recommendation\n\nRecommendation:\n\n{review.Recommendation}\n\nManual action required:\n\n{review.ManualActionRequired}\n";
    private static string Lines(IEnumerable<string> lines, string empty) => lines.Any() ? string.Join("\n", lines) : empty;
}
