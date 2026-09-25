using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Kimulator.Core.Assembly;
using Kimulator.Core.Debugging;
using Kimulator.Kim1;

namespace Kimulator.App.Editor;

/// <summary>
/// Source editor with a built-in assembler that writes straight into the KIM's memory.
/// Lines map to addresses, so breakpoints can be set from the source and the current PC is highlighted.
/// </summary>
public partial class AssemblerWindow : Window
{
    public const string SampleProgram = """
        ; Kimulator assembler — F5 assembles, loads and runs.
        ; KIM-1 monitor labels (SCANDS, GETKEY, OUTCH, POINTL, ...) are predefined.

                .org $0200

        start:  lda #$C0        ; show "C0DE 00" on the LED display
                sta POINTH
                lda #$DE
                sta POINTL
                lda #$00
                sta INH
        loop:   jsr SCANDS      ; one scan of the display (also reads the keypad)
                jmp loop        ; press ST to return to the monitor
        """;

    private readonly EmulatorSession _session;
    private readonly AppSettings _settings;
    private readonly LineHighlighter _highlighter = new();
    private string? _filePath;
    private bool _dirty;
    private bool _editedSinceAssemble = true;
    private AssemblyResult? _lastResult;
    private Dictionary<ushort, int> _lineByAddress = [];

    public AssemblerWindow() : this(null!, new AppSettings()) { } // designer

    public AssemblerWindow(EmulatorSession session, AppSettings settings)
    {
        InitializeComponent();
        _session = session;
        _settings = settings;

        Editor.SyntaxHighlighting = AsmHighlighting.Definition;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 8;
        Editor.Options.HighlightCurrentLine = true;
        Editor.TextArea.TextView.BackgroundRenderers.Add(_highlighter);
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCaret();
        Editor.TextChanged += (_, _) =>
        {
            _editedSinceAssemble = true;
            if (!_dirty) SetDirty(true);
            if (_highlighter.ErrorLines.Count > 0)
            {
                _highlighter.ErrorLines.Clear();
                Editor.TextArea.TextView.InvalidateLayer(_highlighter.Layer);
            }
        };

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        RestoreDocument();

        if (session is null) return;
        session.Stopped += OnStopped;
        session.Resumed += OnResumed;
        Opened += (_, _) => Editor.Focus();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Never lose work: unsaved text is kept in the settings and restored next time.
        _settings.AssemblerFile = _filePath;
        _settings.AssemblerScratch = _dirty || _filePath is null ? Editor.Text : null;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_session is not null)
        {
            _session.Stopped -= OnStopped;
            _session.Resumed -= OnResumed;
        }

