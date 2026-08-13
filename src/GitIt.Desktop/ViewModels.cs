using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GitIt.Core;

namespace GitIt.Desktop;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); return true;
    }
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand(Action<object?> action) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action(parameter);
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed record FamilyItemViewModel(string Id, string Name, OfficeFileKind FileKind, int Versions, int Duplicates, int Unlinked, int Related)
{
    public string Kind => FileKind.ToString().ToUpperInvariant();
    public string Summary => $"{Versions} 个版本 · {Duplicates} 个重复 · {Related} 个相关未证实 · {Unlinked} 个未关联";
}

public sealed record GraphNodeViewModel(string Path, string Label, string Kind, string Date, string Confidence, string Status, string Badges, double X, double Y, bool IsDuplicate);

public enum GraphRelationKind { Strong, Weak, RelatedButUnproven, Conflicting }

public sealed class GraphEdgeViewModel
{
    public GraphEdgeViewModel(LineageEdge edge, double x1, double y1, double x2, double y2)
    {
        Edge = edge; X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
        Kind = edge.Evidence.Any(evidence => evidence.IsConflict) ? GraphRelationKind.Conflicting : edge.Status == LineageStatus.Probable ? GraphRelationKind.Strong : GraphRelationKind.Weak;
    }
    public GraphEdgeViewModel(LineageCandidate candidate, double x1, double y1, double x2, double y2)
    {
        RelatedCandidate = candidate; X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
        Kind = candidate.Evidence.Any(evidence => evidence.IsConflict) ? GraphRelationKind.Conflicting : GraphRelationKind.RelatedButUnproven;
    }
    public LineageEdge? Edge { get; }
    public LineageCandidate? RelatedCandidate { get; }
    public double X1 { get; }
    public double Y1 { get; }
    public double X2 { get; }
    public double Y2 { get; }
    public GraphRelationKind Kind { get; }
    public string Stroke => Kind == GraphRelationKind.Conflicting ? "#B42318" : Kind == GraphRelationKind.RelatedButUnproven ? "#7A5AF8" : "#426B9B";
    public string Dash => Kind switch { GraphRelationKind.Strong => string.Empty, GraphRelationKind.Weak => "6,4", GraphRelationKind.RelatedButUnproven => "1,4", _ => "8,3,2,3" };
    public string Marker => Kind switch { GraphRelationKind.Strong => "→", GraphRelationKind.Weak => "⇢", GraphRelationKind.RelatedButUnproven => "?", _ => "⚠" };
    public string AccessibleDescription => Kind switch { GraphRelationKind.Strong => "较强来源关系", GraphRelationKind.Weak => "较弱来源关系", GraphRelationKind.RelatedButUnproven => "相关但来源未证实", _ => "证据存在冲突" };
    public string Label => Edge is not null
        ? $"{Marker} {System.IO.Path.GetFileName(Edge.From)} → {System.IO.Path.GetFileName(Edge.To)} · {Edge.Confidence:P0} · {AccessibleDescription}"
        : $"{Marker} {System.IO.Path.GetFileName(RelatedCandidate!.From)} ··· ? ··· {System.IO.Path.GetFileName(RelatedCandidate.To)} · {AccessibleDescription}";
}

public sealed record TimelineItemViewModel(string Path, string Date, string File, string Kind, string ParticipantSummary, string Environment, string ChangeSummary, string Status, string Badges);
public sealed record DiffRowViewModel(string Category, string Location, string Before, string After, string Detail, bool IsRemoval, bool IsAddition);
public sealed record ParticipantItemViewModel(ParticipantIdentity Participant)
{
    public string Name => Participant.DisplayName;
    public string Summary => $"{Participant.Evidence.Count} 条参与线索";
}

public static class FamilyDisplayName
{
    public static string From(IReadOnlyList<OfficeDocumentProfile> profiles, OfficeFileKind kind)
    {
        var stems = profiles.Select(profile => Clean(System.IO.Path.GetFileNameWithoutExtension(profile.Path))).Where(value => value.Length >= 3).ToArray();
        var prefix = CommonPrefix(stems);
        if (prefix.Length >= 3) return prefix;
        var title = profiles.Select(profile => profile.Metadata.Title?.Trim()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && value!.Length >= 3);
        return title ?? (kind switch { OfficeFileKind.Docx => "未命名文档组", OfficeFileKind.Xlsx => "未命名表格组", _ => "未命名演示组" });
    }

