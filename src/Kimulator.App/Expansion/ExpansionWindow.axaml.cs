using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Kimulator.Kim1.Cards;

namespace Kimulator.App.Expansion;

/// <summary>
/// Configure KIM system cards: the KIM-4 motherboard and KIM-2/3/5 cards in its slots (or a single
/// card on the KIM-1's expansion connector). Card switches are set by clicking them on the card photo.
/// </summary>
public partial class ExpansionWindow : Window
{
    private static readonly (CardType? Type, string Label)[] CardChoices =
    [
        (null, "(empty)"),
        (CardType.Kim2, CardConfig.DisplayName(CardType.Kim2)),
        (CardType.Kim3, CardConfig.DisplayName(CardType.Kim3)),
        (CardType.Kim5, CardConfig.DisplayName(CardType.Kim5)),
        (CardType.K1008, CardConfig.DisplayName(CardType.K1008)),
    ];

    private readonly EmulatorSession _session;
    private readonly AppSettings _settings;
    private readonly Action? _showVisibleMemory;
    private ExpansionConfig _config;
    private int _slot;
    private bool _updating;
    private bool _showingKim4;

    public ExpansionWindow() : this(null!, new AppSettings()) { } // designer

    public ExpansionWindow(EmulatorSession session, AppSettings settings, Action? showVisibleMemory = null)
    {
        InitializeComponent();
        _session = session;
        _settings = settings;
        _showVisibleMemory = showVisibleMemory;
        _config = (session?.Expansion ?? new ExpansionConfig()).Clone();
        CardTypeBox.ItemsSource = CardChoices.Select(c => c.Label).ToList();
        Card.SwitchToggled += OnSwitchToggled;
        Refresh();
    }

    // ---------------------------------------------------------------- view

    private void Refresh()
    {
        _updating = true;
        Kim4Box.IsChecked = _config.Kim4;
        SystemImage.Source = (_config.Kim4 ? CardArt.Kim4WithKim1 : CardArt.Kim4).Bitmap;
        Kim4Picture.IsVisible = _config.Kim4;
        SystemHint.Text = _config.Kim4
            ? "The KIM-4 buffers the bus to six slots and takes over address decoding: cards answer at $0400–$13FF and $2000–$FFF7, and the KIM-1's memory no longer repeats every 8K. $FFF8–$FFFF stay on the KIM-1 so reset/NMI/IRQ still reach the monitor."
            : "Without a motherboard, one card (a KIM-2, KIM-3 or K-1008) can be cabled straight to the KIM-1's expansion connector. The KIM-1 only decodes 13 address lines, so its 8K appears again throughout memory wherever no card answers.";

        _slot = Math.Min(_slot, _config.SlotCount - 1);
        SlotList.ItemsSource = Enumerable.Range(0, _config.SlotCount).Select(SlotSummary).ToList();
        SlotList.SelectedIndex = _slot;

        MapView.Update(_config);
        var problems = _config.Problems();
        ProblemText.Text = string.Join("\n", problems.Select(p => "⚠ " + p));
        ProblemText.IsVisible = problems.Count > 0;
        RegionText.Text = string.Join("\n", _config.MemoryMap().Select(r => $"{r.Range}  {r.Owner}"));

        ShowSlot();
        UpdateStatus();
        _updating = false;
    }

    /// <summary>Shows a slot's card (used by the card indicator on the main window).</summary>
    public void SelectSlot(int slot)
    {
        if (slot < 0 || slot >= _config.SlotCount) return;
        _slot = slot;
        Refresh();
    }

    private string SlotSummary(int slot)
    {
        string name = _config.Kim4 ? $"Slot {slot + 1}" : "Expansion connector";
        if (_config.Slot(slot) is not { } card) return $"{name}: empty";
        string ranges = string.Join(", ", ExpansionConfig.RangesOf(card));
        return $"{name}: {CardConfig.DisplayName(card.Type).Split("  ")[0]}  {ranges}";
    }

    private void ShowSlot()
    {
        _showingKim4 = false;
        SlotTitle.Text = _config.Kim4 ? $"Slot {_slot + 1}" : "Expansion connector";
        var card = _config.Slot(_slot);
        CardTypeBox.SelectedIndex = Array.FindIndex(CardChoices, c => c.Type == card?.Type);
        SocketPanel.Children.Clear();

        if (card is null)
        {
            Card.Show(null);
            CardAddressText.Text = "";
            CardHint.Text = "Choose a card for this slot.";
            return;
        }

        var art = CardArt.For(card.Type);
        Card.Show(art, card.Switches, card.Switches2);
        CardAddressText.Text = "Answers at " + (ExpansionConfig.RangesOf(card).Any()
            ? string.Join(", ", ExpansionConfig.RangesOf(card))
            : "nothing (no ROMs fitted)");

        CardHint.Text = card.Type switch
        {
            CardType.Kim2 => "Click the address switch to set the 4K block: switch 1 = A15, 2 = A14, 3 = A13, 4 = A12, on = 1 (MOS KIM-2/3 manual, Table 4).",
            CardType.Kim3 => "Click the address switch to set the 8K block: switch 1 = A15, 2 = A14, 3 = A13, on = 1; switch 4 isn't connected (MOS KIM-2/3 manual, Table 5).",
            CardType.K1008 => "Socket S1 holds three pairs of switches: 1 or 2 sets A15 to 0 or 1, 3 or 4 sets A14, 5 or 6 sets A13 — exactly one of each pair on (K-1008 manual). MTU shipped it at $2000, where its demo programs expect it. The 320×200 picture is in View › Visible Memory display.",
            _ => "Switch banks S1 and S2 set the 8K block for sockets U1–U4 and U5–U8 (switches 1–3 = A15–A13, on = 1). The Resident Assembler/Editor needs S1 at $E000 and RAM for its text (e.g. a KIM-3 at $2000). In TTY mode, start the editor at $F100 (it sets up its I/O vectors and asks BASE=) or the assembler at $E000.",
        };

        if (card.Type == CardType.Kim5) BuildSockets(card);
        if (card.Type == CardType.K1008 && _showVisibleMemory is not null)
        {
            var show = new Button { Content = "Show the display", HorizontalAlignment = HorizontalAlignment.Left };
            show.Click += (_, _) => _showVisibleMemory();
            SocketPanel.Children.Add(show);
        }
    }

