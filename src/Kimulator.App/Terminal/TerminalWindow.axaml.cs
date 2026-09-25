using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Kimulator.Core.Formats;

namespace Kimulator.App.Terminal;

/// <summary>
/// A glass teletype for the KIM-1's TTY port. What the KIM prints comes from the session transcript;
/// what you type goes out as serial characters on the KIM's input line.
/// </summary>
public partial class TerminalWindow : Window
{
    private static readonly int[] BaudRates = [110, 300, 600, 1200, 2400, 4800];

    private readonly EmulatorSession _session;
    private readonly AppSettings _settings;
    private readonly Func<bool, Task> _setTtyMode;
    private readonly DispatcherTimer _statusTimer;
    private long _shownVersion = -1;
    private bool _initializing = true;

    public TerminalWindow() : this(null!, new AppSettings(), _ => Task.CompletedTask) { } // designer

    public TerminalWindow(EmulatorSession session, AppSettings settings, Func<bool, Task> setTtyMode)
    {
        InitializeComponent();
        _session = session;
        _settings = settings;
        _setTtyMode = setTtyMode;

        BaudBox.ItemsSource = BaudRates;
        BaudBox.SelectedItem = BaudRates.Contains(settings.BaudRate) ? settings.BaudRate : 1200;
        UppercaseBox.IsChecked = settings.TerminalUppercase;
        PaperBox.IsChecked = settings.TerminalPaper;
        ApplyLook(settings.TerminalPaper);
        _initializing = false;

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnPreviewTextInput, RoutingStrategies.Tunnel);

        if (session is not null)
        {
            session.TranscriptChanged += Refresh;
            Refresh();
        }

