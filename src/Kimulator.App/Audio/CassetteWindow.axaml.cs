using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kimulator.Core.Formats;
using Kimulator.Kim1;

namespace Kimulator.App.Audio;

/// <summary>A cassette deck for the KIM-1's tape interface, with WAV import/export.</summary>
public partial class CassetteWindow : Window
{
    private readonly EmulatorSession _session;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer;

    public CassetteWindow() : this(null!, new AppSettings()) { } // designer

    public CassetteWindow(EmulatorSession session, AppSettings settings)
    {
        InitializeComponent();
        _session = session;
        _settings = settings;
        AutoStopBox.IsChecked = settings.CassetteAutoStop;

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, async (_, _) => await RefreshAsync());
        if (session is not null) _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }

    private async Task RefreshAsync()
    {
        var status = await _session.CassetteStatusAsync();
        StateText.Text = status.State switch
        {
            TapeState.Playing => "▶  PLAYING",
            TapeState.Recording => "●  RECORDING",
            _ => status.HasTape ? "■  STOPPED" : "■  NO TAPE",
        };
        StateText.Foreground = status.State == TapeState.Recording ? Avalonia.Media.Brushes.OrangeRed : Avalonia.Media.Brushes.Gainsboro;
        TimeText.Text = $"{Format(status.PositionSeconds)} / {Format(status.LengthSeconds)}";
        TapeProgress.Value = status.LengthSeconds > 0 ? Math.Min(1, status.PositionSeconds / status.LengthSeconds) : 0;
    }

    private static string Format(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"m\:ss\.f", CultureInfo.InvariantCulture);

    private async void OnRewind(object? sender, RoutedEventArgs e) => await _session.CassetteAsync((c, _) => c.Rewind());

    private async void OnPlay(object? sender, RoutedEventArgs e) => await _session.CassetteAsync((c, _) => c.Play());

    private async void OnRecord(object? sender, RoutedEventArgs e) => await _session.CassetteAsync((c, cycle) => c.Record(cycle));

    private async void OnStop(object? sender, RoutedEventArgs e) => await _session.CassetteAsync((c, _) => c.Stop());

    private async void OnAutoStopChanged(object? sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        bool on = AutoStopBox.IsChecked == true;
        _settings.CassetteAutoStop = on;
        await _session.CassetteAsync((c, _) => c.AutoStopSilenceSeconds = on ? 2.0 : 0);
    }

    private async void OnOpenWav(object? sender, RoutedEventArgs e)
    {
        var file = await FileDialogs.OpenAsync(this, _settings, "Insert tape (WAV)", FileDialogs.WavTypes);
        if (file is null) return;
        try
        {
            var audio = WavFile.Read(file.Value.Content);
            await _session.CassetteAsync((c, _) => c.Insert(audio.Samples, audio.SampleRate));
        }
        catch (FormatException ex)
        {
            await MessageDialog.ShowAsync(this, "Could not read tape", ex.Message);
        }
    }

    private async void OnSaveWav(object? sender, RoutedEventArgs e)
    {
        var (samples, rate) = await _session.GetTapeAsync();
        if (samples.Length == 0)
        {
            await MessageDialog.ShowAsync(this, "Nothing to save", "The tape is empty. Record something first.");
            return;
        }

        await FileDialogs.SaveAsync(this, _settings, "Save tape as WAV", "kim-tape.wav", FileDialogs.WavTypes, WavFile.Write(samples, rate));
    }

    private async void OnSaveProgram(object? sender, RoutedEventArgs e)
    {
        if (!TryHex(SaveStartBox.Text, out ushort start) || !TryHex(SaveEndBox.Text, out ushort end) || end < start
            || !TryHex(SaveIdBox.Text, out ushort id) || id > 0xFF)
        {
            await MessageDialog.ShowAsync(this, "Check the values", "Start and end are hex addresses (end at or after start); ID is a hex byte. IDs 00 and FF have special meanings when loading, so use 01–FE.");
            return;
        }

        await _session.SaveToTapeAsync(start, end, (byte)id);
    }

    private async void OnLoadProgram(object? sender, RoutedEventArgs e)
    {
        if (!TryHex(LoadIdBox.Text, out ushort id) || id > 0xFF)
        {
            await MessageDialog.ShowAsync(this, "Check the ID", "The ID is a hex byte; 00 loads the first program found.");
            return;
        }

        var status = await _session.CassetteStatusAsync();
        if (!status.HasTape)
        {
            await MessageDialog.ShowAsync(this, "No tape", "Open a WAV file (or record one) first.");
            return;
        }

        await _session.LoadFromTapeAsync((byte)id);
    }

    private static bool TryHex(string? text, out ushort value) =>
        ushort.TryParse(text?.Trim().TrimStart('$'), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
}