    private static string Clean(string value)
    {
        var result = System.Text.RegularExpressions.Regex.Replace(value, @"\s*([_\- ]?(v|ver|version)?\d+|[（(]\d+[）)]|[_\- ]?(final|draft|copy|revision|edited|修改|最终|终稿|副本))+$", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return result.Trim(' ', '_', '-', '(', ')', '（', '）');
    }

    private static string CommonPrefix(IReadOnlyList<string> values)
    {
        if (values.Count == 0) return string.Empty;
        var prefix = values[0];
        foreach (var value in values.Skip(1))
        {
            var length = 0;
            while (length < prefix.Length && length < value.Length && char.ToUpperInvariant(prefix[length]) == char.ToUpperInvariant(value[length])) length++;
            prefix = prefix[..length];
            if (prefix.Length == 0) return string.Empty;
        }
        return prefix.Trim(' ', '_', '-', '(', ')', '（', '）');
    }
}

public sealed class MainViewModel : ObservableObject
{
    private readonly DesktopAnalysisAdapter adapter;
    private DesktopAnalysisSession? session;
    private FamilyItemViewModel? selectedFamily;
    private ParticipantItemViewModel? selectedParticipant;
    private string statusText = "把包含 Office 文件的文件夹拖到这里，或点击“选择文件夹”。";
    private string summaryTitle = "尚未选择版本";
    private string summaryText = "GitIt 只展示 Core 已给出的结论，不会在界面中重新推断来源。";
    private string changeTitle = "选择一条关系查看改动";
    private bool isBusy;
    private bool hasAnalysis;

    public MainViewModel(DesktopAnalysisAdapter? adapter = null)
    {
        this.adapter = adapter ?? new DesktopAnalysisAdapter();
        SelectNodeCommand = new RelayCommand(value => { if (value is GraphNodeViewModel node) ShowNode(node.Path); });
        SelectEdgeCommand = new RelayCommand(value => { if (value is GraphEdgeViewModel edge) ShowRelation(edge); });
        SelectTimelineCommand = new RelayCommand(value => { if (value is TimelineItemViewModel item) ShowNode(item.Path); });
    }

