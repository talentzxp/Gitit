using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitIt.Core;
using GitIt.GroundTruth;

var json = args.Contains("--json", StringComparer.OrdinalIgnoreCase);
var cleanArgs = args.Where(argument => !argument.Equals("--json", StringComparison.OrdinalIgnoreCase)).ToArray();
if (cleanArgs.Length > 0 && cleanArgs[0].Equals("real", StringComparison.OrdinalIgnoreCase))
{
    var corpusRoot = cleanArgs.Length > 1 ? cleanArgs[1] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "real-world-corpus"));
    var review = RealCorpusGate.Run(corpusRoot);
    RealCorpusGate.WriteReport(review);
    if (json) Console.WriteLine(JsonSerializer.Serialize(review, Json.Options)); else RealCorpusGate.Print(review);
    return review.Recommendation == "FIX CORE AND REPEAT REAL CORPUS GATE" ? 2 : 0;
}
var dataset = new GroundTruthGenerator().Create();
var report = Benchmark.Run(dataset, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "real-world-corpus")));
Benchmark.WriteReports(report);
if (json) Console.WriteLine(JsonSerializer.Serialize(report, Json.Options)); else Benchmark.Print(report);
return report.FalseConfidentPredictionRate > 0 ? 2 : 0;

internal sealed record Metric(string Name, double Value, string Definition);
internal sealed record FormatMetric(string Format, int Versions, string Metric, double? Value, string Note);
internal sealed record CalibrationBucket(string Range, int Predictions, int Correct, int Incorrect, double? EmpiricalAccuracy);
internal sealed record FailureCase(string Category, string Detail);
internal sealed record ScalingMetric(int Files, double ColdParseMs, double WarmParseMs, double CandidateRetrievalMs, double DeepScoringMs, double GraphMs, double JsonMs, double TotalMs, long NaivePairs, long RetrievedCandidates, double ReductionRatio, double AverageCandidates, int P95Candidates, long ManagedBytes);
internal sealed record IncrementalMetric(int ExistingFiles, double AddLatencyMs, int RetrievedCandidates);
internal sealed record ExternalCorpusSummary(bool InfrastructureReady, int TotalFiles, int WordFiles, int WpsFiles, int WeChatFiles, string Status, IReadOnlyList<string> ValidationErrors, IReadOnlyList<string> ValidationWarnings);
internal sealed record BenchmarkReport(
    string DatasetRoot, string TestSummary, IReadOnlyList<Metric> Metrics, double DocumentFamilyDetectionAccuracy,
    double ParentEdgePrecision, double ParentEdgeRecall, double ExactParentAccuracy, double BranchDetectionAccuracy,
    double DuplicateDetectionAccuracy, double AbstentionRate, double FalseConfidentPredictionRate,
    IReadOnlyList<string> FalseConfidentPredictions, IReadOnlyList<FormatMetric> ByFormat,
    double TemplateSiblingFalseFamilyRate, double TemplateSiblingFalseEdgeRate, IReadOnlyList<CalibrationBucket> Calibration,
    IReadOnlyList<ScalingMetric> Scaling, IncrementalMetric Incremental, CandidateRetrievalStats GroundTruthRetrieval,
    bool CandidateRetrievalAccuracyRegression, ExternalCorpusSummary ExternalCorpus, IReadOnlyList<FailureCase> FailureAnalysis);

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
}

