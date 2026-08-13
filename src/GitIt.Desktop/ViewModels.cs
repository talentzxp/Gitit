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

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> action;
    public RelayCommand(Action<object?> action) => this.action = action;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action(parameter);
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class FamilyItemViewModel(string id, OfficeFileKind kind, int versions, int duplicates, int unlinked, int related) : ObservableObject
{
    public string Id { get; } = id;
    public string Name => $"文档家族 {Id.Replace("family-", string.Empty, StringComparison.OrdinalIgnoreCase)}";
    public string Kind => kind.ToString().ToUpperInvariant();
    public int Versions { get; } = versions;
    public int Duplicates { get; } = duplicates;
    public int Unlinked { get; } = unlinked;
    public int Related { get; } = related;
    public string Summary => $"{Versions} 个版本 · {Duplicates} 个重复 · {Related} 个相关未证实 · {Unlinked} 个未关联";
}

public sealed class GraphNodeViewModel(string path, string label, string status, double x, double y, bool duplicate) : ObservableObject
{
    public string Path { get; } = path;
    public string Label { get; } = label;
    public string Status { get; } = status;
    public double X { get; } = x;
    public double Y { get; } = y;
    public bool Duplicate { get; } = duplicate;
}

public sealed class GraphEdgeViewModel(LineageEdge edge, double x1, double y1, double x2, double y2) : ObservableObject
{
    public LineageEdge Edge { get; } = edge;
    public double X1 { get; } = x1;
    public double Y1 { get; } = y1;
    public double X2 { get; } = x2;
    public double Y2 { get; } = y2;
    public bool IsStrong => Edge.Status == LineageStatus.Probable;
    public string Label => $"{System.IO.Path.GetFileName(Edge.From)} → {System.IO.Path.GetFileName(Edge.To)}  {Edge.Confidence:P0}";
}

public sealed class ParticipantItemViewModel(ParticipantIdentity participant) : ObservableObject
{
    public ParticipantIdentity Participant { get; } = participant;
    public string Name => Participant.DisplayName;
    public string Summary => $"{Participant.Evidence.Count} 条参与线索";
}

public sealed class MainViewModel : ObservableObject
{
    private readonly DesktopAnalysisAdapter adapter;
    private DesktopAnalysisSession? session;
    private FamilyItemViewModel? selectedFamily;
    private ParticipantItemViewModel? selectedParticipant;
    private string statusText = "把包含 Office 文件的文件夹拖到这里，或点击“选择文件夹”。";
    private string detailTitle = "尚未选择版本";
    private string detailSubtitle = "GitIt 只展示 Core 已给出的结论，不会在界面中重新推断来源。";
    private bool isBusy;
    private bool hasAnalysis;

    public MainViewModel(DesktopAnalysisAdapter? adapter = null)
    {
        this.adapter = adapter ?? new DesktopAnalysisAdapter();
        SelectNodeCommand = new RelayCommand(value => { if (value is GraphNodeViewModel node) ShowNode(node.Path); });
        SelectEdgeCommand = new RelayCommand(value => { if (value is GraphEdgeViewModel edge) ShowEdge(edge.Edge); });
    }

