using System.Windows;

namespace GitIt.Desktop;

internal static class TextPrompt
{
    public static string? Ask(Window owner, string title, string instruction, string initialValue = "")
    {
        var input = new System.Windows.Controls.TextBox { Text = initialValue, MinWidth = 330, Margin = new Thickness(0, 8, 0, 12) };
        var accept = new System.Windows.Controls.Button { Content = "确定", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new System.Windows.Controls.Button { Content = "取消", IsCancel = true, MinWidth = 80 };
        var dialog = new Window
        {
            Owner = owner, Title = title, Width = 430, Height = 175, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, Content = new System.Windows.Controls.StackPanel { Margin = new Thickness(18) }
        };
        ((System.Windows.Controls.StackPanel)dialog.Content).Children.Add(new System.Windows.Controls.TextBlock { Text = instruction, TextWrapping = TextWrapping.Wrap });
        ((System.Windows.Controls.StackPanel)dialog.Content).Children.Add(input);
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        buttons.Children.Add(accept); buttons.Children.Add(cancel); ((System.Windows.Controls.StackPanel)dialog.Content).Children.Add(buttons);
        accept.Click += (_, _) => dialog.DialogResult = true;
        cancel.Click += (_, _) => dialog.DialogResult = false;
        input.SelectAll(); input.Focus();
        return dialog.ShowDialog() == true ? input.Text : null;
    }
}
