namespace GitIt.Core;

public enum OfficeFileKind { Docx, Xlsx, Pptx }
public enum EvidenceStrength { Strong, Medium, Weak, Conflicting, ParticipationOnly }
public enum LineageStatus { Probable, Possible, RelatedButUnproven, Uncertain, Duplicate }

public sealed record Evidence(
    string Type,
    EvidenceStrength Strength,
    double Score,
    string Detail,
    bool IsConflict = false);

public sealed record CommonOfficeMetadata(
    string? Title,
    string? Creator,
    string? LastModifiedBy,
    DateTimeOffset? Created,
    DateTimeOffset? Modified,
    string? Revision);

public sealed record ParticipantEvidence(
    string Value,
    string DocumentVersion,
    string Source,
    string EvidenceType,
    EvidenceStrength Strength,
    string Detail);

public sealed record IdentityCandidate(string DisplayName, double PossibleSamePersonConfidence, string Basis);
public sealed record ParticipantIdentity(string Id, string DisplayName, IReadOnlyList<ParticipantEvidence> Evidence, IReadOnlyList<IdentityCandidate>? IdentityCandidates = null);

public sealed record ParagraphFingerprint(int Index, string Text, string TextHash, string StyleId, string FormatHash);
public sealed record TableFingerprint(int Index, int Rows, int Columns, string Hash);
public sealed record RevisionEvent(string Author, DateTimeOffset? Date, string Kind);

public sealed record DocxDetails(
    IReadOnlyList<ParagraphFingerprint> Paragraphs,
    IReadOnlyList<TableFingerprint> Tables,
    IReadOnlyList<string> Rsids,
    IReadOnlyDictionary<string, int> RevisionKinds,
    IReadOnlyList<string> RevisionAuthors,
    IReadOnlyList<string> CommentAuthors,
    string BodyHash,
    string StyleHash,
    IReadOnlyList<RevisionEvent>? RevisionEvents = null);

public sealed record SpreadsheetCell(
    string Address,
    string? Value,
    string? Formula,
    string DataType,
    string StyleSignature);

public sealed record SpreadsheetSheet(
    int Index,
    string Name,
    IReadOnlyList<SpreadsheetCell> Cells,
    IReadOnlyList<string> MergedCells,
    IReadOnlyDictionary<int, string> RowProperties,
    IReadOnlyDictionary<int, string> ColumnProperties,
    string Hash);

public sealed record XlsxDetails(IReadOnlyList<SpreadsheetSheet> Sheets, IReadOnlyList<string> UnsupportedFeatures);

public sealed record SlideShape(
    uint Id,
    string Name,
    string ShapeType,
    string Text,
    long? X,
    long? Y,
    long? Width,
    long? Height,
    string FontSignature);

public sealed record PresentationSlide(int Index, string LayoutName, IReadOnlyList<SlideShape> Shapes, string Hash);
public sealed record PptxDetails(IReadOnlyList<PresentationSlide> Slides, string ThemeHash, IReadOnlyList<string> UnsupportedFeatures);

public sealed record OfficeDocumentProfile(
    string Path,
    OfficeFileKind Kind,
    long FileSize,
    DateTimeOffset FileModified,
    string FileHash,
    CommonOfficeMetadata Metadata,
    IReadOnlyDictionary<string, string> Fingerprint,
    IReadOnlyList<ParticipantEvidence> ParticipantEvidence,
    IReadOnlyList<Evidence> Evidence,
    IReadOnlyList<string> UnsupportedFeatures,
    DocxDetails? Docx = null,
    XlsxDetails? Xlsx = null,
    PptxDetails? Pptx = null);

public sealed record ScanIssue(string Path, string Message);
public sealed record ScanResult(IReadOnlyList<OfficeDocumentProfile> Documents, IReadOnlyList<ScanIssue> Issues);

public sealed record DiffChange(string Category, string Location, string Detail, string? Before = null, string? After = null);
public sealed record SemanticDiffResult(
    string SourcePath,
    string TargetPath,
    OfficeFileKind Kind,
    IReadOnlyList<DiffChange> Changes,
    IReadOnlyList<Evidence> SourceEvidence,
    IReadOnlyList<string> UnsupportedFeatures,
    string Assessment);

public sealed record LineageCandidate(
    string From,
    string To,
    double Confidence,
    LineageStatus Status,
    IReadOnlyList<Evidence> Evidence,
    IReadOnlyList<string> Warnings);

public sealed record CandidateSelectionEvidence(string Type, double Score, string Detail);
public sealed record CandidateRetrievalStats(long NaivePairCount, long RetrievedCandidateCount, double CandidateReductionRatio, double? TrueParentCandidateRecall, double AverageCandidatesPerVersion, int P95CandidatesPerVersion);

public sealed record LineageEdge(
    string From,
    string To,
    double Confidence,
    LineageStatus Status,
    IReadOnlyList<Evidence> Evidence,
    IReadOnlyList<string> Warnings);

public sealed record DuplicateGroup(string CanonicalPath, string FileHash, IReadOnlyList<string> Paths);
public sealed record LineageResult(
    IReadOnlyList<LineageEdge> Edges,
    IReadOnlyList<LineageCandidate> Candidates,
    IReadOnlyList<string> UncertainDocuments,
    IReadOnlyList<DuplicateGroup> Duplicates,
    CandidateRetrievalStats? CandidateRetrieval = null);

public sealed record ExplainResult(string File, string? FamilyId, LineageEdge? MostLikelyParent, IReadOnlyList<LineageCandidate> Alternatives, IReadOnlyList<ParticipantIdentity> Participants, IReadOnlyList<string> Warnings);

public sealed record DocumentFamily(string Id, OfficeFileKind Kind, IReadOnlyList<string> VersionIds, string DetectionBasis);
public sealed record DocumentVersion(string Id, string Path, OfficeFileKind Kind, string? DuplicateOf, string FileHash, IReadOnlyDictionary<string, string> Fingerprint);
public sealed record PerformanceMetric(string Operation, double Milliseconds, long ManagedBytes);
public sealed record ProjectInfo(string Root, DateTimeOffset AnalyzedAt, string EngineVersion);

/// <summary>Stable JSON contract for renderers. UI clients consume this object and never recompute lineage.</summary>
public sealed record GitItAnalysisResult(
    string SchemaVersion,
    ProjectInfo Project,
    IReadOnlyList<DocumentFamily> DocumentFamilies,
    IReadOnlyList<DocumentVersion> Versions,
    IReadOnlyList<LineageEdge> Edges,
    IReadOnlyList<SemanticDiffResult> Changes,
    IReadOnlyList<ParticipantIdentity> Participants,
    IReadOnlyList<Evidence> Evidence,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> UnsupportedFeatures,
    IReadOnlyList<PerformanceMetric> Performance);

public sealed record LineageWeights(
    double ContentSimilarity = 0.28,
    double StructureSimilarity = 0.14,
    double StyleSimilarity = 0.10,
    double Rsid = 0.20,
    double Revision = 0.12,
    double Metadata = 0.06,
    double Timestamp = 0.02,
    double Filename = 0.01,
    double Containment = 0.07,
    double MinimumEdgeConfidence = 0.62,
    double ProbableConfidence = 0.78,
    double HighConfidenceThreshold = 0.85,
    int CandidateTopK = 10,
    double CandidateMinimumScore = 0.20,
    int CandidateRareTokenMaximumDocumentFrequency = 12);
