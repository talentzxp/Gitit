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
        if (dialog.ShowDialog() == Forms.DialogResult.OK) await viewModel.AnalyzeFolderAsync(dialog.SelectedPath);
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
        await viewModel.AnalyzeFolderAsync(paths[0]);
    }

    private void DropArea_DragOver(object sender, System.Windows.DragEventArgs eventArgs)
    {
        eventArgs.Effects = eventArgs.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        eventArgs.Handled = true;
    }
}