    public ObservableCollection<FamilyItemViewModel> Families { get; } = [];
    public ObservableCollection<GraphNodeViewModel> GraphNodes { get; } = [];
    public ObservableCollection<GraphEdgeViewModel> GraphEdges { get; } = [];
    public ObservableCollection<GraphEdgeViewModel> EdgeList { get; } = [];
    public ObservableCollection<TimelineItemViewModel> Timeline { get; } = [];
    public ObservableCollection<ParticipantItemViewModel> Participants { get; } = [];
    public ObservableCollection<string> SupportingEvidence { get; } = [];
    public ObservableCollection<string> ConcernEvidence { get; } = [];
    public ObservableCollection<DiffRowViewModel> DiffRows { get; } = [];
    public ObservableCollection<string> TechnicalDetails { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    public ICommand SelectNodeCommand { get; }
    public ICommand SelectEdgeCommand { get; }
    public ICommand SelectTimelineCommand { get; }
    public string StatusText { get => statusText; private set => Set(ref statusText, value); }
    public string SummaryTitle { get => summaryTitle; private set => Set(ref summaryTitle, value); }
    public string SummaryText { get => summaryText; private set => Set(ref summaryText, value); }
    public string ChangeTitle { get => changeTitle; private set => Set(ref changeTitle, value); }
    public bool IsBusy { get => isBusy; private set => Set(ref isBusy, value); }
    public bool HasAnalysis { get => hasAnalysis; private set => Set(ref hasAnalysis, value); }
    public string Summary => session is null ? "未分析文件夹" : $"发现 {session.Analysis.Versions.Count} 个 Office 文件 · {session.Analysis.DocumentFamilies.Count} 个文档家族 · {session.Lineage.Duplicates.Count} 个重复组";

    public FamilyItemViewModel? SelectedFamily
    {
        get => selectedFamily;
        set { if (Set(ref selectedFamily, value) && value is not null) BuildFamilyViews(value); }
    }

    public ParticipantItemViewModel? SelectedParticipant
    {
        get => selectedParticipant;
        set
        {
            if (Set(ref selectedParticipant, value) && value is not null)
            {
                SummaryTitle = value.Name;
                SummaryText = "参与者身份来自 Office 字符串；它不是经过认证的真实身份。";
                SupportingEvidence.Clear(); ConcernEvidence.Clear(); DiffRows.Clear(); TechnicalDetails.Clear();
                foreach (var evidence in value.Participant.Evidence) SupportingEvidence.Add($"{System.IO.Path.GetFileName(evidence.DocumentVersion)} · {evidence.EvidenceType} · {evidence.Strength}: {evidence.Detail}");
            }
        }
    }

    public async Task AnalyzeFolderAsync(string folder)
    {
        IsBusy = true; StatusText = "正在扫描与分析…";
        try { Load(await Task.Run(() => adapter.Analyze(folder))); StatusText = $"分析完成：{Summary}"; }
        catch (Exception exception) { StatusText = $"部分文件无法分析：{exception.Message}"; }
        finally { IsBusy = false; }
    }

    public void ShowMessage(string message) => StatusText = message;

    public void Load(DesktopAnalysisSession value)
    {
        session = value; HasAnalysis = true; Families.Clear(); Participants.Clear(); Warnings.Clear();
        foreach (var warning in value.Analysis.Warnings.Concat(value.Analysis.UnsupportedFeatures).Distinct(StringComparer.OrdinalIgnoreCase)) Warnings.Add(warning);
        foreach (var family in value.Analysis.DocumentFamilies)
        {
            var profiles = family.VersionIds.Select(path => value.Profiles[path]).ToArray();
            var paths = family.VersionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var duplicates = value.Analysis.Versions.Count(version => paths.Contains(version.Path) && version.DuplicateOf is not null);
            var related = value.Lineage.Candidates.Count(candidate => candidate.Status == LineageStatus.RelatedButUnproven && paths.Contains(candidate.From) && paths.Contains(candidate.To));
            var linked = value.Analysis.Edges.SelectMany(edge => new[] { edge.From, edge.To }).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Families.Add(new FamilyItemViewModel(family.Id, FamilyDisplayName.From(profiles, family.Kind), family.Kind, paths.Count, duplicates, paths.Count(path => !linked.Contains(path)), related));
        }
        foreach (var participant in value.Analysis.Participants) Participants.Add(new ParticipantItemViewModel(participant));
        SelectedFamily = Families.FirstOrDefault(); Raise(nameof(Summary));
    }

    private void BuildFamilyViews(FamilyItemViewModel family)
    {
        if (session is null) return;
        GraphNodes.Clear(); GraphEdges.Clear(); EdgeList.Clear(); Timeline.Clear();
        var familyPaths = session.Analysis.DocumentFamilies.Single(item => item.Id == family.Id).VersionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var edges = session.Analysis.Edges.Where(edge => familyPaths.Contains(edge.From) && familyPaths.Contains(edge.To)).ToArray();
        var related = session.Lineage.Candidates.Where(candidate => candidate.Status == LineageStatus.RelatedButUnproven && familyPaths.Contains(candidate.From) && familyPaths.Contains(candidate.To)).ToArray();
        var parent = edges.GroupBy(edge => edge.To, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.OrderByDescending(edge => edge.Confidence).First().From, StringComparer.OrdinalIgnoreCase);
        var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int Level(string path) => levels.TryGetValue(path, out var known) ? known : levels[path] = parent.TryGetValue(path, out var source) ? Level(source) + 1 : 0;
        foreach (var path in familyPaths) Level(path);
        var nodes = new Dictionary<string, GraphNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var level in levels.GroupBy(item => item.Value).OrderBy(group => group.Key))
        foreach (var item in level.OrderBy(item => Date(item.Key)).ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select((item, index) => new { item, index }))
        {
            var profile = session.Profiles[item.item.Key]; var edge = edges.SingleOrDefault(value => value.To == item.item.Key);
            var duplicate = session.Analysis.Versions.Single(version => version.Path == item.item.Key).DuplicateOf is not null;
            var badges = Badges(profile, duplicate); var status = duplicate ? "重复副本" : related.Any(candidate => candidate.To == item.item.Key) ? "相关但未证实" : edge?.Status.ToString() ?? "未关联";
            var node = new GraphNodeViewModel(profile.Path, System.IO.Path.GetFileName(profile.Path), profile.Kind.ToString().ToUpperInvariant(), Date(profile.Path).ToString("yyyy-MM-dd"), edge is null ? "未证实" : edge.Confidence.ToString("P0"), status, badges, 28 + level.Key * 230, 34 + item.index * 118, duplicate);
            nodes[node.Path] = node; GraphNodes.Add(node);
        }
        foreach (var edge in edges)
        {
            var item = new GraphEdgeViewModel(edge, nodes[edge.From].X + 182, nodes[edge.From].Y + 43, nodes[edge.To].X, nodes[edge.To].Y + 43);
            GraphEdges.Add(item); EdgeList.Add(item);
        }
        foreach (var candidate in related)
        {
            var item = new GraphEdgeViewModel(candidate, nodes[candidate.From].X + 182, nodes[candidate.From].Y + 60, nodes[candidate.To].X, nodes[candidate.To].Y + 60);
            GraphEdges.Add(item); EdgeList.Add(item);
        }
        foreach (var path in familyPaths.OrderBy(Date).ThenBy(path => path, StringComparer.OrdinalIgnoreCase)) Timeline.Add(BuildTimelineItem(path, edges.SingleOrDefault(edge => edge.To == path)));
        SummaryTitle = family.Name; SummaryText = "选择版本节点查看来源依据，选择关系查看语义改动；虚线、点线和警示线分别保留不同程度的不确定性。";
        SupportingEvidence.Clear(); ConcernEvidence.Clear(); DiffRows.Clear(); TechnicalDetails.Clear();
    }

