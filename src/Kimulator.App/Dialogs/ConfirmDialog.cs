using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Kimulator.App;

/// <summary>A minimal modal yes/no question.</summary>
public static class ConfirmDialog
{
    public static async Task<bool> AskAsync(Window owner, string title, string message, string confirmText)
    {
        var confirm = new Button { Content = confirmText, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", IsDefault = true, IsCancel = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { confirm, cancel } },
                },
            },
        };
        confirm.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        return await dialog.ShowDialog<bool?>(owner) == true;
    }
}