    public ObservableCollection<FamilyItemViewModel> Families { get; } = [];
    public ObservableCollection<GraphNodeViewModel> GraphNodes { get; } = [];
    public ObservableCollection<GraphEdgeViewModel> GraphEdges { get; } = [];
    public ObservableCollection<GraphEdgeViewModel> EdgeList { get; } = [];
    public ObservableCollection<ParticipantItemViewModel> Participants { get; } = [];
    public ObservableCollection<string> DetailLines { get; } = [];
    public ObservableCollection<string> TestLines { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    public ICommand SelectNodeCommand { get; }
    public ICommand SelectEdgeCommand { get; }
    public string StatusText { get => statusText; private set => Set(ref statusText, value); }
    public string DetailTitle { get => detailTitle; private set => Set(ref detailTitle, value); }
    public string DetailSubtitle { get => detailSubtitle; private set => Set(ref detailSubtitle, value); }
    public bool IsBusy { get => isBusy; private set => Set(ref isBusy, value); }
    public bool HasAnalysis { get => hasAnalysis; private set => Set(ref hasAnalysis, value); }
    public string Summary => session is null ? "未分析文件夹" : $"发现 {session.Analysis.Versions.Count} 个 Office 文件 · {session.Analysis.DocumentFamilies.Count} 个文档家族 · {session.Lineage.Duplicates.Count} 个重复组";

    public FamilyItemViewModel? SelectedFamily
    {
        get => selectedFamily;
        set { if (Set(ref selectedFamily, value) && value is not null) BuildGraph(value); }
    }

    public ParticipantItemViewModel? SelectedParticipant
    {
        get => selectedParticipant;
        set
        {
            if (Set(ref selectedParticipant, value) && value is not null)
            {
                DetailTitle = value.Name; DetailSubtitle = value.Summary; DetailLines.Clear(); TestLines.Clear();
                foreach (var evidence in value.Participant.Evidence) DetailLines.Add($"{System.IO.Path.GetFileName(evidence.DocumentVersion)} · {evidence.EvidenceType} · {evidence.Strength}: {evidence.Detail}");
            }
        }
    }

    public async Task AnalyzeFolderAsync(string folder)
    {
        IsBusy = true; StatusText = "正在扫描与分析…";
        try
        {
            var result = await Task.Run(() => adapter.Analyze(folder));
            Load(result); StatusText = $"分析完成：{Summary}";
        }
        catch (Exception exception)
        {
            StatusText = $"部分文件无法分析：{exception.Message}";
        }
        finally { IsBusy = false; }
    }

    public void ShowMessage(string message) => StatusText = message;

    public void Load(DesktopAnalysisSession value)
    {
        session = value; HasAnalysis = true; Families.Clear(); Participants.Clear(); Warnings.Clear();
        foreach (var warning in value.Analysis.Warnings.Concat(value.Analysis.UnsupportedFeatures).Distinct(StringComparer.OrdinalIgnoreCase)) Warnings.Add(warning);
        foreach (var family in value.Analysis.DocumentFamilies)
        {
            var paths = family.VersionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var duplicates = value.Analysis.Versions.Count(version => paths.Contains(version.Path) && version.DuplicateOf is not null);
            var related = value.Lineage.Candidates.Count(candidate => candidate.Status == LineageStatus.RelatedButUnproven && (paths.Contains(candidate.From) || paths.Contains(candidate.To)));
            var linked = value.Analysis.Edges.SelectMany(edge => new[] { edge.From, edge.To }).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Families.Add(new FamilyItemViewModel(family.Id, family.Kind, paths.Count, duplicates, paths.Count(path => !linked.Contains(path)), related));
        }
        foreach (var participant in value.Analysis.Participants) Participants.Add(new ParticipantItemViewModel(participant));
        SelectedFamily = Families.FirstOrDefault(); Raise(nameof(Summary));
    }

    private void BuildGraph(FamilyItemViewModel family)
    {
        if (session is null) return;
        GraphNodes.Clear(); GraphEdges.Clear(); EdgeList.Clear();
        var familyPaths = session.Analysis.DocumentFamilies.Single(item => item.Id == family.Id).VersionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var edges = session.Analysis.Edges.Where(edge => familyPaths.Contains(edge.From) && familyPaths.Contains(edge.To)).ToArray();
        var parent = edges.GroupBy(edge => edge.To, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.OrderByDescending(edge => edge.Confidence).First().From, StringComparer.OrdinalIgnoreCase);
        var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int Level(string path) => levels.TryGetValue(path, out var known) ? known : levels[path] = parent.TryGetValue(path, out var source) ? Level(source) + 1 : 0;
        foreach (var path in familyPaths) Level(path);
        var nodes = new Dictionary<string, GraphNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var level in levels.GroupBy(item => item.Value).OrderBy(group => group.Key))
        foreach (var item in level.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select((item, index) => new { item, index }))
        {
            var version = session.Analysis.Versions.Single(value => value.Path == item.item.Key);
            var duplicate = version.DuplicateOf is not null;
            var relation = session.Lineage.Candidates.FirstOrDefault(candidate => candidate.To == item.item.Key && candidate.Status == LineageStatus.RelatedButUnproven);
            var status = duplicate ? "完全相同的副本" : relation is not null ? "相关但未证实" : parent.ContainsKey(item.item.Key) ? edges.Single(edge => edge.To == item.item.Key).Status.ToString() : "未关联";
            var node = new GraphNodeViewModel(item.item.Key, System.IO.Path.GetFileName(item.item.Key), status, 32 + level.Key * 210, 42 + item.index * 100, duplicate);
            nodes[node.Path] = node; GraphNodes.Add(node);
        }
        foreach (var edge in edges)
        {
            var source = nodes[edge.From]; var target = nodes[edge.To]; var item = new GraphEdgeViewModel(edge, source.X + 160, source.Y + 31, target.X, target.Y + 31);
            GraphEdges.Add(item); EdgeList.Add(item);
        }
        DetailTitle = family.Name; DetailSubtitle = family.Summary; DetailLines.Clear(); TestLines.Clear();
    }