        base.OnClosed(e);
    }

    private void RestoreDocument()
    {
        _filePath = _settings.AssemblerFile;
        if (_settings.AssemblerScratch is { } scratch)
        {
            Editor.Text = scratch;
            SetDirty(_filePath is not null);
        }
        else if (_filePath is not null && File.Exists(_filePath))
        {
            Editor.Text = File.ReadAllText(_filePath);
            SetDirty(false);
        }
        else
        {
            _filePath = null;
            Editor.Text = SampleProgram;
            SetDirty(false);
        }
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        string name = _filePath is null ? "untitled.asm" : Path.GetFileName(_filePath);
        Title = $"{(dirty ? "● " : "")}{name} — KIM-1 Assembler";
    }

    private void UpdateCaret()
    {
        int line = Editor.TextArea.Caret.Line;
        string address = _lastResult?.LineAddresses.TryGetValue(line, out ushort a) == true ? $"  ${a:X4}" : "";
        CaretText.Text = $"Ln {line}, Col {Editor.TextArea.Caret.Column}{address}";
    }

    // ---------------------------------------------------------------- keyboard

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key, e.KeyModifiers)
        {
            case (Key.F7, KeyModifiers.None): OnAssemble(null, e); break;
            case (Key.F8, KeyModifiers.None): OnLoad(null, e); break;
            case (Key.F5, KeyModifiers.None): OnLoadAndRun(null, e); break;
            case (Key.F9, KeyModifiers.None): OnToggleBreakpoint(null, e); break;
            case (Key.S, KeyModifiers.Control): OnSave(null, e); break;
            case (Key.O, KeyModifiers.Control): OnOpen(null, e); break;
            case (Key.N, KeyModifiers.Control): OnNew(null, e); break;
            default: return;
        }

        e.Handled = true;
    }

    // ---------------------------------------------------------------- files

    private async void OnNew(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync()) return;
        _filePath = null;
        Editor.Text = SampleProgram;
        SetDirty(false);
        ClearResult();
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync()) return;
        var file = await FileDialogs.OpenPathAsync(this, _settings, "Open assembly source", FileDialogs.AssemblyTypes);
        if (file is null) return;
        _filePath = file.Value.Path;
        Editor.Text = Encoding.UTF8.GetString(file.Value.Content);
        SetDirty(false);
        ClearResult();
    }

    private async void OnSave(object? sender, RoutedEventArgs e) => await SaveAsync(saveAs: _filePath is null);

    private async void OnSaveAs(object? sender, RoutedEventArgs e) => await SaveAsync(saveAs: true);

    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (saveAs || _filePath is null)
        {
            string suggested = _filePath is null ? "program.asm" : Path.GetFileName(_filePath);
            string? path = await FileDialogs.SavePathAsync(this, _settings, "Save assembly source", suggested, FileDialogs.AssemblyTypes, Encoding.UTF8.GetBytes(Editor.Text));
            if (path is null) return false;
            _filePath = path;
        }
        else
        {
            await File.WriteAllTextAsync(_filePath, Editor.Text);
        }

        SetDirty(false);
        StatusText.Text = $"Saved {_filePath}.";
        return true;
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_dirty) return true;
        return await ConfirmDialog.AskAsync(this, "Discard changes?", "The current program has unsaved changes. Discard them?", "Discard");
    }

    // ---------------------------------------------------------------- assemble / load / run

    private AssemblyResult Assemble()
    {
        var result = Assembler.Assemble(Editor.Text, new AssemblerOptions { PredefinedSymbols = Kim1Board.MonitorSymbols });
        _lastResult = result;
        _editedSinceAssemble = false;
        _lineByAddress = [];
        foreach (var (line, address) in result.LineAddresses) _lineByAddress.TryAdd(address, line);

        _highlighter.ErrorLines.Clear();
        foreach (var error in result.Errors) _highlighter.ErrorLines.Add(error.Line);
        Editor.TextArea.TextView.InvalidateLayer(_highlighter.Layer);

        ProblemList.ItemsSource = result.Errors;
        ListingText.Text = result.FormatListing();
        var symbols = new StringBuilder();
        foreach (var (name, value) in result.Symbols.Symbols.OrderBy(s => s.Value).ThenBy(s => s.Key))
            symbols.Append($"${value:X4}  {name}\n");
        SymbolText.Text = symbols.ToString();

        if (!result.Success)
        {
            Tabs.SelectedIndex = 0;
            StatusText.Text = $"{result.Errors.Count} error{(result.Errors.Count == 1 ? "" : "s")}. Double-click one to go to its line.";
            if (result.Errors.FirstOrDefault(er => er.Line > 0) is { } first) GoToLine(first.Line);
        }
        else
        {
            string ranges = string.Join(", ", result.Segments.Select(s => $"${s.Address:X4}–${s.End - 1:X4}"));
            StatusText.Text = result.ByteCount == 0 ? "Assembled: no bytes produced." : $"Assembled {result.ByteCount} bytes at {ranges}.";
        }

        UpdateCaret();
        return result;
    }

    private void OnAssemble(object? sender, RoutedEventArgs e)
    {
        if (Assemble().Success) Tabs.SelectedIndex = 1;
    }

    private async void OnLoad(object? sender, RoutedEventArgs e) => await LoadAsync(run: false);

    private async void OnLoadAndRun(object? sender, RoutedEventArgs e) => await LoadAsync(run: true);

    private async Task LoadAsync(bool run)
    {
        var result = Assemble();
        if (!result.Success || result.ByteCount == 0) return;

        ushort entry = result.Symbols.TryGetAddress("start", out ushort start) ? start : result.StartAddress ?? result.Segments[0].Address;
        int skipped = await _session.LoadProgramAsync(result.Segments, entry);
        _session.SetProgramSymbols(result.Symbols);
        await RefreshBreakpointsAsync();

        string where = skipped > 0
            ? $" {skipped} bytes fell outside RAM ($0000–$03FF, $1780–$17FF) and were skipped!"
            : "";
        if (run)
        {
            await _session.RunFromAsync(entry);
            StatusText.Text = $"Loaded {result.ByteCount} bytes; running from ${entry:X4}.{where}";
        }
        else
        {
            StatusText.Text = $"Loaded {result.ByteCount} bytes; the open cell is ${entry:X4}, so GO runs it.{where}";
        }
    }

    private void ClearResult()
    {
        _lastResult = null;
        _lineByAddress = [];
        _highlighter.ErrorLines.Clear();
        _highlighter.BreakpointLines.Clear();
        _highlighter.PcLine = null;
        ProblemList.ItemsSource = null;
        ListingText.Text = SymbolText.Text = "";
        Editor.TextArea.TextView.InvalidateLayer(_highlighter.Layer);
    }

    private void OnProblemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ProblemList.SelectedItem is AssemblyError { Line: > 0 } error) GoToLine(error.Line);
    }

    private void GoToLine(int line)
    {
        if (line > Editor.Document.LineCount) return;
        Editor.TextArea.Caret.Line = line;
        Editor.TextArea.Caret.Column = 1;
        Editor.ScrollToLine(line);
        Editor.Focus();
    }

    // ---------------------------------------------------------------- debugger integration

    private async void OnToggleBreakpoint(object? sender, RoutedEventArgs e)
    {
        if ((_lastResult is null || _editedSinceAssemble) && !Assemble().Success) return;
        int line = Editor.TextArea.Caret.Line;
        if (!_lastResult!.LineAddresses.TryGetValue(line, out ushort address))
        {
            StatusText.Text = "This line has no code. Put the caret on an instruction (and assemble first).";
            return;
        }

        await _session.WithDebuggerAsync(d => d.ToggleExecute(address));
        await RefreshBreakpointsAsync();
    }

    private async Task RefreshBreakpointsAsync()
    {
        var breakpoints = await _session.WithDebuggerAsync(d => d.Breakpoints.ToList());
        _highlighter.BreakpointLines.Clear();
        foreach (var bp in breakpoints.Where(b => b.Enabled && b.Kind == BreakpointKind.Execute && b.Start == b.End))
        {
            if (_lineByAddress.TryGetValue(bp.Start, out int line)) _highlighter.BreakpointLines.Add(line);
        }

        Editor.TextArea.TextView.InvalidateLayer(_highlighter.Layer);
    }

    private async void OnStopped(StopInfo stop)
    {
        await RefreshBreakpointsAsync();
        _highlighter.PcLine = _lineByAddress.TryGetValue(stop.Pc, out int line) ? line : null;
        if (_highlighter.PcLine is { } pcLine) Editor.ScrollToLine(pcLine);
        Editor.TextArea.TextView.InvalidateLayer(_highlighter.Layer);
    }

    private void OnResumed()
    {
        _highlighter.PcLine = null;
        Editor.TextArea.TextView.InvalidateLayer(_highlighter.Layer);
    }
}
