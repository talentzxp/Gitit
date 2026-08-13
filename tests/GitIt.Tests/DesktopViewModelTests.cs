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
}
