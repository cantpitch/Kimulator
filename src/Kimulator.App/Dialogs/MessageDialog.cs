using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Kimulator.App;

/// <summary>A minimal modal message box.</summary>
public static class MessageDialog
{
    public static Task ShowAsync(Window owner, string title, string message)
    {
        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, MinWidth = 72, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 16,
                Children =
                {
                    new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    ok,
                },
            },
        };
        ok.Click += (_, _) => dialog.Close();
        return dialog.ShowDialog(owner);
    }
}
