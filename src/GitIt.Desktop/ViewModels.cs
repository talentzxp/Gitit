using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GitIt.Core;
using GitIt.UserAnnotations;

namespace GitIt.Desktop;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
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

public sealed record FamilyItemViewModel(string Id, string Name, OfficeFileKind FileKind, IReadOnlyList<string> Paths, int Duplicates, int Unlinked, int Related, bool IsUserManaged)
{
    public string Kind => FileKind.ToString().ToUpperInvariant();
    public int Versions => Paths.Count;
    public string Origin => IsUserManaged ? "用户文档组" : "Core 自动家族";
    public string Summary => $"{Versions} 个文件 · {Duplicates} 个重复 · {Related} 个相关未证实 · {Unlinked} 个未关联";
}

public sealed record ManagedFileViewModel(string Path, string File, string Kind, string Status, string Detail)
{
    public override string ToString() => File;
}

public sealed record GraphNodeViewModel(string Path, string Label, string Kind, string Date, string Confidence, string Status, string Badges, double X, double Y, bool IsDuplicate, bool IsParticipantHighlight)
{
    public string HighlightBrush => IsParticipantHighlight ? "#DDEBFF" : "#FFFFFF";
}

public enum GraphRelationKind { Strong, Weak, RelatedButUnproven, Candidate, Conflicting, UserConfirmed }

public sealed class GraphEdgeViewModel
{
    public GraphEdgeViewModel(LineageEdge edge, double x1, double y1, double x2, double y2, UserConfirmedRelation? confirmation = null)
    {
        Edge = edge;
        Confirmation = confirmation;
        X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
        Kind = confirmation is not null ? GraphRelationKind.UserConfirmed : edge.Evidence.Any(item => item.IsConflict) ? GraphRelationKind.Conflicting : edge.Status == LineageStatus.Probable ? GraphRelationKind.Strong : GraphRelationKind.Weak;
    }

    public GraphEdgeViewModel(LineageCandidate candidate, double x1, double y1, double x2, double y2)
    {
        RelatedCandidate = candidate;
        X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
        Kind = candidate.Evidence.Any(item => item.IsConflict) ? GraphRelationKind.Conflicting : candidate.Status == LineageStatus.RelatedButUnproven ? GraphRelationKind.RelatedButUnproven : GraphRelationKind.Candidate;
    }

    public GraphEdgeViewModel(UserConfirmedRelation confirmation, double x1, double y1, double x2, double y2)
    {
        Confirmation = confirmation;
        X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
        Kind = GraphRelationKind.UserConfirmed;
    }

    public LineageEdge? Edge { get; }
    public LineageCandidate? RelatedCandidate { get; }
    public UserConfirmedRelation? Confirmation { get; }
    public string Source => Edge?.From ?? RelatedCandidate?.From ?? Confirmation!.Source;
    public string Target => Edge?.To ?? RelatedCandidate?.To ?? Confirmation!.Target;
    public double X1 { get; }
    public double Y1 { get; }
    public double X2 { get; }
    public double Y2 { get; }
    public GraphRelationKind Kind { get; }
    public string Stroke => Kind switch { GraphRelationKind.UserConfirmed => "#1570EF", GraphRelationKind.Conflicting => "#B42318", GraphRelationKind.RelatedButUnproven => "#7A5AF8", GraphRelationKind.Candidate => "#98A2B3", _ => "#426B9B" };
    public string Dash => Kind switch { GraphRelationKind.Strong => string.Empty, GraphRelationKind.Weak => "6,4", GraphRelationKind.RelatedButUnproven => "1,4", GraphRelationKind.Candidate => "2,5", GraphRelationKind.UserConfirmed => "10,3", _ => "8,3,2,3" };
    public string Marker => Kind switch { GraphRelationKind.Strong => "→", GraphRelationKind.Weak => "⇢", GraphRelationKind.RelatedButUnproven => "?", GraphRelationKind.UserConfirmed => "✓", _ => "⚠" };
    public string AccessibleDescription => Kind switch { GraphRelationKind.Strong => "较强 Core 来源关系", GraphRelationKind.Weak => "较弱 Core 来源关系", GraphRelationKind.RelatedButUnproven => "相关但来源未证实", GraphRelationKind.Candidate => "候选来源；强血缘证据不足", GraphRelationKind.UserConfirmed when Edge is not null => "Core 推断加用户确认", GraphRelationKind.UserConfirmed => "用户确认来源关系", _ => "证据存在冲突" };
    public string Label => Edge is not null
        ? $"{Marker} {System.IO.Path.GetFileName(Source)} → {System.IO.Path.GetFileName(Target)} · {Edge.Confidence:P0} · {AccessibleDescription}"
        : RelatedCandidate is not null
            ? $"{Marker} {System.IO.Path.GetFileName(Source)} ··· ? ··· {System.IO.Path.GetFileName(Target)} · {RelatedCandidate.Confidence:P0} · {AccessibleDescription}"
            : $"{Marker} {System.IO.Path.GetFileName(Source)} → {System.IO.Path.GetFileName(Target)} · {AccessibleDescription}";
}

public sealed record TimelineItemViewModel(string Path, DateTimeOffset SortTime, string Date, string TimePrecision, string TimeDescription, string EventIcon, string EventType, string File, string Kind, string Participant, string Evidence, string ChangeSummary, string Status, string Badges, bool IsParticipantHighlight)
{
    public string HighlightBrush => IsParticipantHighlight ? "#DDEBFF" : "#FFFFFF";
}
public sealed record DiffRowViewModel(string Category, string Location, string Before, string After, string Detail, bool IsRemoval, bool IsAddition);
public sealed record CandidateSourceItemViewModel(string Source, string Target, string Confidence, string Status, string Support, string Missing, LineageCandidate Candidate, bool IsUserConfirmed, bool IsReviewed);

public sealed record ParticipantItemViewModel(ParticipantIdentity Participant)
{
    public string Name => Participant.DisplayName;
    public string Summary => $"{Participant.Evidence.Count} 条参与线索 · {string.Join(" / ", Participant.Evidence.Select(item => Role(item.EvidenceType)).Distinct())}";
    public string Roles => string.Join("；", Participant.Evidence.GroupBy(item => item.EvidenceType).Select(group => $"{Role(group.Key)} {group.Count()}"));
    private static string Role(string type) => type switch { "creator" => "Creator", "revision-author" => "Revision Author", "comment-author" => "Comment Author", "lastModifiedBy" => "Possible Participant", _ => "Possible Participant" };
}

