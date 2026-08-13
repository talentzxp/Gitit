using GitIt.Desktop;
using GitIt.GroundTruth;
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
}