        _statusTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, async (_, _) => await UpdateStatusAsync());
        _statusTimer.Start();
        Opened += (_, _) => Screen.Focus();
        Activated += (_, _) =>
        {
            _initializing = true;
            BaudBox.SelectedItem = _settings.BaudRate; // the main window's menu may have changed it
            _initializing = false;
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _statusTimer.Stop();
        if (_session is not null) _session.TranscriptChanged -= Refresh;
        base.OnClosed(e);
    }

    /// <summary>Called when TTY mode changes elsewhere (menu, save state).</summary>
    public void OnTtyModeChanged() => _ = UpdateStatusAsync();

    private void Refresh()
    {
        var transcript = _session.Transcript;
        if (transcript.Version == _shownVersion) return;
        _shownVersion = transcript.Version;
        string text = transcript.ToString();
        Screen.Text = text;
        Screen.CaretIndex = text.Length; // keep the newest output in view
    }

    private async Task UpdateStatusAsync()
    {
        if (_session is null) return;
        int pending = await _session.PendingTtyInputAsync();
        bool tty = _session.TtyMode;
        EnableTtyButton.IsVisible = !tty;
        string mode = tty
            ? "TTY mode — type here; the KIM echoes what you type."
            : "The KIM is in keyboard mode; switch to TTY mode to use the terminal.";
        StatusText.Text = pending > 0 ? $"{mode}   Sending… {pending} characters queued (Esc to cancel)" : mode;
    }

    // ---------------------------------------------------------------- typing

    private void OnPreviewTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;
        Send(e.Text);
        e.Handled = true;
    }

    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        byte? code = (e.Key, e.KeyModifiers) switch
        {
            (Key.Enter, KeyModifiers.None) => 0x0D,               // CR: next cell
            (Key.Enter, KeyModifiers.Shift) => 0x0A,              // LF: previous cell
            (Key.J, KeyModifiers.Control) => 0x0A,
            (Key.Back or Key.Delete, KeyModifiers.None) => 0x7F,  // RUBOUT
            _ => null,
        };

        if (code is not null)
        {
            _session.SendToTty([code.Value]);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            _session.ClearTtyInput();
            e.Handled = true;
        }
        else if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control) && Clipboard is { } clipboard)
        {
            e.Handled = true;
            if (await clipboard.TryGetTextAsync() is { } text) Send(text);
        }
    }

    /// <summary>Sends text as typed: newlines become CR (the monitor's "next cell"), optional upper-casing.</summary>
    private void Send(string text)
    {
        text = text.Replace("\r\n", "\r").Replace('\n', '\r');
        if (UppercaseBox.IsChecked == true) text = text.ToUpperInvariant();
        var bytes = text.Where(c => c < 0x80 && (c >= ' ' || c == '\r')).Select(c => (byte)c).ToArray();
        if (bytes.Length > 0) _session.SendToTty(bytes);
    }

    // ---------------------------------------------------------------- toolbar

    private void OnBaudChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializing || BaudBox.SelectedItem is not int baud || baud == _settings.BaudRate) return;
        _settings.BaudRate = baud;
        _session.SetBaudRate(baud);
    }

    private void OnPaperChanged(object? sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _settings.TerminalPaper = PaperBox.IsChecked == true;
        ApplyLook(_settings.TerminalPaper);
    }

    // ---------------------------------------------------------------- looks

    private static readonly FontFamily ScreenFont = new("Consolas, Menlo, DejaVu Sans Mono, monospace");
    private static readonly FontFamily TypewriterFont = new("Courier Prime, Courier New, Courier, Nimbus Mono PS, FreeMono, Liberation Mono, monospace");

    /// <summary>Glowing amber CRT, or cream teletype paper with typewriter ink.</summary>
    private void ApplyLook(bool paper)
    {
        IBrush background = paper
            ? new LinearGradientBrush
            {
                StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
                EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0xF7, 0xF2, 0xE4), 0),
                    new GradientStop(Color.FromRgb(0xEE, 0xE6, 0xD0), 1),
                },
            }
            : new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0C));
        IBrush ink = new SolidColorBrush(paper ? Color.FromRgb(0x2A, 0x25, 0x20) : Color.FromRgb(0xFF, 0xC2, 0x4A));

        Screen.Background = background;
        Screen.Foreground = ink;
        Screen.FontFamily = paper ? TypewriterFont : ScreenFont;
        Screen.FontSize = paper ? 16 : 15;
        Screen.FontWeight = paper ? FontWeight.SemiBold : FontWeight.Normal;
        Screen.CaretBrush = ink;
        Screen.SelectionBrush = new SolidColorBrush(paper ? Color.FromArgb(0x55, 0x80, 0x60, 0x30) : Color.FromArgb(0x66, 0xFF, 0xC2, 0x4A));
        Screen.Resources["TextControlBackgroundFocused"] = background;
        Screen.Resources["TextControlBackgroundPointerOver"] = background;
        Screen.Resources["TextControlForegroundFocused"] = ink;
        Screen.Resources["TextControlForegroundPointerOver"] = ink;
        Screen.Padding = paper ? new Avalonia.Thickness(28, 16) : new Avalonia.Thickness(10);
    }

    private void OnUppercaseChanged(object? sender, RoutedEventArgs e)
    {
        if (!_initializing) _settings.TerminalUppercase = UppercaseBox.IsChecked == true;
    }

    private async void OnLoadPaperTape(object? sender, RoutedEventArgs e)
    {
        var file = await FileDialogs.OpenAsync(this, _settings, "Load paper tape through the monitor", FileDialogs.PaperTapeTypes);
        if (file is null) return;
        string text = Encoding.ASCII.GetString(file.Value.Content);
        try
        {
            PaperTape.Parse(text); // validate before tying up the line for minutes
        }
        catch (FormatException ex)
        {
            await MessageDialog.ShowAsync(this, "Not a valid paper tape", ex.Message);
            return;
        }

        if (!_session.TtyMode) await _setTtyMode(true);
        _session.SendToTty(Encoding.ASCII.GetBytes("L" + text));
        Screen.Focus();
    }

    private async void OnSendText(object? sender, RoutedEventArgs e)
    {
        var file = await FileDialogs.OpenAsync(this, _settings, "Send text file", FileDialogs.TextTypes);
        if (file is null) return;
        Send(Encoding.ASCII.GetString(file.Value.Content));
        Screen.Focus();
    }

    private async void OnSaveTranscript(object? sender, RoutedEventArgs e)
    {
        string text = _session.Transcript.ToString().Replace("\n", Environment.NewLine);
        await FileDialogs.SaveAsync(this, _settings, "Save transcript", "kim-session.txt", FileDialogs.TextTypes, Encoding.ASCII.GetBytes(text));
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        _session.Transcript.Clear();
        Refresh();
        Screen.Focus();
    }

    private async void OnEnableTty(object? sender, RoutedEventArgs e)
    {
        await _setTtyMode(true);
        await UpdateStatusAsync();
        Screen.Focus();
    }
}