public sealed record FolderScanPreview(string Folder, int OfficeFiles, int TemporaryOfficeFiles, int OtherFiles, IReadOnlyDictionary<string, int> SkippedByExtension)
{
    public string Summary => $"发现 Office 文件：{OfficeFiles}；临时 Office 文件：{TemporaryOfficeFiles}；其他文件：{OtherFiles}";
    public string SkippedSummary => SkippedByExtension.Count == 0 ? "没有需要跳过的非 Office 文件。" : string.Join(" · ", SkippedByExtension.OrderByDescending(item => item.Value).Take(5).Select(item => $"{item.Key} {item.Value}"));

    public static FolderScanPreview Create(string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Folder not found: {folder}");
        var office = 0; var temporary = 0; var other = 0;
        var skipped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var extension = System.IO.Path.GetExtension(path);
            var supported = extension.Equals(".docx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase);
            if (supported && System.IO.Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal)) { temporary++; continue; }
            if (supported) { office++; continue; }
            other++;
            var key = string.IsNullOrWhiteSpace(extension) ? "无扩展名" : extension.ToLowerInvariant();
            skipped[key] = skipped.TryGetValue(key, out var count) ? count + 1 : 1;
        }
        return new FolderScanPreview(System.IO.Path.GetFullPath(folder), office, temporary, other, skipped);
    }
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
    private readonly UserAnnotationProjectStore projectStore = new();
    private readonly Dictionary<string, LocalGroupAnalysis> localGroupAnalyses = new(StringComparer.OrdinalIgnoreCase);
    private DesktopAnalysisSession? session;
    private UserAnnotationProject annotations = new();
    private FamilyItemViewModel? selectedFamily;
    private ParticipantItemViewModel? selectedParticipant;
    private GraphEdgeViewModel? selectedRelation;
    private string? selectedNodePath;
    private FolderScanPreview? pendingPreview;
    private string statusText = "选择文件夹后，GitIt 会先展示扫描范围，再由你决定是否开始分析。";
    private string summaryTitle = "尚未选择版本";
    private string summaryText = "GitIt 展示 Core 结论与用户标注；两者始终分开保存。";
    private string changeTitle = "选择一条关系查看改动";
    private string noteText = string.Empty;
    private string searchText = string.Empty;
    private string diffTitle = "尚未选择比较";
    private string selectedDiffCategory = "全部";
    private SemanticDiffResult? activeDiff;
    private bool showCandidateRelations;
    private bool hasDiffWorkbench;
    private bool isUnifiedDiff;
    private int localReanalysisCount;
    private int workspaceTabIndex;
    private bool isBusy;
    private bool hasAnalysis;

    public MainViewModel(DesktopAnalysisAdapter? adapter = null)
    {
        this.adapter = adapter ?? new DesktopAnalysisAdapter();
        SelectNodeCommand = new RelayCommand(value => { if (value is GraphNodeViewModel node) ShowNode(node.Path); });
        SelectEdgeCommand = new RelayCommand(value => { if (value is GraphEdgeViewModel edge) ShowRelation(edge); });
        SelectTimelineCommand = new RelayCommand(value => { if (value is TimelineItemViewModel item) ShowNode(item.Path); });
        ConfirmCandidateCommand = new RelayCommand(value => { if (value is CandidateSourceItemViewModel candidate) ConfirmCandidate(candidate); });
        ConfirmSelectedRelationCommand = new RelayCommand(_ => ConfirmSelectedRelation());
        SaveNoteCommand = new RelayCommand(_ => SaveCurrentNote());
        OpenDiffWorkbenchCommand = new RelayCommand(_ => OpenDiffWorkbench());
        OpenCandidateDiffCommand = new RelayCommand(value => { if (value is CandidateSourceItemViewModel candidate) OpenCandidateDiff(candidate); });
        KeepCandidateUnconfirmedCommand = new RelayCommand(value => { if (value is CandidateSourceItemViewModel candidate) KeepCandidateUnconfirmed(candidate); });
        CompareSelectedFilesCommand = new RelayCommand(_ => CompareSelectedFiles());
        SetDiffCategoryCommand = new RelayCommand(value => SetDiffCategory(value as string ?? "全部"));
    }

    public ObservableCollection<FamilyItemViewModel> Families { get; } = [];
    public ObservableCollection<GraphNodeViewModel> GraphNodes { get; } = [];
    public ObservableCollection<GraphEdgeViewModel> GraphEdges { get; } = [];
    public ObservableCollection<GraphEdgeViewModel> EdgeList { get; } = [];
    public ObservableCollection<TimelineItemViewModel> Timeline { get; } = [];
    public ObservableCollection<ParticipantItemViewModel> Participants { get; } = [];
    public ObservableCollection<ManagedFileViewModel> UnlinkedFiles { get; } = [];
    public ObservableCollection<ManagedFileViewModel> SearchResults { get; } = [];
    public ObservableCollection<CandidateSourceItemViewModel> CandidateSources { get; } = [];
    public ObservableCollection<string> SupportingEvidence { get; } = [];
    public ObservableCollection<string> ConcernEvidence { get; } = [];
    public ObservableCollection<DiffRowViewModel> DiffRows { get; } = [];
    public ObservableCollection<string> TechnicalDetails { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    public ObservableCollection<string> DiffCategories { get; } = ["全部", "内容", "格式", "结构", "公式"];
    public ICommand SelectNodeCommand { get; }
    public ICommand SelectEdgeCommand { get; }
    public ICommand SelectTimelineCommand { get; }
    public ICommand ConfirmCandidateCommand { get; }
    public ICommand ConfirmSelectedRelationCommand { get; }
    public ICommand SaveNoteCommand { get; }
    public ICommand OpenDiffWorkbenchCommand { get; }
    public ICommand OpenCandidateDiffCommand { get; }
    public ICommand KeepCandidateUnconfirmedCommand { get; }
    public ICommand CompareSelectedFilesCommand { get; }
    public ICommand SetDiffCategoryCommand { get; }
    public string StatusText { get => statusText; private set => Set(ref statusText, value); }
    public string SummaryTitle { get => summaryTitle; private set => Set(ref summaryTitle, value); }
    public string SummaryText { get => summaryText; private set => Set(ref summaryText, value); }
    public string ChangeTitle { get => changeTitle; private set => Set(ref changeTitle, value); }
    public string NoteText { get => noteText; set => Set(ref noteText, value); }
    public string SearchText { get => searchText; private set => Set(ref searchText, value); }
    public bool IsBusy { get => isBusy; private set => Set(ref isBusy, value); }
    public bool HasAnalysis { get => hasAnalysis; private set => Set(ref hasAnalysis, value); }
    public bool HasPendingPreview => pendingPreview is not null;
    public string ScanSummary => pendingPreview?.Summary ?? string.Empty;
    public string ScanSkippedSummary => pendingPreview?.SkippedSummary ?? string.Empty;
    public int ConfirmedRelationCount => annotations.ConfirmedRelations.Count;
    public int LocalReanalysisCount => localReanalysisCount;
    public bool ShowCandidateRelations { get => showCandidateRelations; set { if (Set(ref showCandidateRelations, value) && SelectedFamily is not null) BuildFamilyViews(SelectedFamily); } }
    public bool HasDiffWorkbench { get => hasDiffWorkbench; private set => Set(ref hasDiffWorkbench, value); }
    public bool IsUnifiedDiff { get => isUnifiedDiff; set => Set(ref isUnifiedDiff, value); }
    public string DiffTitle { get => diffTitle; private set => Set(ref diffTitle, value); }
    public int WorkspaceTabIndex { get => workspaceTabIndex; set => Set(ref workspaceTabIndex, value); }
    public string SelectedDiffCategory { get => selectedDiffCategory; private set => Set(ref selectedDiffCategory, value); }
    public string Summary => session is null ? "未分析文件夹" : $"发现 {VisiblePaths().Count} 个可见 Office 文件 · {Families.Count} 个文档组 · {annotations.ConfirmedRelations.Count} 条用户确认关系";

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
            if (!Set(ref selectedParticipant, value) || value is null) return;
            if (SelectedFamily is not null) BuildFamilyViews(SelectedFamily);
            SummaryTitle = value.Name;
            SummaryText = "参与者身份来自 Office 字符串；它不是经过认证的真实身份。已高亮该身份相关版本与时间线事件。";
            SupportingEvidence.Clear(); ConcernEvidence.Clear(); DiffRows.Clear(); TechnicalDetails.Clear(); CandidateSources.Clear();
            foreach (var evidence in value.Participant.Evidence) SupportingEvidence.Add($"{System.IO.Path.GetFileName(evidence.DocumentVersion)} · {RoleEvent(evidence.EvidenceType)} · {evidence.Strength}: {evidence.Detail}");
        }
    }

    public async Task PreviewFolderAsync(string folder)
    {
        IsBusy = true; StatusText = "正在扫描文件夹范围（尚未分析）…";
        try
        {
            pendingPreview = await Task.Run(() => FolderScanPreview.Create(folder));
            StatusText = $"扫描完成：{pendingPreview.Summary}。请确认后开始分析。";
        }
        catch (Exception exception) { StatusText = $"无法扫描文件夹：{exception.Message}"; pendingPreview = null; }
        finally { IsBusy = false; Raise(nameof(HasPendingPreview)); Raise(nameof(ScanSummary)); Raise(nameof(ScanSkippedSummary)); }
    }

    public async Task AnalyzePendingFolderAsync()
    {
        if (pendingPreview is null) { ShowMessage("请先选择或拖入一个文件夹。"); return; }
        await AnalyzeFolderAsync(pendingPreview.Folder);
        pendingPreview = null; Raise(nameof(HasPendingPreview)); Raise(nameof(ScanSummary)); Raise(nameof(ScanSkippedSummary));
    }

    public void CancelPreview()
    {
        pendingPreview = null; StatusText = "已取消，本次未分析任何文件。";
        Raise(nameof(HasPendingPreview)); Raise(nameof(ScanSummary)); Raise(nameof(ScanSkippedSummary));
    }

    public async Task AnalyzeFolderAsync(string folder)
    {
        IsBusy = true; StatusText = "正在分析 Office 文件…";
        try { Load(await Task.Run(() => adapter.Analyze(folder)), new UserAnnotationProject { AnalysisRoot = System.IO.Path.GetFullPath(folder) }); StatusText = $"分析完成：{Summary}"; }
        catch (Exception exception) { StatusText = $"部分文件无法分析：{exception.Message}"; }
        finally { IsBusy = false; }
    }

    public void Load(DesktopAnalysisSession value, UserAnnotationProject? userProject = null)
    {
        session = value;
        annotations = userProject ?? new UserAnnotationProject { AnalysisRoot = value.Analysis.Project.Root };
        annotations.AnalysisRoot = string.IsNullOrWhiteSpace(annotations.AnalysisRoot) ? value.Analysis.Project.Root : annotations.AnalysisRoot;
        localGroupAnalyses.Clear(); localReanalysisCount = 0;
        foreach (var group in annotations.DocumentGroups) ReanalyzeUserGroup(group);
        HasAnalysis = true;
        Warnings.Clear(); Participants.Clear();
        foreach (var warning in value.Analysis.Warnings.Concat(value.Analysis.UnsupportedFeatures).Distinct(StringComparer.OrdinalIgnoreCase)) Warnings.Add(warning);
        foreach (var participant in value.Analysis.Participants) Participants.Add(new ParticipantItemViewModel(participant));
        RebuildOverlayViews();
    }

    public void SetSelectedFiles(IEnumerable<ManagedFileViewModel> files) => selectedManagedFiles = files.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private List<string> selectedManagedFiles = [];

    public bool CreateUserGroup(string name)
    {
        var paths = selectedManagedFiles.Where(path => VisiblePaths().Contains(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length < 2) { ShowMessage("请选择至少两个可见文件后再创建文档组。"); return false; }
        if (string.IsNullOrWhiteSpace(name)) { ShowMessage("文档组需要一个名称。"); return false; }
        var group = new UserDocumentGroup { Name = name.Trim(), Files = paths.ToList() };
        annotations.DocumentGroups.Add(group);
        ReanalyzeUserGroup(group);
        RebuildOverlayViews(); ShowNode(paths[^1]); StatusText = $"已创建用户文档组“{name.Trim()}”；这不会断言父子关系。"; return true;
    }

    public bool AddSelectedFilesToGroup()
    {
        if (SelectedFamily is null || !SelectedFamily.IsUserManaged) { ShowMessage("请先选择一个“用户文档组”，然后从未关联文件中选择文件加入。" ); return false; }
        return AddFilesToGroup(SelectedFamily, selectedManagedFiles);
    }

    public bool AddFilesToGroup(FamilyItemViewModel family, IEnumerable<string> files)
    {
        if (!family.IsUserManaged) { ShowMessage("文件只能加入用户文档组；自动家族仍由 Core 发现。" ); return false; }
        var group = annotations.DocumentGroups.Single(item => item.GroupId == family.Id);
        var additions = files.Where(path => VisiblePaths().Contains(path) && !group.Files.Contains(path, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (additions.Length == 0) { ShowMessage("没有可加入的新增文件。" ); return false; }
        group.Files.AddRange(additions);
        StatusText = "正在重新分析此文档组…";
        ReanalyzeUserGroup(group);
        RebuildOverlayViews(); SelectedFamily = Families.Single(item => item.Id == group.GroupId); ShowNode(additions[^1]); StatusText = $"已加入 {additions.Length} 个文件，并已仅对该用户文档组重新分析候选来源、血缘和 Diff。"; return true;
    }

    public bool RenameSelectedFamily(string name)
    {
        if (SelectedFamily is null || string.IsNullOrWhiteSpace(name)) return false;
        if (SelectedFamily.IsUserManaged) annotations.DocumentGroups.Single(item => item.GroupId == SelectedFamily.Id).Name = name.Trim();
        else annotations.FamilyNames[SelectedFamily.Id] = name.Trim();
        var id = SelectedFamily.Id; RebuildOverlayViews(); SelectedFamily = Families.Single(item => item.Id == id); StatusText = $"已重命名文档组为“{name.Trim()}”。"; return true;
    }

    public void HideSelectedFamily()
    {
        if (SelectedFamily is null) return;
        foreach (var path in SelectedFamily.Paths.Where(path => annotations.HiddenItems.All(item => !Same(item.Path, path)))) annotations.HiddenItems.Add(new UserHiddenItem { Path = path });
        RebuildOverlayViews(); StatusText = "已从分析视图移除该文档组；原始文件未被删除。";
    }

    public void RestoreHiddenFiles()
    {
        annotations.HiddenItems.Clear(); RebuildOverlayViews(); StatusText = "已恢复所有仅在视图中隐藏的文件。";
    }

    public void ConfirmCandidate(CandidateSourceItemViewModel candidate) => ConfirmRelation(candidate.Source, candidate.Target);
    public void ConfirmSelectedRelation()
    {
        if (selectedRelation is null) { ShowMessage("请先选择一条 Core 关系或候选关系。" ); return; }
        ConfirmRelation(selectedRelation.Source, selectedRelation.Target);
    }

    private void ConfirmRelation(string source, string target)
    {
        if (annotations.ConfirmedRelations.Any(item => Same(item.Source, source) && Same(item.Target, target))) { ShowMessage("该来源关系已经由用户确认。" ); return; }
        annotations.ConfirmedRelations.Add(new UserConfirmedRelation { Source = source, Target = target });
        annotations.CandidateReviews.RemoveAll(item => Same(item.Source, source) && Same(item.Target, target));
        annotations.CandidateReviews.Add(new UserCandidateReview { Source = source, Target = target, State = "confirmed" });
        RebuildOverlayViews(); Raise(nameof(ConfirmedRelationCount));
        if (SelectedFamily is not null) SelectedFamily = Families.FirstOrDefault(item => item.Paths.Contains(target, StringComparer.OrdinalIgnoreCase));
        StatusText = $"已记录用户确认：{System.IO.Path.GetFileName(source)} 是 {System.IO.Path.GetFileName(target)} 的来源。Core 推断未被修改。";
    }

    public void SaveCurrentNote()
    {
        if (string.IsNullOrWhiteSpace(selectedNodePath)) { ShowMessage("请先选择一个文件再保存备注。" ); return; }
        if (string.IsNullOrWhiteSpace(NoteText)) annotations.Notes.Remove(selectedNodePath);
        else annotations.Notes[selectedNodePath] = NoteText.Trim();
        StatusText = "已保存用户备注；备注不参与 Core 推断。";
    }

    public void SaveProject(string path)
    {
        if (session is null) { ShowMessage("没有可保存的分析项目。" ); return; }
        annotations.AnalysisRoot = session.Analysis.Project.Root;
        annotations.AnalysisCache = new AnalysisCache { Analysis = session.Analysis, Profiles = session.Profiles.Values.ToList(), Lineage = session.Lineage };
        projectStore.Save(path, annotations);
        StatusText = $"已保存 GitIt 项目：{System.IO.Path.GetFileName(path)}。项目只保存分析与标注，不保存原始 Office 文件。";
    }

    public void OpenProject(string path)
    {
        var project = projectStore.Load(path);
        if (project.AnalysisCache?.Analysis is null || project.AnalysisCache.Lineage is null || project.AnalysisCache.Profiles.Count == 0)
            throw new InvalidDataException("该 GitIt 项目没有可用的分析快照，请重新选择原始文件夹分析。");
        var profiles = project.AnalysisCache.Profiles.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
        Load(new DesktopAnalysisSession(project.AnalysisCache.Analysis, profiles, project.AnalysisCache.Lineage), project);
        StatusText = $"已从项目快照打开：{System.IO.Path.GetFileName(path)}。未重新读取原始 Office 文件。";
    }

    public void Search(string query)
    {
        SearchText = query ?? string.Empty; SearchResults.Clear();
        if (session is null || string.IsNullOrWhiteSpace(SearchText)) return;
        foreach (var path in VisiblePaths().Where(path => MatchesSearch(session.Profiles[path], SearchText)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Take(100)) SearchResults.Add(ToManagedFile(path));
    }

    public void ShowMessage(string message) => StatusText = message;

    private void RebuildOverlayViews()
    {
        if (session is null) return;
        BuildFamilies(); BuildUnlinkedFiles(); Search(SearchText);
        selectedFamily = null;
        Raise(nameof(SelectedFamily));
        SelectedFamily = Families.FirstOrDefault(); Raise(nameof(Summary));
    }

    private void ReanalyzeUserGroup(UserDocumentGroup group)
    {
        if (session is null) return;
        var profiles = group.Files.Where(session.Profiles.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => session.Profiles[path]).ToArray();
        if (profiles.Length < 2) { localGroupAnalyses.Remove(group.GroupId); return; }
        localGroupAnalyses[group.GroupId] = adapter.AnalyzeGroup(profiles);
        localReanalysisCount++;
        Raise(nameof(LocalReanalysisCount));
    }

    private LineageResult LineageFor(FamilyItemViewModel family) => family.IsUserManaged && localGroupAnalyses.TryGetValue(family.Id, out var local) ? local.Lineage : session!.Lineage;

    private void BuildFamilies()
    {
        Families.Clear();
        var visible = VisiblePaths();
        var manualMembership = annotations.DocumentGroups.SelectMany(group => group.Files).Where(visible.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in annotations.DocumentGroups)
        {
            var paths = group.Files.Where(visible.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (paths.Length == 0) continue;
            var kind = session!.Profiles[paths[0]].Kind;
            Families.Add(FamilyItem(group.GroupId, group.Name, kind, paths, true));
        }
        foreach (var family in session!.Analysis.DocumentFamilies)
        {
            var paths = family.VersionIds.Where(path => visible.Contains(path) && !manualMembership.Contains(path)).ToArray();
            if (paths.Length == 0) continue;
            var profiles = paths.Select(path => session.Profiles[path]).ToArray();
            var name = annotations.FamilyNames.TryGetValue(family.Id, out var renamed) ? renamed : FamilyDisplayName.From(profiles, family.Kind);
            Families.Add(FamilyItem(family.Id, name, family.Kind, paths, false));
        }
    }

    private FamilyItemViewModel FamilyItem(string id, string name, OfficeFileKind kind, IReadOnlyList<string> paths, bool manual)
    {
        var duplicated = session!.Analysis.Versions.Count(version => paths.Contains(version.Path, StringComparer.OrdinalIgnoreCase) && version.DuplicateOf is not null);
        var related = session.Lineage.Candidates.Count(item => item.Status == LineageStatus.RelatedButUnproven && paths.Contains(item.From, StringComparer.OrdinalIgnoreCase) && paths.Contains(item.To, StringComparer.OrdinalIgnoreCase));
        var linked = session.Analysis.Edges.SelectMany(item => new[] { item.From, item.To }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new FamilyItemViewModel(id, name, kind, paths, duplicated, paths.Count(path => !linked.Contains(path)), related, manual);
    }

    private void BuildUnlinkedFiles()
    {
        UnlinkedFiles.Clear();
        var linked = session!.Analysis.Edges.SelectMany(item => new[] { item.From, item.To }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in VisiblePaths().Where(path => !linked.Contains(path)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) UnlinkedFiles.Add(ToManagedFile(path));
    }

    private void BuildFamilyViews(FamilyItemViewModel family)
    {
        if (session is null) return;
        GraphNodes.Clear(); GraphEdges.Clear(); EdgeList.Clear(); Timeline.Clear();
        var familyPaths = family.Paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lineage = LineageFor(family);
        var edges = lineage.Edges.Where(edge => familyPaths.Contains(edge.From) && familyPaths.Contains(edge.To)).ToArray();
        var related = lineage.Candidates.Where(candidate => candidate.Status == LineageStatus.RelatedButUnproven && familyPaths.Contains(candidate.From) && familyPaths.Contains(candidate.To)).ToArray();
        var candidates = lineage.Candidates.Where(candidate => candidate.Status != LineageStatus.RelatedButUnproven && familyPaths.Contains(candidate.From) && familyPaths.Contains(candidate.To) && !edges.Any(edge => Same(edge.From, candidate.From) && Same(edge.To, candidate.To))).ToArray();
        var confirmations = annotations.ConfirmedRelations.Where(item => familyPaths.Contains(item.Source) && familyPaths.Contains(item.Target)).ToArray();
        var parents = edges.GroupBy(item => item.To, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.Confidence).First().From, StringComparer.OrdinalIgnoreCase);
        var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int Level(string path)
        {
            if (levels.TryGetValue(path, out var known)) return known;
            if (!visiting.Add(path)) return 0;
            var level = parents.TryGetValue(path, out var source) ? Level(source) + 1 : 0;
            visiting.Remove(path); return levels[path] = level;
        }
        foreach (var path in familyPaths) Level(path);
        var nodes = new Dictionary<string, GraphNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var level in levels.GroupBy(item => item.Value).OrderBy(group => group.Key))
        foreach (var item in level.OrderBy(item => Date(item.Key)).ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select((item, index) => new { item, index }))
        {
            var profile = session.Profiles[item.item.Key]; var edge = edges.SingleOrDefault(value => Same(value.To, item.item.Key));
            var duplicate = session.Analysis.Versions.Single(version => Same(version.Path, item.item.Key)).DuplicateOf is not null;
            var highlighted = selectedParticipant?.Participant.Evidence.Any(evidence => Same(evidence.DocumentVersion, item.item.Key)) == true;
            var badges = Badges(profile, duplicate) + (highlighted ? " 👤" : string.Empty);
            var status = duplicate ? "重复副本" : confirmations.Any(value => Same(value.Target, item.item.Key)) ? "用户确认来源" : related.Any(candidate => Same(candidate.To, item.item.Key)) ? "相关但未证实" : edge?.Status.ToString() ?? "未关联";
            var node = new GraphNodeViewModel(profile.Path, System.IO.Path.GetFileName(profile.Path), profile.Kind.ToString().ToUpperInvariant(), Date(profile.Path).ToString("yyyy-MM-dd"), edge is null ? "未证实" : edge.Confidence.ToString("P0"), status, badges, 28 + level.Key * 230, 34 + item.index * 118, duplicate, highlighted);
            nodes[node.Path] = node; GraphNodes.Add(node);
        }
        foreach (var edge in edges)
        {
            var confirmation = confirmations.FirstOrDefault(item => Same(item.Source, edge.From) && Same(item.Target, edge.To));
            AddGraphEdge(new GraphEdgeViewModel(edge, nodes[edge.From].X + 182, nodes[edge.From].Y + 43, nodes[edge.To].X, nodes[edge.To].Y + 43, confirmation));
        }
        foreach (var candidate in related.Where(candidate => !confirmations.Any(item => Same(item.Source, candidate.From) && Same(item.Target, candidate.To)))) AddGraphEdge(new GraphEdgeViewModel(candidate, nodes[candidate.From].X + 182, nodes[candidate.From].Y + 60, nodes[candidate.To].X, nodes[candidate.To].Y + 60));
        if (ShowCandidateRelations) foreach (var candidate in candidates.Where(candidate => !confirmations.Any(item => Same(item.Source, candidate.From) && Same(item.Target, candidate.To)))) AddGraphEdge(new GraphEdgeViewModel(candidate, nodes[candidate.From].X + 182, nodes[candidate.From].Y + 68, nodes[candidate.To].X, nodes[candidate.To].Y + 68));
        foreach (var confirmation in confirmations.Where(confirmation => !edges.Any(edge => Same(edge.From, confirmation.Source) && Same(edge.To, confirmation.Target)))) AddGraphEdge(new GraphEdgeViewModel(confirmation, nodes[confirmation.Source].X + 182, nodes[confirmation.Source].Y + 60, nodes[confirmation.Target].X, nodes[confirmation.Target].Y + 60));
        BuildTimeline(familyPaths, edges);
        SummaryTitle = family.Name;
        SummaryText = family.IsUserManaged ? "这是用户组织的文档组：它只提供分析上下文，不等同于确认的父子关系。" : "选择版本节点查看来源依据，选择关系查看语义改动；虚线、点线和警示线保留不同程度的不确定性。";
        SupportingEvidence.Clear(); ConcernEvidence.Clear(); DiffRows.Clear(); TechnicalDetails.Clear(); CandidateSources.Clear();
    }

    private void AddGraphEdge(GraphEdgeViewModel edge) { GraphEdges.Add(edge); EdgeList.Add(edge); }

    private void BuildTimeline(IReadOnlySet<string> familyPaths, IReadOnlyList<LineageEdge> edges)
    {
        var events = new List<TimelineEvent>();
        var ordered = familyPaths.OrderBy(Date).ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var firstSeen = new Dictionary<string, (string Path, DateTimeOffset Date)>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < ordered.Length; index++)
        {
            var path = ordered[index]; var profile = session!.Profiles[path]; var versionTime = Date(path); var edge = edges.SingleOrDefault(value => Same(value.To, path));
            var creator = profile.ParticipantEvidence.FirstOrDefault(item => item.EvidenceType == "creator")?.Value;
            events.Add(new TimelineEvent(TimelineEventType.Created, creator, path, profile.Metadata.Created ?? versionTime, null, profile.Metadata.Created is null ? TimePrecision.VersionTime : TimePrecision.Exact, "creator", EvidenceStrength.Medium, profile.Metadata.Created is null ? "创建时间未保存；时间取自版本时间。" : "Evidence: package Created."));
            var exactRevisionAuthors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var revision in profile.Docx?.RevisionEvents ?? Array.Empty<RevisionEvent>())
            {
                exactRevisionAuthors.Add(revision.Author);
                events.Add(new TimelineEvent(TimelineEventType.Modified, revision.Author, path, revision.Date ?? versionTime, null, revision.Date is null ? TimePrecision.VersionTime : TimePrecision.Exact, "revision-author", EvidenceStrength.Strong, revision.Date is null ? "Evidence: Revision Author；时间取自版本时间。" : $"Evidence: Revision Author + Revision Date ({revision.Kind})."));
            }
            foreach (var evidence in profile.ParticipantEvidence.Where(item => item.EvidenceType != "creator" && !(item.EvidenceType == "revision-author" && exactRevisionAuthors.Contains(item.Value))))
            {
                var eventType = evidence.EvidenceType switch { "revision-author" => TimelineEventType.Modified, "comment-author" => TimelineEventType.Commented, "lastModifiedBy" => TimelineEventType.Saved, _ => TimelineEventType.Participated };
                events.Add(new TimelineEvent(eventType, evidence.Value, path, versionTime, null, TimePrecision.VersionTime, evidence.EvidenceType, evidence.Strength, evidence.EvidenceType switch { "comment-author" => "Evidence: Comment Author；评论证明参与，不证明修改。时间取自版本时间。", "lastModifiedBy" => "Evidence: LastModifiedBy；保存线索不是认证身份。时间取自版本时间。", "revision-author" => "Evidence: Revision Author；未保存独立修订时间，时间取自版本时间。", _ => $"Evidence: {evidence.EvidenceType}；时间取自版本时间。" }));
            }
            foreach (var person in profile.ParticipantEvidence.Select(item => item.Value).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (firstSeen.ContainsKey(person)) continue;
                if (index > 0 && !events.Any(item => Same(item.Version, path) && Same(item.Participant ?? string.Empty, person) && item.EventType == TimelineEventType.Modified && item.TimePrecision == TimePrecision.Exact))
                {
                    var previous = Date(ordered[index - 1]);
                    events.Add(new TimelineEvent(TimelineEventType.Participated, person, path, previous, versionTime, TimePrecision.EstimatedInterval, "first-participation-evidence", EvidenceStrength.Weak, "首次出现参与证据；这是版本间隔推定，不是精确参与时间。"));
                }
                firstSeen[person] = (path, versionTime);
            }
            events.Add(new TimelineEvent(TimelineEventType.VersionObserved, null, path, versionTime, null, TimePrecision.VersionTime, "version-observed", EvidenceStrength.Weak, TimelineChanges(path, edge)));
        }
        foreach (var item in events.OrderBy(item => item.Start ?? DateTimeOffset.MaxValue).ThenBy(item => item.Version, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.EventType)) Timeline.Add(ToTimelineItem(item, edges));
    }

    private TimelineItemViewModel ToTimelineItem(TimelineEvent item, IReadOnlyList<LineageEdge> edges)
    {
        var profile = session!.Profiles[item.Version];
        var (date, precision, detail) = item.TimePrecision switch
        {
            TimePrecision.Exact => (item.Start?.ToString("yyyy-MM-dd HH:mm") ?? "时间未知", "精确", "时间由独立 Office 事件证据保存。"),
            TimePrecision.VersionTime => (item.Start?.ToString("yyyy-MM-dd HH:mm") ?? "时间未知", "版本时间", "时间取自版本时间，而非独立参与事件时间。"),
            TimePrecision.EstimatedInterval => ($"{item.Start:yyyy-MM-dd} ～ {item.End:yyyy-MM-dd}", "区间推定", "这是区间推定，不是精确事件时间。"),
            _ => ("时间未知", "未知", "Office 包未保存可用时间。")
        };
        var (icon, name) = item.EventType switch { TimelineEventType.Created => ("📄", "创建"), TimelineEventType.Modified => ("✏", "修改"), TimelineEventType.Commented => ("💬", "评论"), TimelineEventType.Saved => ("💾", "最后保存"), TimelineEventType.Participated => ("👥", "参与"), _ => ("●", "版本观察") };
        var edge = edges.SingleOrDefault(value => Same(value.To, item.Version));
        return new TimelineItemViewModel(item.Version, item.Start ?? DateTimeOffset.MaxValue, date, precision, detail, icon, name, System.IO.Path.GetFileName(item.Version), profile.Kind.ToString().ToUpperInvariant(), item.Participant ?? "—", $"{item.Description} Strength: {item.EvidenceStrength}", TimelineChanges(item.Version, edge), edge?.Status.ToString() ?? "未关联", Badges(profile, false), ParticipantMatches(item.Version));
    }

    private string TimelineChanges(string path, LineageEdge? edge)
    {
        if (edge is null) return "未检测到可展示的已支持改动";
        var diff = FindDiff(edge.From, edge.To);
        return diff is null ? "Core 未提供此关系的语义 Diff" : string.Join(" · ", diff.Changes.GroupBy(change => change.Category).Select(group => $"{DisplayCategory(group.Key)} {group.Count()}处"));
    }

    private void ShowNode(string path)
    {
        if (session is null || !session.Profiles.TryGetValue(path, out var profile)) return;
        selectedNodePath = path; NoteText = annotations.Notes.TryGetValue(path, out var note) ? note : string.Empty;
        var lineage = SelectedFamily is null ? session.Lineage : LineageFor(SelectedFamily);
        var parent = lineage.Edges.SingleOrDefault(edge => Same(edge.To, path));
        SummaryTitle = System.IO.Path.GetFileName(path);
        SummaryText = parent is null ? "该文件没有可断言的直接来源；这是 GitIt 保留不确定性的结果。" : $"该文件很可能来自：{System.IO.Path.GetFileName(parent.From)}（{parent.Confidence:P0}，{parent.Status}）。";
        SupportingEvidence.Clear(); ConcernEvidence.Clear(); DiffRows.Clear(); TechnicalDetails.Clear(); CandidateSources.Clear(); selectedRelation = null;
        if (parent is not null)
        {
            foreach (var evidence in parent.Evidence.Where(item => !item.IsConflict && (item.Strength is EvidenceStrength.Strong or EvidenceStrength.Medium))) SupportingEvidence.Add($"✓ {evidence.Detail}");
            foreach (var evidence in parent.Evidence.Where(item => item.IsConflict || item.Strength == EvidenceStrength.Weak)) ConcernEvidence.Add($"⚠ {evidence.Detail}");
            foreach (var warning in parent.Warnings) ConcernEvidence.Add($"⚠ {warning}");
        }
        foreach (var warning in profile.UnsupportedFeatures) ConcernEvidence.Add($"⚠ {warning}");
        foreach (var candidate in lineage.Candidates.Where(item => Same(item.To, path)).OrderByDescending(item => item.Confidence).Take(12)) CandidateSources.Add(ToCandidate(candidate));
        TechnicalDetails.Add($"Open XML file hash: {profile.FileHash}");
        TechnicalDetails.Add($"Creator: {profile.Metadata.Creator ?? "(none)"}");
        TechnicalDetails.Add($"LastModifiedBy: {profile.Metadata.LastModifiedBy ?? "(none)"}");
        TechnicalDetails.Add($"Modified: {Date(path):O}");
        TechnicalDetails.Add($"RSID count: {profile.Docx?.Rsids.Count ?? 0}; revisions: {profile.Docx?.RevisionKinds.Values.Sum() ?? 0}");
    }

    private CandidateSourceItemViewModel ToCandidate(LineageCandidate candidate)
    {
        var support = candidate.Evidence.Where(item => !item.IsConflict).OrderByDescending(item => item.Score).Take(3).Select(item => item.Detail);
        var missing = candidate.Warnings.Concat(candidate.Evidence.Where(item => item.IsConflict).Select(item => item.Detail));
        return new CandidateSourceItemViewModel(candidate.From, candidate.To, candidate.Confidence.ToString("P0"), candidate.Status.ToString(), string.Join("；", support), string.Join("；", missing), candidate, annotations.ConfirmedRelations.Any(item => Same(item.Source, candidate.From) && Same(item.Target, candidate.To)), annotations.CandidateReviews.Any(item => Same(item.Source, candidate.From) && Same(item.Target, candidate.To)));
    }

    private void ShowRelation(GraphEdgeViewModel relation)
    {
        selectedRelation = relation; selectedNodePath = null; NoteText = string.Empty;
        if (relation.Edge is null && relation.RelatedCandidate is not null) { ShowRelated(relation.RelatedCandidate); selectedRelation = relation; return; }
        SummaryTitle = $"{System.IO.Path.GetFileName(relation.Source)} → {System.IO.Path.GetFileName(relation.Target)}";
        SummaryText = relation.Kind == GraphRelationKind.UserConfirmed && relation.Edge is null ? "这是用户确认的来源关系；Core 没有因此改变推断。" : relation.Kind == GraphRelationKind.UserConfirmed ? "Core 推断与用户确认同时存在。" : relation.Kind == GraphRelationKind.Conflicting ? "内容或结构支持此关系，但 Core 同时发现了冲突证据。" : $"{relation.AccessibleDescription}。";
        SupportingEvidence.Clear(); ConcernEvidence.Clear(); DiffRows.Clear(); TechnicalDetails.Clear(); CandidateSources.Clear(); ChangeTitle = "改动（Core semantic diff）";
        if (relation.Edge is not null)
        {
            foreach (var evidence in relation.Edge.Evidence.Where(item => !item.IsConflict && (item.Strength is EvidenceStrength.Strong or EvidenceStrength.Medium))) SupportingEvidence.Add($"✓ {evidence.Detail}");
            foreach (var evidence in relation.Edge.Evidence.Where(item => item.IsConflict || item.Strength == EvidenceStrength.Weak)) ConcernEvidence.Add($"⚠ {evidence.Detail}");
            foreach (var warning in relation.Edge.Warnings) ConcernEvidence.Add($"⚠ {warning}");
            foreach (var evidence in relation.Edge.Evidence) TechnicalDetails.Add($"{evidence.Type}: {evidence.Score:F2} ({evidence.Strength})");
        }
        if (relation.Confirmation is not null) SupportingEvidence.Add($"✓ 用户于 {relation.Confirmation.ConfirmedAt:yyyy-MM-dd} 确认该来源关系。");
        var diff = FindDiff(relation.Source, relation.Target);
        if (diff is null) ConcernEvidence.Add("⚠ Core 未提供此关系的语义 Diff。");
        else SetActiveDiff(diff);
    }

    private void ShowRelated(LineageCandidate candidate)
    {
        SummaryTitle = $"{System.IO.Path.GetFileName(candidate.From)} ··· ? ··· {System.IO.Path.GetFileName(candidate.To)}";
        SummaryText = "这些文件内容或结构相关，但 Core 没有足够来源证据来断言父子关系。你可以将自己的确认作为单独标注保存。";
        SupportingEvidence.Clear(); ConcernEvidence.Clear(); DiffRows.Clear(); TechnicalDetails.Clear(); CandidateSources.Clear(); ChangeTitle = "没有断言的父子关系";
        foreach (var evidence in candidate.Evidence.Where(item => !item.IsConflict)) SupportingEvidence.Add($"✓ {evidence.Detail}");
        foreach (var warning in candidate.Warnings) ConcernEvidence.Add($"⚠ {warning}");
        foreach (var evidence in candidate.Evidence.Where(item => item.IsConflict)) ConcernEvidence.Add($"⚠ {evidence.Detail}");
        CandidateSources.Add(ToCandidate(candidate));
    }

    public void OpenDiffWorkbench()
    {
        if (selectedRelation is null) { ShowMessage("请先选择一条血缘关系或候选来源。" ); return; }
        var diff = FindDiff(selectedRelation.Source, selectedRelation.Target);
        if (diff is null) { ShowMessage("这两个文件没有可用的 Core semantic Diff。" ); return; }
        SetActiveDiff(diff); HasDiffWorkbench = true; WorkspaceTabIndex = 3; StatusText = "已打开 Diff Workbench。";
    }

    private void OpenCandidateDiff(CandidateSourceItemViewModel candidate)
    {
        selectedRelation = new GraphEdgeViewModel(candidate.Candidate, 0, 0, 0, 0);
        OpenDiffWorkbench();
    }

    private void KeepCandidateUnconfirmed(CandidateSourceItemViewModel candidate)
    {
        if (annotations.CandidateReviews.Any(item => Same(item.Source, candidate.Source) && Same(item.Target, candidate.Target))) { ShowMessage("该候选已经记录审阅状态。" ); return; }
        annotations.CandidateReviews.Add(new UserCandidateReview { Source = candidate.Source, Target = candidate.Target, State = "kept-unconfirmed" });
        StatusText = "已记录为“保持未确认”；这不会创建血缘边。";
    }

    public void CompareSelectedFiles()
    {
        var paths = selectedManagedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length != 2 || session is null) { ShowMessage("请在未关联文件或搜索结果中选择恰好两个文件后比较。" ); return; }
        var source = paths.OrderBy(Date).ThenBy(path => path, StringComparer.OrdinalIgnoreCase).First();
        var target = paths.Single(path => !Same(path, source));
        SetActiveDiff(FindDiff(source, target) ?? adapter.Compare(session.Profiles[source], session.Profiles[target]));
        HasDiffWorkbench = true; WorkspaceTabIndex = 3; StatusText = "已打开所选两个版本的比较；这不要求它们已有血缘关系。";
    }

    public void CloseDiffWorkbench() { HasDiffWorkbench = false; WorkspaceTabIndex = 0; }

    private void SetActiveDiff(SemanticDiffResult diff)
    {
        activeDiff = diff; DiffTitle = $"{System.IO.Path.GetFileName(diff.SourcePath)}  →  {System.IO.Path.GetFileName(diff.TargetPath)}"; RefreshDiffRows();
    }

    private void SetDiffCategory(string category)
    {
        SelectedDiffCategory = category; RefreshDiffRows();
    }

    private void RefreshDiffRows()
    {
        DiffRows.Clear();
        if (activeDiff is null) return;
        foreach (var change in activeDiff.Changes.Where(change => SelectedDiffCategory == "全部" || DisplayCategory(change.Category) == SelectedDiffCategory)) DiffRows.Add(new DiffRowViewModel(DisplayCategory(change.Category), change.Location, change.Before ?? string.Empty, change.After ?? string.Empty, change.Detail, !string.IsNullOrWhiteSpace(change.Before) && string.IsNullOrWhiteSpace(change.After), string.IsNullOrWhiteSpace(change.Before) && !string.IsNullOrWhiteSpace(change.After)));
    }

    private SemanticDiffResult? FindDiff(string source, string target)
    {
        var global = session!.Analysis.Changes.SingleOrDefault(change => Same(change.SourcePath, source) && Same(change.TargetPath, target));
        if (global is not null) return global;
        foreach (var local in localGroupAnalyses.Values) if (local.Diffs.TryGetValue(DesktopAnalysisAdapter.PairKey(source, target), out var found)) return found;
        return session.Profiles.ContainsKey(source) && session.Profiles.ContainsKey(target) ? adapter.Compare(session.Profiles[source], session.Profiles[target]) : null;
    }

    private ManagedFileViewModel ToManagedFile(string path)
    {
        var profile = session!.Profiles[path];
        var relation = session.Analysis.Edges.Any(item => Same(item.From, path) || Same(item.To, path));
        return new ManagedFileViewModel(path, System.IO.Path.GetFileName(path), profile.Kind.ToString().ToUpperInvariant(), relation ? "已有 Core 关系" : "未关联文件", profile.ParticipantEvidence.Count == 0 ? "无参与者线索" : $"{profile.ParticipantEvidence.Count} 条参与线索");
    }

    private HashSet<string> VisiblePaths() => session is null ? [] : session.Profiles.Keys.Where(path => annotations.HiddenItems.All(item => !Same(item.Path, path))).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private bool ParticipantMatches(string path) => selectedParticipant?.Participant.Evidence.Any(item => Same(item.DocumentVersion, path)) == true;
    private DateTimeOffset Date(string path) => session!.Profiles[path].Metadata.Modified ?? session.Profiles[path].FileModified;
    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static string Badges(OfficeDocumentProfile profile, bool duplicate) => string.Join(" ", new[] { profile.UnsupportedFeatures.Count > 0 ? "🧩" : null, profile.UnsupportedFeatures.Any(text => text.Contains("No RSID", StringComparison.OrdinalIgnoreCase) || text.Contains("metadata", StringComparison.OrdinalIgnoreCase)) ? "⚠" : null, profile.ParticipantEvidence.Count > 0 ? "👥" : null, duplicate ? "🔗" : null }.Where(value => value is not null));
    private static string DisplayCategory(string category) => category.ToLowerInvariant() switch { "content" or "text" or "cell" => "内容", "format" => "格式", "structure" or "table" or "sheet" or "slide" => "结构", "formula" => "公式", _ => category };
    private static string RoleEvent(string type) => type switch { "creator" => "创建", "revision-author" => "修改", "comment-author" => "评论", "lastModifiedBy" => "保存", _ => "参与" };
    private static bool MatchesSearch(OfficeDocumentProfile profile, string query)
    {
        var comparison = StringComparison.OrdinalIgnoreCase;
        if (System.IO.Path.GetFileName(profile.Path).Contains(query, comparison)) return true;
        if (profile.ParticipantEvidence.Any(item => item.Value.Contains(query, comparison))) return true;
        if (profile.Docx?.Paragraphs.Any(item => item.Text.Contains(query, comparison)) == true) return true;
        if (profile.Xlsx?.Sheets.Any(sheet => sheet.Cells.Any(cell => (cell.Value?.Contains(query, comparison) ?? false) || (cell.Formula?.Contains(query, comparison) ?? false))) == true) return true;
        return profile.Pptx?.Slides.Any(slide => slide.Shapes.Any(shape => shape.Text.Contains(query, comparison))) == true;
    }
}
