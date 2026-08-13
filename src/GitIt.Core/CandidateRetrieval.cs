using System.Text.RegularExpressions;

namespace GitIt.Core;

public sealed record RetrievedCandidate(OfficeDocumentProfile From, OfficeDocumentProfile To, IReadOnlyList<CandidateSelectionEvidence> Evidence);

/// <summary>Stage 1: deterministic low-cost retrieval. It never decides lineage; it only selects candidates for deep scoring.</summary>
public sealed class CandidateRetriever
{
    private readonly LineageWeights weights;
    public CandidateRetriever(LineageWeights? weights = null) => this.weights = weights ?? new LineageWeights();

    public IReadOnlyList<RetrievedCandidate> Retrieve(IReadOnlyList<OfficeDocumentProfile> documents)
    {
        var byRsid = Index(documents, profile => profile.Docx?.Rsids ?? Array.Empty<string>());
        var byStem = Index(documents, profile => new[] { Stem(profile.Path) }.Where(value => value.Length >= 3));
        var byStructure = Index(documents, StructureKeys);
        var byCreator = Index(documents, profile => new[] { profile.Metadata.Creator }.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim().ToUpperInvariant()));
        var tokenFrequency = TokenFrequency(documents);
        var byRareToken = Index(documents, profile => DistinctiveTokens(profile, tokenFrequency));
        var output = new List<RetrievedCandidate>();
        foreach (var target in documents)
        {
            var candidates = new Dictionary<string, (OfficeDocumentProfile Profile, List<CandidateSelectionEvidence> Evidence)>(StringComparer.OrdinalIgnoreCase);
            AddMatches(byRsid, target.Docx?.Rsids ?? Array.Empty<string>(), target, candidates, "sharedRsid", 0.85, "Shares RSID edit-session trace(s).", weights.CandidateTopK * 8);
            AddMatches(byRareToken, DistinctiveTokens(target, tokenFrequency), target, candidates, "distinctiveContent", 0.70, "Shares low-frequency distinctive content token(s).");
            AddMatches(byStem, new[] { Stem(target.Path) }.Where(value => value.Length >= 3), target, candidates, "filenameStem", 0.20, "Normalized filename stem matches.", weights.CandidateTopK * 4);
            AddMatches(byStructure, StructureKeys(target), target, candidates, "coarseStructureBucket", 0.25, "Matches a format-specific coarse structure bucket.", weights.CandidateTopK * 2);
            AddMatches(byCreator, new[] { target.Metadata.Creator }.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim().ToUpperInvariant()), target, candidates, "metadataHint", 0.10, "Creator metadata matches; a weak retrieval hint.", weights.CandidateTopK * 2);
            foreach (var candidate in candidates.Values)
            {
                var structure = CoarseStructure(candidate.Profile, target);
                if (structure > 0.75) candidate.Evidence.Add(new CandidateSelectionEvidence("coarseStructure", 0.10, $"Coarse document size/structure ratio is {structure:P0}."));
                var sourceRsids = candidate.Profile.Docx?.Rsids ?? Array.Empty<string>();
                var targetRsids = target.Docx?.Rsids ?? Array.Empty<string>();
                var additionalSourceTraces = sourceRsids.Except(targetRsids, StringComparer.OrdinalIgnoreCase).Count();
                var missingSourceTraces = targetRsids.Except(sourceRsids, StringComparer.OrdinalIgnoreCase).Count();
                if (sourceRsids.Count > 0 && targetRsids.Count > sourceRsids.Count && additionalSourceTraces == 0 && missingSourceTraces > 0)
                    candidate.Evidence.Add(new CandidateSelectionEvidence("rsidProgression", Math.Min(5, 1d / missingSourceTraces), "Source RSID set is a proper subset of the target, consistent with an earlier edit session."));
            }
            output.AddRange(candidates.Values.Select(value => new { value.Profile, value.Evidence, Score = value.Evidence.Sum(item => item.Score) })
                .Where(value => value.Score >= weights.CandidateMinimumScore)
                .OrderByDescending(value => value.Score).ThenBy(value => value.Profile.Path, StringComparer.OrdinalIgnoreCase)
                .Take(weights.CandidateTopK)
                .Select(value => new RetrievedCandidate(value.Profile, target, value.Evidence.OrderByDescending(item => item.Score).ToArray())));
        }
        return output;
    }

    private static void AddMatches(IReadOnlyDictionary<string, IReadOnlyList<OfficeDocumentProfile>> index, IEnumerable<string> keys, OfficeDocumentProfile target, Dictionary<string, (OfficeDocumentProfile Profile, List<CandidateSelectionEvidence> Evidence)> output, string type, double score, string detail, int maximumMatches = int.MaxValue)
    {
        foreach (var key in keys.Distinct(StringComparer.OrdinalIgnoreCase))
        foreach (var profile in index.GetValueOrDefault(key, Array.Empty<OfficeDocumentProfile>()).OrderBy(profile => profile.Path, StringComparer.OrdinalIgnoreCase).Take(maximumMatches))
        {
            if (profile.Path.Equals(target.Path, StringComparison.OrdinalIgnoreCase) || profile.Kind != target.Kind) continue;
            if (!output.TryGetValue(profile.Path, out var current)) current = (profile, new List<CandidateSelectionEvidence>());
            var existing = current.Evidence.FindIndex(item => item.Type == type);
            if (existing < 0) current.Evidence.Add(new CandidateSelectionEvidence(type, score, detail));
            else
            {
                var evidence = current.Evidence[existing];
                current.Evidence[existing] = evidence with { Score = evidence.Score + score, Detail = $"{detail} Multiple matching traces increase retrieval priority." };
            }
            output[profile.Path] = current;
        }
    }

    private static Dictionary<string, IReadOnlyList<OfficeDocumentProfile>> Index(IEnumerable<OfficeDocumentProfile> documents, Func<OfficeDocumentProfile, IEnumerable<string>> keys) => documents.SelectMany(profile => keys(profile).Where(key => !string.IsNullOrWhiteSpace(key)).Select(key => (Key: key, Profile: profile))).GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => (IReadOnlyList<OfficeDocumentProfile>)group.Select(item => item.Profile).ToArray(), StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> TokenFrequency(IEnumerable<OfficeDocumentProfile> documents) => documents.SelectMany(profile => Tokens(Content(profile)).Distinct(StringComparer.OrdinalIgnoreCase)).GroupBy(token => token, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
    private IEnumerable<string> DistinctiveTokens(OfficeDocumentProfile profile, IReadOnlyDictionary<string, int> frequency) => Tokens(Content(profile)).Where(token => frequency.GetValueOrDefault(token) <= weights.CandidateRareTokenMaximumDocumentFrequency).Distinct(StringComparer.OrdinalIgnoreCase);
    private static string Content(OfficeDocumentProfile profile) => profile.Kind switch { OfficeFileKind.Docx => string.Join(" ", profile.Docx?.Paragraphs.Select(p => p.Text) ?? Array.Empty<string>()), OfficeFileKind.Xlsx => string.Join(" ", profile.Xlsx?.Sheets.SelectMany(sheet => sheet.Cells).Select(cell => cell.Value) ?? Array.Empty<string?>()), OfficeFileKind.Pptx => string.Join(" ", profile.Pptx?.Slides.SelectMany(slide => slide.Shapes).Select(shape => shape.Text) ?? Array.Empty<string>()), _ => string.Empty };
    private static IEnumerable<string> StructureKeys(OfficeDocumentProfile profile)
    {
        var count = profile.Kind switch { OfficeFileKind.Docx => profile.Docx!.Paragraphs.Count + profile.Docx.Tables.Count, OfficeFileKind.Xlsx => profile.Xlsx!.Sheets.Sum(sheet => sheet.Cells.Count), OfficeFileKind.Pptx => profile.Pptx!.Slides.Count + profile.Pptx.Slides.Sum(slide => slide.Shapes.Count), _ => 0 };
        return new[] { $"{profile.Kind}:{count}" };
    }
    private static IEnumerable<string> Tokens(string input) => Regex.Matches(input, @"[\p{L}\p{N}]+", RegexOptions.CultureInvariant).Select(match => match.Value.ToLowerInvariant()).Where(value => value.Length > 2);
    private static string Stem(string path) => Regex.Replace(Path.GetFileNameWithoutExtension(path).ToLowerInvariant(), @"(?:[_\-\s]*(v|ver|version)?\d+|[_\-\s]*(final|draft|copy|revision|edited|修改|最终|终稿))+$", string.Empty).Trim('_', '-', ' ');
    private static double CoarseStructure(OfficeDocumentProfile left, OfficeDocumentProfile right)
    {
        static int Count(OfficeDocumentProfile profile) => profile.Kind switch { OfficeFileKind.Docx => profile.Docx!.Paragraphs.Count + profile.Docx.Tables.Count, OfficeFileKind.Xlsx => profile.Xlsx!.Sheets.Sum(sheet => sheet.Cells.Count), OfficeFileKind.Pptx => profile.Pptx!.Slides.Count + profile.Pptx.Slides.Sum(slide => slide.Shapes.Count), _ => 0 };
        var a = Count(left); var b = Count(right); return a == 0 && b == 0 ? 1 : Math.Min(a, b) / (double)Math.Max(a, b);
    }
}