    private void ShowNode(string path)
    {
        if (session is null || !session.Profiles.TryGetValue(path, out var profile)) return;
        var parent = session.Analysis.Edges.SingleOrDefault(edge => edge.To == path);
        DetailTitle = System.IO.Path.GetFileName(path);
        DetailSubtitle = $"{profile.Kind.ToString().ToUpperInvariant()} · 可能来源：{(parent is null ? "未证实" : System.IO.Path.GetFileName(parent.From))} · {(parent is null ? "Uncertain" : parent.Confidence.ToString("P0"))}";
        DetailLines.Clear(); TestLines.Clear();
        if (parent is null) DetailLines.Add("没有可断言的直接来源；这是一种允许的结论。");
        else
        {
            DetailLines.Add($"状态：{parent.Status}");
            foreach (var evidence in parent.Evidence) DetailLines.Add($"[{evidence.Strength}] {evidence.Type}: {evidence.Detail}");
            foreach (var warning in parent.Warnings) DetailLines.Add($"警告：{warning}");
        }
        TestLines.Add($"File hash: {profile.FileHash}");
        TestLines.Add($"Creator: {profile.Metadata.Creator ?? "(none)"}");
        TestLines.Add($"LastModifiedBy: {profile.Metadata.LastModifiedBy ?? "(none)"}");
        TestLines.Add($"RSID count: {profile.Docx?.Rsids.Count ?? 0}");
        TestLines.Add($"Revision count: {profile.Docx?.RevisionKinds.Values.Sum() ?? 0}");
        foreach (var warning in profile.UnsupportedFeatures) TestLines.Add($"Warning: {warning}");
    }

    private void ShowEdge(LineageEdge edge)
    {
        if (session is null) return;
        DetailTitle = $"{System.IO.Path.GetFileName(edge.From)} → {System.IO.Path.GetFileName(edge.To)}";
        DetailSubtitle = $"{edge.Status} · {edge.Confidence:P0}";
        DetailLines.Clear(); TestLines.Clear();
        var diff = session.Analysis.Changes.SingleOrDefault(change => change.SourcePath == edge.From && change.TargetPath == edge.To);
        if (diff is null) DetailLines.Add("Core 未提供此边的语义 Diff。");
        else
        {
            foreach (var group in diff.Changes.GroupBy(change => change.Category, StringComparer.OrdinalIgnoreCase)) DetailLines.Add($"{group.Key}: {group.Count()} changes");
            foreach (var change in diff.Changes.Take(20)) DetailLines.Add($"[{change.Category}] {change.Location}: {change.Detail}{FormatValues(change.Before, change.After)}");
        }
        foreach (var evidence in edge.Evidence) TestLines.Add($"{evidence.Type}: {evidence.Score:F2} ({evidence.Strength})");
    }

    private static string FormatValues(string? before, string? after) => before is null && after is null ? string.Empty : $" ({before ?? "—"} → {after ?? "—"})";
}
