using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Kimulator.App.Dialogs;

/// <summary>Asks for a hex start address and, optionally, an inclusive end address.</summary>
public sealed class AddressRangeDialog : Window
{
    private readonly TextBox _start;
    private readonly TextBox? _end;
    private readonly TextBlock _error;

    public AddressRangeDialog(string title, string prompt, ushort start, ushort? end)
    {
        Title = title;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _start = HexBox(start);
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 8,
            RowSpacing = 8,
        };
        AddRow(grid, 0, "Start ($)", _start);
        if (end is not null)
        {
            _end = HexBox(end.Value);
            AddRow(grid, 1, "End ($, inclusive)", _end);
        }

        _error = new TextBlock { Foreground = Avalonia.Media.Brushes.OrangeRed, IsVisible = false, TextWrapping = Avalonia.Media.TextWrapping.Wrap };

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 72, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 72, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = prompt, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                grid,
                _error,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { ok, cancel } },
            },
        };

        Opened += (_, _) =>
        {
            _start.Focus();
            _start.SelectAll();
        };
    }

    private void Accept()
    {
        if (!TryParse(_start.Text, out ushort start))
        {
            ShowError("Enter the start address as 1-4 hex digits, e.g. 0200.");
            return;
        }

        ushort? end = null;
        if (_end is not null)
        {
            if (!TryParse(_end.Text, out ushort e) || e < start)
            {
                ShowError("Enter an end address (hex) at or after the start.");
                return;
            }

            end = e;
        }

        Close(new AddressRange(start, end));
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.IsVisible = true;
    }

    private static bool TryParse(string? text, out ushort value) =>
        ushort.TryParse(text?.Trim().TrimStart('$'), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);

    private static TextBox HexBox(ushort value) => new()
    {
        Text = value.ToString("X4"),
        MaxLength = 5,
        FontFamily = new Avalonia.Media.FontFamily("Consolas, Menlo, monospace"),
    };

    private static void AddRow(Grid grid, int row, string label, Control box)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(text, row);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        grid.Children.Add(text);
        grid.Children.Add(box);
    }
}

public sealed record AddressRange(ushort Start, ushort? End);