    private TimelineItemViewModel BuildTimelineItem(string path, LineageEdge? edge)
    {
        var profile = session!.Profiles[path];
        var participants = profile.ParticipantEvidence.GroupBy(item => item.EvidenceType).Select(group => group.Key switch
        {
            "revision-author" => $"✏ 修改 {string.Join(", ", group.Select(item => item.Value).Distinct())}",
            "comment-author" => $"💬 评论 {string.Join(", ", group.Select(item => item.Value).Distinct())}",
            "lastModifiedBy" => $"💾 最后保存 {string.Join(", ", group.Select(item => item.Value).Distinct())}",
            "creator" => $"创建 {string.Join(", ", group.Select(item => item.Value).Distinct())}",
            _ => string.Join(", ", group.Select(item => item.Value).Distinct())
        }).ToArray();
        var diff = edge is null ? null : session.Analysis.Changes.SingleOrDefault(change => change.SourcePath == edge.From && change.TargetPath == edge.To);
        var changes = diff is null ? "未检测到可展示的已支持改动" : string.Join(" · ", diff.Changes.GroupBy(change => change.Category).Select(group => $"{DisplayCategory(group.Key)} {group.Count()}处"));
        return new TimelineItemViewModel(path, Date(path).ToString("yyyy-MM-dd HH:mm"), System.IO.Path.GetFileName(path), profile.Kind.ToString().ToUpperInvariant(), participants.Length == 0 ? "未发现参与者线索" : string.Join("；", participants), "编辑环境：Core 未提供此证据", changes, edge?.Status.ToString() ?? "未关联", Badges(profile, false));
    }

    private void ShowNode(string path)
    {
        if (session is null || !session.Profiles.TryGetValue(path, out var profile)) return;
        var parent = session.Analysis.Edges.SingleOrDefault(edge => edge.To == path);
        SummaryTitle = System.IO.Path.GetFileName(path);
        SummaryText = parent is null ? "该文件没有可断言的直接来源；这是 GitIt 保留不确定性的结果。" : $"该文件很可能来自：{System.IO.Path.GetFileName(parent.From)}（{parent.Confidence:P0}，{parent.Status}）。";
        SupportingEvidence.Clear(); ConcernEvidence.Clear(); DiffRows.Clear(); TechnicalDetails.Clear();
        if (parent is not null)
        {
            foreach (var evidence in parent.Evidence.Where(evidence => !evidence.IsConflict && evidence.Strength is EvidenceStrength.Strong or EvidenceStrength.Medium)) SupportingEvidence.Add($"✓ {evidence.Detail}");
            foreach (var evidence in parent.Evidence.Where(evidence => evidence.IsConflict || evidence.Strength == EvidenceStrength.Weak)) ConcernEvidence.Add($"⚠ {evidence.Detail}");
            foreach (var warning in parent.Warnings) ConcernEvidence.Add($"⚠ {warning}");
        }
        foreach (var warning in profile.UnsupportedFeatures) ConcernEvidence.Add($"⚠ {warning}");
        TechnicalDetails.Add($"Open XML file hash: {profile.FileHash}");
        TechnicalDetails.Add($"Creator: {profile.Metadata.Creator ?? "(none)"}");
        TechnicalDetails.Add($"LastModifiedBy: {profile.Metadata.LastModifiedBy ?? "(none)"}");
        TechnicalDetails.Add($"Modified: {Date(path):O}");
        TechnicalDetails.Add($"RSID count: {profile.Docx?.Rsids.Count ?? 0}; revisions: {profile.Docx?.RevisionKinds.Values.Sum() ?? 0}");
    }

