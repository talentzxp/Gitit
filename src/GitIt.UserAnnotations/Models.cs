using GitIt.Core;

namespace GitIt.UserAnnotations;

/// <summary>
/// User-owned context which deliberately sits above Core inference. It records
/// knowledge and presentation choices but never rewrites an inferred edge.
/// </summary>
public sealed class UserAnnotationProject
{
    public const string CurrentSchemaVersion = "gitit-project-v1";
    public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string AnalysisRoot { get; set; } = string.Empty;
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<UserDocumentGroup> DocumentGroups { get; set; } = [];
    public List<UserConfirmedRelation> ConfirmedRelations { get; set; } = [];
    public List<UserCandidateReview> CandidateReviews { get; set; } = [];
    public List<UserHiddenItem> HiddenItems { get; set; } = [];
    public Dictionary<string, string> FamilyNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Notes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public AnalysisCache? AnalysisCache { get; set; }
}

public sealed class UserDocumentGroup
{
    public string GroupId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<string> Files { get; set; } = [];
    public string CreatedBy { get; set; } = "user";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UserConfirmedRelation
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Type { get; set; } = "user-confirmed-parent";
    public DateTimeOffset ConfirmedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UserCandidateReview
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string State { get; set; } = "kept-unconfirmed";
    public DateTimeOffset ReviewedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UserHiddenItem
{
    public string Path { get; set; } = string.Empty;
    public string Reason { get; set; } = "removed-from-analysis-view";
    public DateTimeOffset HiddenAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Serialized analysis data only. Original Office packages are never embedded in a .gitit project.</summary>
public sealed class AnalysisCache
{
    public GitItAnalysisResult? Analysis { get; set; }
    public List<OfficeDocumentProfile> Profiles { get; set; } = [];
    public LineageResult? Lineage { get; set; }
}
