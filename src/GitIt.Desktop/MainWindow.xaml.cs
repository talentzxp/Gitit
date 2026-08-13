using System.Windows;
using Forms = System.Windows.Forms;

namespace GitIt.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel = new();

    public MainWindow()
    {
        InitializeComponent(); DataContext = viewModel;
    }

    private async void ChooseFolder_Click(object sender, RoutedEventArgs eventArgs)
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = "选择包含 Office 文件的文件夹" };
        if (dialog.ShowDialog() == Forms.DialogResult.OK) await viewModel.PreviewFolderAsync(dialog.SelectedPath);
    }

    private async void OpenDemo_Click(object sender, RoutedEventArgs eventArgs)
    {
        var demo = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "demo"));
        if (!System.IO.Directory.Exists(demo))
        {
            viewModel.ShowMessage("演示数据尚未生成。请在仓库根目录运行：dotnet run --project src/GitIt.Benchmarks -- demo");
            return;
        }
        await viewModel.PreviewFolderAsync(demo);
    }

    private async void DropArea_Drop(object sender, System.Windows.DragEventArgs eventArgs)
    {
        if (eventArgs.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths)
        {
            viewModel.ShowMessage("请拖入一个文件夹，而不是单个文件或多个项目。");
            return;
        }
        if (paths.Length != 1 || !System.IO.Directory.Exists(paths[0]))
        {
            viewModel.ShowMessage("请拖入一个文件夹，而不是单个文件或多个项目。");
            return;
        }
        await viewModel.PreviewFolderAsync(paths[0]);
    }

    private void DropArea_DragOver(object sender, System.Windows.DragEventArgs eventArgs)
    {
        eventArgs.Effects = eventArgs.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        eventArgs.Handled = true;
    }

    private async void StartAnalysis_Click(object sender, RoutedEventArgs eventArgs) => await viewModel.AnalyzePendingFolderAsync();
    private void CancelScan_Click(object sender, RoutedEventArgs eventArgs) => viewModel.CancelPreview();

    private void CreateGroup_Click(object sender, RoutedEventArgs eventArgs)
    {
        var name = TextPrompt.Ask(this, "创建文档组", "为所选文件输入文档组名称：");
        if (name is not null) viewModel.CreateUserGroup(name);
    }

    private void AddToGroup_Click(object sender, RoutedEventArgs eventArgs) => viewModel.AddSelectedFilesToGroup();

    private void RenameGroup_Click(object sender, RoutedEventArgs eventArgs)
    {
        var current = viewModel.SelectedFamily?.Name;
        if (current is null) { viewModel.ShowMessage("请先选择一个文档组。"); return; }
        var name = TextPrompt.Ask(this, "重命名文档组", "输入新的文档组名称：", current);
        if (name is not null) viewModel.RenameSelectedFamily(name);
    }

    private void HideGroup_Click(object sender, RoutedEventArgs eventArgs) => viewModel.HideSelectedFamily();
    private void RestoreHidden_Click(object sender, RoutedEventArgs eventArgs) => viewModel.RestoreHiddenFiles();

    private void SaveProject_Click(object sender, RoutedEventArgs eventArgs)
    {
        using var dialog = new Forms.SaveFileDialog { Filter = "GitIt project (*.gitit)|*.gitit", DefaultExt = "gitit", FileName = "document-history.gitit" };
        if (dialog.ShowDialog() == Forms.DialogResult.OK) viewModel.SaveProject(dialog.FileName);
    }

    private void OpenProject_Click(object sender, RoutedEventArgs eventArgs)
    {
        using var dialog = new Forms.OpenFileDialog { Filter = "GitIt project (*.gitit)|*.gitit", Multiselect = false };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        try { viewModel.OpenProject(dialog.FileName); }
        catch (Exception exception) { viewModel.ShowMessage($"无法打开 GitIt 项目：{exception.Message}"); }
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs eventArgs) => viewModel.Search(((System.Windows.Controls.TextBox)sender).Text);

    private void ManagedFiles_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs eventArgs)
    {
        if (sender is System.Windows.Controls.ListBox list) viewModel.SetSelectedFiles(list.SelectedItems.OfType<ManagedFileViewModel>());
    }
}