    private void BuildSockets(CardConfig card)
    {
        SocketPanel.Children.Add(new TextBlock { Text = "ROM sockets (MOS 6540, 2K each)", FontWeight = Avalonia.Media.FontWeight.SemiBold });
        for (int s = 0; s < RomCard.SocketCount; s++)
        {
            int socket = s;
            ushort address = (ushort)(RamCard.Kim3Address(s < 4 ? card.Switches : card.Switches2) + (s % 4) * RomCard.RomSize);
            string? source = card.Sockets.ElementAtOrDefault(s);
            string contents = source is null ? "empty"
                : source.StartsWith(CardConfig.BuiltInPrefix) ? $"{source[CardConfig.BuiltInPrefix.Length..]} (Resident Assembler/Editor)"
                : Path.GetFileName(source);

            var load = new Button { Content = "Load ROM…", Padding = new Avalonia.Thickness(8, 2) };
            load.Click += async (_, _) => await LoadSocketAsync(card, socket);
            var clear = new Button { Content = "Remove", Padding = new Avalonia.Thickness(8, 2), IsEnabled = source is not null };
            clear.Click += (_, _) =>
            {
                card.Sockets[socket] = null;
                Refresh();
            };

            SocketPanel.Children.Add(new DockPanel
            {
                Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, [DockPanel.DockProperty] = Dock.Right, Children = { load, clear } },
                    new TextBlock
                    {
                        Text = $"U{s + 1}  ${address:X4}–${address + RomCard.RomSize - 1:X4}   {contents}",
                        FontFamily = new Avalonia.Media.FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            });
        }
    }

    private void UpdateStatus()
    {
        bool changed = !Same(_config, _session?.Expansion ?? new ExpansionConfig());
        ApplyButton.IsEnabled = changed;
        StatusText.Text = changed
            ? "Not applied yet. Applying installs the cards now; RAM on the cards starts empty."
            : "This is the configuration the KIM is running with.";
    }

    private static bool Same(ExpansionConfig a, ExpansionConfig b) =>
        System.Text.Json.JsonSerializer.Serialize(a) == System.Text.Json.JsonSerializer.Serialize(b);

    // ---------------------------------------------------------------- edits

    private void OnKim4Changed(object? sender, RoutedEventArgs e)
    {
        if (_updating) return;
        _config.Kim4 = Kim4Box.IsChecked == true;
        Refresh();
    }

    private void OnSlotSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_updating || SlotList.SelectedIndex < 0) return;
        _slot = SlotList.SelectedIndex;
        _updating = true;
        ShowSlot();
        _updating = false;
    }

    private void OnCardTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updating || CardTypeBox.SelectedIndex < 0) return;
        var type = CardChoices[CardTypeBox.SelectedIndex].Type;
        if (type == _config.Slot(_slot)?.Type) return;
        _config.SetSlot(_slot, type is null ? null : CardConfig.Default(type.Value));
        Refresh();
    }

    private void OnSwitchToggled(int bank, int index)
    {
        if (_showingKim4 || _config.Slot(_slot) is not { } card) return;
        if (bank == 0) card.Switches ^= 1 << index;
        else card.Switches2 ^= 1 << index;
        Refresh();
    }

    private async Task LoadSocketAsync(CardConfig card, int socket)
    {
        var file = await FileDialogs.OpenPathAsync(this, _settings, $"ROM image for socket U{socket + 1}", FileDialogs.RomTypes);
        if (file is null) return;
        if (file.Value.Content.Length != RomCard.RomSize)
        {
            await MessageDialog.ShowAsync(this, "Not a 6540 ROM image", $"A 6540 ROM image is {RomCard.RomSize} bytes (2K); this file is {file.Value.Content.Length} bytes.");
            return;
        }

        card.Sockets[socket] = file.Value.Path;
        Refresh();
    }

    private void OnShowKim4(object? sender, RoutedEventArgs e)
    {
        _showingKim4 = true;
        SlotTitle.Text = "KIM-4 motherboard";
        CardAddressText.Text = "";
        CardHint.Text = "The KIM-4 has six card slots. Pick a slot on the left to choose its card.";
        SocketPanel.Children.Clear();
        Card.Show(CardArt.Kim4);
    }

    private void OnRevert(object? sender, RoutedEventArgs e)
    {
        _config = (_session?.Expansion ?? new ExpansionConfig()).Clone();
        Refresh();
    }

    private async void OnApply(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _session.ApplyExpansionAsync(_config.Clone());
            _settings.Expansion = _config.Clone();
            UpdateStatus();
            StatusText.Text = "Applied.";
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, "Could not install the cards", ex.Message);
        }
    }
}