    private void ShowRelation(GraphEdgeViewModel relation)
    {
        if (relation.Edge is null) { ShowRelated(relation.RelatedCandidate!); return; }
        var edge = relation.Edge; SummaryTitle = $"{System.IO.Path.GetFileName(edge.From)} → {System.IO.Path.GetFileName(edge.To)}";
        SummaryText = relation.Kind == GraphRelationKind.Conflicting ? "内容或结构支持此关系，但 Core 同时发现了冲突证据。" : $"{relation.AccessibleDescription}，可信度 {edge.Confidence:P0}。";
        SupportingEvidence.Clear(); ConcernEvidence.Clear(); DiffRows.Clear(); TechnicalDetails.Clear(); ChangeTitle = "改动（Core semantic diff）";
        foreach (var evidence in edge.Evidence.Where(evidence => !evidence.IsConflict && evidence.Strength is EvidenceStrength.Strong or EvidenceStrength.Medium)) SupportingEvidence.Add($"✓ {evidence.Detail}");
        foreach (var evidence in edge.Evidence.Where(evidence => evidence.IsConflict || evidence.Strength == EvidenceStrength.Weak)) ConcernEvidence.Add($"⚠ {evidence.Detail}");
        foreach (var warning in edge.Warnings) ConcernEvidence.Add($"⚠ {warning}");
        var diff = session!.Analysis.Changes.SingleOrDefault(change => change.SourcePath == edge.From && change.TargetPath == edge.To);
        if (diff is null) ConcernEvidence.Add("⚠ Core 未提供此关系的语义 Diff。");
        else foreach (var change in diff.Changes) DiffRows.Add(new DiffRowViewModel(DisplayCategory(change.Category), change.Location, change.Before ?? "", change.After ?? "", change.Detail, !string.IsNullOrWhiteSpace(change.Before) && string.IsNullOrWhiteSpace(change.After), string.IsNullOrWhiteSpace(change.Before) && !string.IsNullOrWhiteSpace(change.After)));
        foreach (var evidence in edge.Evidence) TechnicalDetails.Add($"{evidence.Type}: {evidence.Score:F2} ({evidence.Strength})");
    }

    private void ShowRelated(LineageCandidate candidate)
    {
        SummaryTitle = $"{System.IO.Path.GetFileName(candidate.From)} ··· ? ··· {System.IO.Path.GetFileName(candidate.To)}";
        SummaryText = "这些文件内容或结构相关，但 Core 没有足够来源证据来断言父子关系。";
        SupportingEvidence.Clear(); ConcernEvidence.Clear(); DiffRows.Clear(); TechnicalDetails.Clear(); ChangeTitle = "没有断言的父子关系";
        foreach (var evidence in candidate.Evidence.Where(evidence => !evidence.IsConflict)) SupportingEvidence.Add($"✓ {evidence.Detail}");
        foreach (var warning in candidate.Warnings) ConcernEvidence.Add($"⚠ {warning}");
        foreach (var evidence in candidate.Evidence.Where(evidence => evidence.IsConflict)) ConcernEvidence.Add($"⚠ {evidence.Detail}");
    }

    private DateTimeOffset Date(string path) => session!.Profiles[path].Metadata.Modified ?? session.Profiles[path].FileModified;
    private static string Badges(OfficeDocumentProfile profile, bool duplicate) => string.Join(" ", new[] { profile.UnsupportedFeatures.Count > 0 ? "🧩" : null, profile.UnsupportedFeatures.Any(text => text.Contains("No RSID", StringComparison.OrdinalIgnoreCase) || text.Contains("metadata", StringComparison.OrdinalIgnoreCase)) ? "⚠" : null, profile.ParticipantEvidence.Count > 0 ? "👥" : null, duplicate ? "🔗" : null }.Where(value => value is not null));
    private static string DisplayCategory(string category) => category.ToLowerInvariant() switch { "content" or "text" or "cell" => "内容", "format" => "格式", "structure" or "table" or "sheet" or "slide" => "结构", "formula" => "公式", _ => category };
}

