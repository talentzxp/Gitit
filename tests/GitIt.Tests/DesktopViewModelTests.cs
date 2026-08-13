using GitIt.Desktop;
using GitIt.GroundTruth;
using GitIt.UserAnnotations;
using Xunit;

namespace GitIt.Tests;

public sealed class DesktopViewModelTests
{
    [Fact]
    public void Desktop_adapter_consumes_core_result_without_reimplementing_analysis()
    {
        var dataset = new GroundTruthGenerator().Create();

        var session = new DesktopAnalysisAdapter().Analyze(dataset.Root);

        Assert.NotEmpty(session.Analysis.Versions);
        Assert.NotEmpty(session.Analysis.DocumentFamilies);
        Assert.NotEmpty(session.Lineage.Edges);
        Assert.NotEmpty(session.Profiles);
    }

    [Fact]
    public void View_model_builds_family_graph_and_participant_views_from_core_output()
    {
        var dataset = new GroundTruthGenerator().Create();
        var viewModel = new MainViewModel();

        viewModel.Load(new DesktopAnalysisAdapter().Analyze(dataset.Root));

        Assert.True(viewModel.HasAnalysis);
        Assert.NotEmpty(viewModel.Families);
        Assert.NotEmpty(viewModel.GraphNodes);
        Assert.NotEmpty(viewModel.EdgeList);
        Assert.NotEmpty(viewModel.Participants);
    }

    [Fact]
    public void Human_centered_views_keep_family_names_timeline_and_core_diff_separate()
    {
        var dataset = new GroundTruthGenerator().Create();
        var viewModel = new MainViewModel();
        viewModel.Load(new DesktopAnalysisAdapter().Analyze(dataset.Root));

        Assert.All(viewModel.Families, family => Assert.False(family.Name.StartsWith("文档家族", StringComparison.Ordinal)));
        Assert.NotEmpty(viewModel.Timeline);
        Assert.All(viewModel.Timeline, item => Assert.NotEmpty(item.Date));
        var relation = Assert.IsType<GraphEdgeViewModel>(viewModel.EdgeList.First());
        viewModel.SelectEdgeCommand.Execute(relation);
        Assert.NotEmpty(viewModel.SupportingEvidence);
        Assert.NotEmpty(viewModel.DiffRows);
    }

    [Fact]
    public void User_annotations_create_rename_hide_confirm_and_round_trip_without_changing_core()
    {
        var dataset = new GroundTruthGenerator().Create();
        var session = new DesktopAnalysisAdapter().Analyze(dataset.Root);
        var viewModel = new MainViewModel();
        viewModel.Load(session);
        var originalCoreEdgeCount = session.Analysis.Edges.Count;
        var selectedPaths = session.Profiles.Keys.Take(2).ToArray();
        viewModel.SetSelectedFiles(selectedPaths.Select(path => new ManagedFileViewModel(path, Path.GetFileName(path), "DOCX", "未关联文件", string.Empty)));

        Assert.True(viewModel.CreateUserGroup("人工确认的报告组"));
        var group = Assert.Single(viewModel.Families, item => item.IsUserManaged);
        Assert.True(viewModel.RenameSelectedFamily("人工命名报告"));
        Assert.Contains(viewModel.Families, item => item.Name == "人工命名报告");

        var candidate = session.Lineage.Candidates.First();
        viewModel.ConfirmCandidate(new CandidateSourceItemViewModel(candidate.From, candidate.To, candidate.Confidence.ToString("P0"), candidate.Status.ToString(), string.Empty, string.Empty, candidate, false));
        Assert.Contains(viewModel.EdgeList, item => item.Kind == GraphRelationKind.UserConfirmed);
        Assert.Equal(originalCoreEdgeCount, session.Analysis.Edges.Count);

        viewModel.SelectedFamily = viewModel.Families.Single(item => item.IsUserManaged);
        viewModel.HideSelectedFamily();
        Assert.DoesNotContain(viewModel.Families, item => item.IsUserManaged);
        viewModel.RestoreHiddenFiles();
        Assert.Contains(viewModel.Families, item => item.IsUserManaged);

        var projectPath = Path.Combine(dataset.Root, "history.gitit");
        viewModel.SaveProject(projectPath);
        var restored = new MainViewModel();
        restored.OpenProject(projectPath);
        Assert.True(restored.HasAnalysis);
        Assert.Contains(restored.Families, item => item.Name == "人工命名报告");
        Assert.Equal(1, restored.ConfirmedRelationCount);
    }

    [Fact]
    public void Annotation_store_persists_groups_relations_hidden_items_and_notes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gitit-{Guid.NewGuid():N}.gitit");
        var project = new UserAnnotationProject
        {
            AnalysisRoot = "C:\\authorized-corpus",
            DocumentGroups = [new UserDocumentGroup { GroupId = "group-001", Name = "贵州土壤三普报告", Files = ["a.docx", "b.docx"] }],
            ConfirmedRelations = [new UserConfirmedRelation { Source = "a.docx", Target = "b.docx" }],
            HiddenItems = [new UserHiddenItem { Path = "temp.docx" }],
            Notes = new Dictionary<string, string> { ["b.docx"] = "人工确认来源。" }
        };

        try
        {
            var store = new UserAnnotationProjectStore();
            store.Save(path, project);
            var loaded = store.Load(path);
            Assert.Equal("贵州土壤三普报告", Assert.Single(loaded.DocumentGroups).Name);
            Assert.Single(loaded.ConfirmedRelations);
            Assert.Single(loaded.HiddenItems);
            Assert.Equal("人工确认来源。", loaded.Notes["b.docx"]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