internal static class Benchmark
{
    public static BenchmarkReport Run(GroundTruthDataset dataset, string externalRoot)
    {
        var scan = new OfficeScanner().Scan(dataset.Root);
        var optimized = new LineageInferer().Infer(scan.Documents);
        var naive = new LineageInferer().InferNaive(scan.Documents);
        var hashes = scan.Documents.ToDictionary(document => document.Path, document => document.FileHash, StringComparer.OrdinalIgnoreCase);
        var expected = dataset.Versions.Where(version => version.ParentId is not null).Select(version => (hashes[dataset.Versions.Single(parent => parent.Id == version.ParentId).Path], hashes[version.Path])).ToHashSet();
        var predicted = optimized.Edges.Select(edge => (hashes[edge.From], hashes[edge.To])).ToHashSet();
        var naivePredicted = naive.Edges.Select(edge => (hashes[edge.From], hashes[edge.To])).ToHashSet();
        var correct = predicted.Intersect(expected).Count();
        var precision = Divide(correct, predicted.Count); var recall = Divide(correct, expected.Count);
        var actualBranchParents = dataset.Versions.Where(v => v.ParentId is not null).GroupBy(v => v.ParentId!).Where(group => group.Count() > 1).Select(group => dataset.Versions.Single(v => v.Id == group.Key).Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var predictedBranchParents = optimized.Edges.GroupBy(edge => edge.From, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedAbstentions = dataset.Versions.Where(version => version.ExpectsAbstention).Select(version => version.Path).ToArray();
        var falseConfident = optimized.Edges.Where(edge => edge.Status == LineageStatus.Probable && (!expected.Contains((hashes[edge.From], hashes[edge.To])) || expectedAbstentions.Contains(edge.To, StringComparer.OrdinalIgnoreCase))).Select(edge => $"{Path.GetFileName(edge.From)} -> {Path.GetFileName(edge.To)} ({edge.Confidence:P0})").ToArray();
        var retrieval = RetrievalStats(scan.Documents, dataset, hashes);
        var template = TemplateSibling();
        var failures = new List<FailureCase>
        {
            new("copy-paste-reconstruction", "Reconstructed content has no reliable provenance; the engine must abstain or mark it RelatedButUnproven."),
            new("metadata-and-revision-cleanup", "Removing document properties, revisions, and comments can erase high-value provenance evidence."),
            new("template-sibling", "Highly similar template descendants can look related; the dedicated sibling benchmark checks for false families and edges.")
        };
        failures.AddRange(falseConfident.Select(item => new FailureCase("high-confidence-error", item)));
        var metrics = new[]
        {
            new Metric("documentFamilyDetectionAccuracy", FamilyAccuracy(dataset, optimized), "Pairwise agreement on synthetic ground truth."),
            new Metric("parentEdgePrecision", precision, "Correct asserted parents / asserted parents."),
            new Metric("parentEdgeRecall", recall, "Correct asserted parents / expected parents."),
            new Metric("exactParentAccuracy", recall, "Children whose direct parent was selected exactly."),
            new Metric("branchDetectionAccuracy", Divide(actualBranchParents.Intersect(predictedBranchParents, StringComparer.OrdinalIgnoreCase).Count(), actualBranchParents.Count), "Expected branch parents detected."),
            new Metric("duplicateDetectionAccuracy", DuplicateAccuracy(dataset, optimized), "SHA-256 duplicate groups detected."),
            new Metric("abstentionRate", Divide(expectedAbstentions.Count(path => !optimized.Edges.Any(edge => edge.To.Equals(path, StringComparison.OrdinalIgnoreCase))), expectedAbstentions.Length), "Expected abstentions with no asserted edge."),
            new Metric("falseConfidentPredictionRate", Divide(falseConfident.Length, Math.Max(1, optimized.Edges.Count)), "Wrong Probable edges / all asserted edges."),
        };
        return new BenchmarkReport(dataset.Root, Environment.GetEnvironmentVariable("GITIT_TESTS_PASSED") ?? "not supplied", metrics, metrics[0].Value, precision, recall, recall, metrics[4].Value, metrics[5].Value, metrics[6].Value, metrics[7].Value, falseConfident, FormatBreakdown(dataset, optimized, hashes), template.FalseFamilyRate, template.FalseEdgeRate, Calibration(optimized, expected, hashes), Scaling(), Incremental(), retrieval, !predicted.SetEquals(naivePredicted), External(externalRoot), failures);
    }

    private static CandidateRetrievalStats RetrievalStats(IReadOnlyList<OfficeDocumentProfile> documents, GroundTruthDataset data, IReadOnlyDictionary<string, string> hashes)
    {
        var retrieved = new CandidateRetriever().Retrieve(documents);
        var expected = data.Versions.Where(version => version.ParentId is not null).Select(version => (From: data.Versions.Single(parent => parent.Id == version.ParentId).Path, To: version.Path)).ToHashSet();
        var retained = expected.Count(pair => retrieved.Any(candidate => candidate.From.Path.Equals(pair.From, StringComparison.OrdinalIgnoreCase) && candidate.To.Path.Equals(pair.To, StringComparison.OrdinalIgnoreCase)));
        var counts = retrieved.GroupBy(candidate => candidate.To.Path, StringComparer.OrdinalIgnoreCase).Select(group => group.Count()).OrderBy(value => value).ToArray();
        var naive = (long)documents.Count * Math.Max(0, documents.Count - 1);
        return new CandidateRetrievalStats(naive, retrieved.Count, naive == 0 ? 0 : Math.Round(1 - retrieved.Count / (double)naive, 4), Divide(retained, expected.Count), documents.Count == 0 ? 0 : Math.Round(retrieved.Count / (double)documents.Count, 2), counts.Length == 0 ? 0 : counts[(int)Math.Ceiling(counts.Length * .95) - 1]);
    }

    private static IReadOnlyList<ScalingMetric> Scaling()
    {
        var output = new List<ScalingMetric>(); var generator = new GroundTruthGenerator();
        foreach (var count in new[] { 10, 50, 100, 500, 1000, 2000 })
        {
            var root = Path.Combine(Path.GetTempPath(), "gitit-v004-scaling", $"{count}-{Guid.NewGuid():N}"); generator.CreatePerformanceFiles(root, count);
            var timer = Stopwatch.StartNew(); var scanner = new OfficeScanner(); var scan = scanner.Scan(root); var cold = timer.Elapsed.TotalMilliseconds;
            timer.Restart(); scanner.Scan(root); var warm = timer.Elapsed.TotalMilliseconds;
            timer.Restart(); var retrieved = new CandidateRetriever().Retrieve(scan.Documents); var retrieveMs = timer.Elapsed.TotalMilliseconds;
            timer.Restart(); var deep = new LineageInferer().Infer(scan.Documents); var allMs = timer.Elapsed.TotalMilliseconds;
            timer.Restart(); var graph = deep.Edges.Count; var graphMs = timer.Elapsed.TotalMilliseconds;
            timer.Restart(); JsonSerializer.Serialize(deep, Json.Options); var jsonMs = timer.Elapsed.TotalMilliseconds;
            var stats = deep.CandidateRetrieval!;
            output.Add(new ScalingMetric(count, Math.Round(cold, 1), Math.Round(warm, 1), Math.Round(retrieveMs, 1), Math.Round(Math.Max(0, allMs - retrieveMs), 1), Math.Round(graphMs, 1), Math.Round(jsonMs, 1), Math.Round(cold + retrieveMs + allMs + jsonMs, 1), stats.NaivePairCount, stats.RetrievedCandidateCount, stats.CandidateReductionRatio, stats.AverageCandidatesPerVersion, stats.P95CandidatesPerVersion, GC.GetTotalMemory(false)));
        }
        return output;
    }

    private static IncrementalMetric Incremental()
    {
        var root = Path.Combine(Path.GetTempPath(), "gitit-v004-incremental", Guid.NewGuid().ToString("N")); var generator = new GroundTruthGenerator(); generator.CreatePerformanceFiles(root, 501);
        var documents = new OfficeScanner().Scan(root).Documents.OrderBy(profile => profile.Path, StringComparer.OrdinalIgnoreCase).ToArray(); var timer = Stopwatch.StartNew(); var result = new LineageInferer().InferForNewVersion(documents.Take(500).ToArray(), documents[500]);
        return new IncrementalMetric(500, Math.Round(timer.Elapsed.TotalMilliseconds, 1), (int)(result.CandidateRetrieval?.RetrievedCandidateCount ?? 0));
    }

    private static ExternalCorpusSummary External(string root)
    {
        var validation = CorpusManifestLoader.Validate(root); var files = Directory.Exists(root) ? Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories).Where(path => new[] { ".docx", ".xlsx", ".pptx" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)).ToArray() : Array.Empty<string>();
        var word = files.Count(path => path.Contains("docx" + Path.DirectorySeparatorChar + "word-only", StringComparison.OrdinalIgnoreCase));
        var wps = files.Count(path => path.Contains("word-wps", StringComparison.OrdinalIgnoreCase) || path.Contains("office-wps", StringComparison.OrdinalIgnoreCase));
        var wechat = files.Count(path => path.Contains("wechat-flow", StringComparison.OrdinalIgnoreCase));
        return new ExternalCorpusSummary(validation.IsValid && Directory.Exists(root), files.Length, word, wps, wechat, files.Length == 0 ? "Infrastructure ready, manual corpus still required." : "Populated; results must be interpreted by recorded environment and sample size.", validation.Errors, validation.Warnings);
    }

    private static (double FalseFamilyRate, double FalseEdgeRate) TemplateSibling()
    {
        var data = new GroundTruthGenerator().CreateTemplateSiblingDataset(); var analysis = new ProjectAnalyzer().Analyze(data.Root); var family = data.Versions.ToDictionary(version => version.Path, version => version.Family, StringComparer.OrdinalIgnoreCase);
        var falseEdges = analysis.Edges.Count(edge => family.TryGetValue(edge.From, out var from) && family.TryGetValue(edge.To, out var to) && from != to);
        var falseFamilies = analysis.DocumentFamilies.Count(group => group.VersionIds.Where(family.ContainsKey).Select(path => family[path]).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
        return (Divide(falseFamilies, Math.Max(1, analysis.DocumentFamilies.Count)), Divide(falseEdges, Math.Max(1, analysis.Edges.Count)));
    }

    private static IReadOnlyList<FormatMetric> FormatBreakdown(GroundTruthDataset data, LineageResult result, IReadOnlyDictionary<string, string> hashes)
    {
        var metrics = new List<FormatMetric>();
        foreach (var kind in new[] { OfficeFileKind.Docx, OfficeFileKind.Xlsx, OfficeFileKind.Pptx })
        {
            var versions = data.Versions.Where(version => Path.GetExtension(version.Path).Equals($".{kind.ToString().ToLowerInvariant()}", StringComparison.OrdinalIgnoreCase)).ToArray();
            var expected = versions.Where(version => version.ParentId is not null).Select(version => (hashes[data.Versions.Single(parent => parent.Id == version.ParentId).Path], hashes[version.Path])).ToHashSet();
            var predicted = result.Edges.Where(edge => Path.GetExtension(edge.To).Equals($".{kind.ToString().ToLowerInvariant()}", StringComparison.OrdinalIgnoreCase)).Select(edge => (hashes[edge.From], hashes[edge.To])).ToHashSet();
            var note = expected.Count < 3 ? "Insufficient sample size; synthetic only." : "Synthetic only.";
            metrics.Add(new FormatMetric(kind.ToString(), versions.Length, "parentPrecision", expected.Count < 3 ? null : Divide(predicted.Intersect(expected).Count(), predicted.Count), note));
            metrics.Add(new FormatMetric(kind.ToString(), versions.Length, "parentRecall", expected.Count < 3 ? null : Divide(predicted.Intersect(expected).Count(), expected.Count), note));
        }
        return metrics;
    }

    private static IReadOnlyList<CalibrationBucket> Calibration(LineageResult result, IReadOnlySet<(string, string)> expected, IReadOnlyDictionary<string, string> hashes) => new[] { (0.50, .59), (.60, .69), (.70, .79), (.80, .89), (.90, 1.0) }.Select(range => { var edges = result.Edges.Where(edge => edge.Confidence >= range.Item1 && edge.Confidence <= range.Item2).ToArray(); var correct = edges.Count(edge => expected.Contains((hashes[edge.From], hashes[edge.To]))); return new CalibrationBucket($"{range.Item1:F2}-{range.Item2:F2}", edges.Length, correct, edges.Length - correct, edges.Length == 0 ? null : Divide(correct, edges.Length)); }).ToArray();
    private static double FamilyAccuracy(GroundTruthDataset data, LineageResult result)
    {
        var parent = data.Versions.ToDictionary(version => version.Path, version => version.Path, StringComparer.OrdinalIgnoreCase);
        string Find(string value) => parent[value] == value ? value : parent[value] = Find(parent[value]);
        void Union(string left, string right) { var a = Find(left); var b = Find(right); if (a != b) parent[b] = a; }
        foreach (var edge in result.Edges) Union(edge.From, edge.To);
        foreach (var group in result.Duplicates) foreach (var duplicate in group.Paths.Skip(1)) Union(group.CanonicalPath, duplicate);
        var correct = 0; var total = 0;
        for (var left = 0; left < data.Versions.Count; left++)
        for (var right = left + 1; right < data.Versions.Count; right++)
        {
            total++;
            if ((data.Versions[left].Family == data.Versions[right].Family) == (Find(data.Versions[left].Path) == Find(data.Versions[right].Path))) correct++;
        }
        return Divide(correct, total);
    }
    private static double DuplicateAccuracy(GroundTruthDataset data, LineageResult result) => Divide(data.Versions.Where(version => version.IsDuplicate).Count(version => result.Duplicates.Any(group => group.Paths.Contains(version.Path, StringComparer.OrdinalIgnoreCase))), data.Versions.Count(version => version.IsDuplicate));
    private static double Divide(int numerator, int denominator) => denominator == 0 ? 1 : Math.Round((double)numerator / denominator, 4);

    public static void WriteReports(BenchmarkReport report)
    {
        var output = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "outputs")); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "benchmark-report.json"), JsonSerializer.Serialize(report, Json.Options));
        File.WriteAllText(Path.Combine(output, "benchmark-report.md"), Markdown(report));
        File.WriteAllText(Path.Combine(output, "REVIEW_SUMMARY_v0.0.4.md"), Review(report));
    }

    public static void Print(BenchmarkReport report)
    {
        Console.WriteLine("=== GitIt v0.0.4 Review Summary ===\n");
        Console.WriteLine($"Tests: {report.TestSummary} passed");
        Console.WriteLine($"Synthetic lineage: family {report.DocumentFamilyDetectionAccuracy:P1}; parent precision {report.ParentEdgePrecision:P1}; recall {report.ParentEdgeRecall:P1}; branch {report.BranchDetectionAccuracy:P1}; duplicate {report.DuplicateDetectionAccuracy:P1}; abstention {report.AbstentionRate:P1}.");
        Console.WriteLine($"Template sibling: false family {report.TemplateSiblingFalseFamilyRate:P1}; false edge {report.TemplateSiblingFalseEdgeRate:P1}.");
        Console.WriteLine($"Candidate retrieval: true parent retained {report.GroundTruthRetrieval.TrueParentCandidateRecall:P1}; reduction {report.GroundTruthRetrieval.CandidateReductionRatio:P1}; {report.GroundTruthRetrieval.RetrievedCandidateCount}/{report.GroundTruthRetrieval.NaivePairCount} pairs.");
        foreach (var item in report.Scaling.Where(item => item.Files is 100 or 500 or 1000 or 2000)) Console.WriteLine($"Performance {item.Files}: cold {item.ColdParseMs:F0} ms, lineage {item.CandidateRetrievalMs + item.DeepScoringMs:F0} ms, candidates {item.RetrievedCandidates}/{item.NaivePairs}.");
        Console.WriteLine($"Incremental: existing 500 + one file {report.Incremental.AddLatencyMs:F0} ms, {report.Incremental.RetrievedCandidates} candidates.");
        Console.WriteLine($"Real corpus: Word {report.ExternalCorpus.WordFiles}, WPS {report.ExternalCorpus.WpsFiles}, WeChat {report.ExternalCorpus.WeChatFiles}; {report.ExternalCorpus.Status}");
        Console.WriteLine("Known risks: provenance stripped after reconstruction; WPS may rewrite OOXML evidence; near-identical template descendants.");
        Console.WriteLine("Recommendation: DO NOT ENTER PC ALPHA YET — real Word/WPS/WeChat corpus is not populated. Core work should pause for manual corpus review.");
    }

    private static string Markdown(BenchmarkReport r) => $"# GitIt v0.0.4 benchmark report\n\n## Synthetic Ground Truth\n\n- Family accuracy: {r.DocumentFamilyDetectionAccuracy:P2}\n- Parent precision / recall: {r.ParentEdgePrecision:P2} / {r.ParentEdgeRecall:P2}\n- Branch / duplicate / abstention: {r.BranchDetectionAccuracy:P2} / {r.DuplicateDetectionAccuracy:P2} / {r.AbstentionRate:P2}\n\n## Template Siblings\n\n- False family: {r.TemplateSiblingFalseFamilyRate:P2}\n- False edge: {r.TemplateSiblingFalseEdgeRate:P2}\n\n## External Real Corpus\n\n- {r.ExternalCorpus.Status}\n- Real Word: {r.ExternalCorpus.WordFiles}; WPS: {r.ExternalCorpus.WpsFiles}; WeChat: {r.ExternalCorpus.WeChatFiles}\n\n## Scaling Benchmark\n\n| Files | Cold parse ms | Candidate pairs | Naive pairs | Reduction | Total ms |\n|---:|---:|---:|---:|---:|---:|\n{string.Join("\n", r.Scaling.Select(s => $"| {s.Files} | {s.ColdParseMs:F1} | {s.RetrievedCandidates} | {s.NaivePairs} | {s.ReductionRatio:P2} | {s.TotalMs:F1} |"))}\n";
    private static string Review(BenchmarkReport r) => $"# GitIt v0.0.4 review summary\n\n## One-line conclusion\n\nGitIt v0.0.4 completed explainable two-stage candidate retrieval, but real Word/WPS/WeChat corpus remains unpopulated, so real-world validation is pending.\n\n## What changed\n\n- Added a deterministic Stage 1 candidate index using RSIDs, rare content tokens, filename stems, metadata hints, and coarse structure.\n- Limited deep lineage scoring to configurable Top-K candidates and attached selection evidence to every scored candidate.\n- Added a corpus manifest validator that checks file existence, format, IDs, parents, cycles, and identical-file warnings.\n- Added a bounded incremental API for one newly added version.\n- Added scaling and retrieval metrics separate from synthetic, template-sibling, and external-corpus results.\n\n## Tests\n\n`dotnet test`: {r.TestSummary} passed\n\n## Synthetic metrics\n\n- Family precision/recall proxy: {r.DocumentFamilyDetectionAccuracy:P2}\n- Parent precision: {r.ParentEdgePrecision:P2}; parent recall/exact parent: {r.ParentEdgeRecall:P2}\n- Branch: {r.BranchDetectionAccuracy:P2}; duplicate: {r.DuplicateDetectionAccuracy:P2}; abstention: {r.AbstentionRate:P2}\n- False confident: {r.FalseConfidentPredictionRate:P2}\n- Template sibling false family/edge: {r.TemplateSiblingFalseFamilyRate:P2} / {r.TemplateSiblingFalseEdgeRate:P2}\n\n## Format samples\n\n{string.Join("\n", r.ByFormat.GroupBy(m => m.Format).Select(g => $"- {g.Key}: {g.First().Versions} versions; {g.First().Note}"))}\n\n## Candidate retrieval and performance\n\n{string.Join("\n", r.Scaling.Where(s => s.Files is 100 or 500 or 1000 or 2000).Select(s => $"- {s.Files} files: naive {s.NaivePairs}, retrieved {s.RetrievedCandidates}, reduction {s.ReductionRatio:P2}, total lineage {s.CandidateRetrievalMs + s.DeepScoringMs:F1} ms; cold {s.ColdParseMs:F1} ms, warm {s.WarmParseMs:F1} ms."))}\n\nIncremental: existing 500 files + one file = {r.Incremental.AddLatencyMs:F1} ms; {r.Incremental.RetrievedCandidates} candidates.\n\n## External real corpus\n\nExternal corpus infrastructure: {(r.ExternalCorpus.InfrastructureReady ? "READY" : "NOT READY")}\n\nReal Word corpus: {(r.ExternalCorpus.WordFiles == 0 ? "NOT POPULATED" : r.ExternalCorpus.WordFiles)}\n\nReal WPS corpus: {(r.ExternalCorpus.WpsFiles == 0 ? "NOT POPULATED" : r.ExternalCorpus.WpsFiles)}\n\nReal WeChat transfer corpus: {(r.ExternalCorpus.WeChatFiles == 0 ? "NOT POPULATED" : r.ExternalCorpus.WeChatFiles)}\n\n## Three successful cases\n\n1. Normal synthetic DOCX chain: shared RSID/revision/content evidence retains direct parents and GitIt selects the recorded parent.\n2. Branch case: two versions based on one known parent are retained as separate child edges.\n3. Exact duplicate: byte-identical copies are grouped by SHA-256 rather than asserted as a new lineage edge.\n\n## Three uncertain or failure cases\n\n{string.Join("\n", r.FailureAnalysis.Take(3).Select((f, i) => $"{i + 1}. {f.Detail}"))}\n\n## Most dangerous false-positive scenarios\n\n1. Large content reconstruction after provenance is stripped.\n2. WPS or privacy cleanup rewriting OOXML provenance.\n3. Highly similar documents independently produced from a shared template.\n\n## Did optimization reduce accuracy?\n\n{(r.CandidateRetrievalAccuracyRegression ? "YES — inspect the synthetic regression before proceeding." : "NO — optimized and naive synthetic edge sets matched in this run.")}\n\n## Recommendation\n\nRecommendation:\n\nDO NOT ENTER PC ALPHA YET\n\nReason: the only current release blocker is human-made external Word/WPS/WeChat corpus validation. Do not add more Core features until that review is complete.\n";
}
