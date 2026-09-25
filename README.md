# Kimulator

A cross-platform (Windows, macOS, Linux) KIM-1 emulator in .NET 10 and Avalonia. The window shows a photo
of a real KIM-1, and the keys, the SST switch and the LED display on the photo all work.

- **Cycle-accurate NMOS 6502**, including the undocumented opcodes and NMOS decimal-mode behavior. It passes
  Tom Harte's SingleStepTests, which check every bus cycle of every opcode, and Klaus Dormann's functional test.
- **Hardware-level KIM-1**: the original 6530-002 and 6530-003 ROMs run unmodified, with no patched monitor
  routines. The model covers the 74145 digit/row decoder, the keypad matrix, the multiplexed LEDs (brightness
  comes from each segment's duty cycle), RS/ST/SST, and the 8 KB address mirroring.

## Run

```bash
dotnet run --project src/Kimulator.App
```

Add `-- --compact` to start with only the display and keypad.

| PC key | KIM-1 key |
|---|---|
| 0–9, A–F | hex keys |
| L / F1 | AD |
| M / F2 | DA |
| P / F3 | PC |
| + / Enter / Space | + |
| G / F5 | GO |
| Esc | ST |
| F12 | RS |
| F9 | SST switch |
| Ctrl+O / Ctrl+M | load program / save memory range (`.ptp`, `.hex`, `.bin`) |
| Ctrl+S / Ctrl+L, F6 / F7 | save / load state, quick save / quick load |
| Ctrl+T | terminal (TTY) window |
| Ctrl+D | debugger |
| Ctrl+E | assembler / editor |
| Ctrl+K | cassette deck |
| Ctrl+1 / Ctrl+2 | full board / compact view |

By default, power-on points the NMI and IRQ vectors (`$17FA`, `$17FE`) at the monitor (`$1C00`) so ST, SST
and BRK work right away. On a real KIM-1 you enter these by hand. You can turn this off in the Machine menu.

## Teletype (TTY) mode

**Machine › TTY mode** moves the TTY/KB jumper and resets the KIM. The monitor then talks to the terminal
window at the bit level: it measures the baud rate from the first character after reset. By default the app
sends that RUBOUT for you. The board's hardware echo shows what you type.

What you can use there:
- The monitor's TTY commands: `0200␠` opens a cell, `A9.` deposits a byte, Enter moves to the next cell,
  Shift+Enter to the previous one, `G` runs, `Q` dumps paper tape up to EAL/EAH (`$17F7`), and `L` loads it.
- **Load paper tape** types `L` and then sends a `.ptp` file, as if it came from the teletype's tape reader.
- Pasted text and type-ahead are sent only when the KIM's output line is idle, because the monitor has no
  receive buffer.

**File › Load program** is the fast path: it writes `.ptp`, Intel HEX or raw binary straight into RAM and sets
the monitor's open cell, so GO runs the program. Settings such as view, window placement, speed, TTY mode and
baud rate are saved in `%APPDATA%\Kimulator` on Windows or `~/.config/Kimulator` on macOS and Linux.

## Debugger

**View › Debugger** (Ctrl+D) opens a debugger for the running machine:
- Disassembly with the monitor's own labels (`SCAND`, `OUTCH`, …) and undocumented opcodes highlighted.
  Click the gutter to set a breakpoint.
- F5 continue/pause, F11 step, F10 step over (runs a JSR as one step), Shift+F11 step out, Ctrl+F10 run to
  the selected line, F9 toggle a breakpoint.
- Editable registers and flags while stopped, and a stack view.
- A memory hex editor that highlights bytes changed since the last stop.
- Execute breakpoints, read/write watchpoints over address ranges, and stop on BRK or JAM.
- A trace of the last 4096 bus cycles (read, opcode fetch, write), plus a history of recently executed instructions.

Stepping works one instruction at a time. For cycle-level detail, use the bus trace. When a breakpoint hits, the
whole machine stops and the LED display freezes on its last frame.

## Assembler

**View › Assembler** (Ctrl+E) opens an editor with syntax highlighting and a built-in 6502 assembler. It uses
ca65/64tass-style syntax:

```asm
        .org $0200              ; or  * = $0200
COUNT   = 10                    ; constants
start:  ldx #COUNT              ; 'start' (if defined) is where Load & run begins
@loop:  dex                     ; @local labels are scoped to the previous label
        bne @loop
:       jsr SCANDS              ; anonymous labels:  :-  :+  (KIM monitor labels are predefined)
        lda #<msg               ; < low byte, > high byte, * current address
        jmp :-
msg:    .byte "HI", $0D, 0      ; .byte .word .dbyt .res .text .asciiz .align .end
```

Details:
- Symbols are case-insensitive. Zero-page addressing is used automatically when an operand is known to be below
  `$100`; `a:` and `z:` force absolute or zero page.
- Undocumented opcodes (LAX, SAX, DCP, ISC, …) are accepted.
- `.res` without a fill value leaves memory untouched.
- Editor keys: F7 assembles, F8 loads into RAM (the start becomes the open cell, so GO runs it), F5 loads and
  runs, and F9 sets a breakpoint on the caret's line.
- The debugger shows your program's labels, and the editor highlights the current line when execution stops.
- Unsaved text is kept when you close the window.

## Cassette and sound

**View › Cassette deck** (Ctrl+K) connects a tape recorder to the KIM-1's audio interface:
- **Recording** samples the tape output (PB7) at 44.1 kHz. It can stop automatically after 2 s of silence.
- **Playback** goes through a model of the board's LM565 PLL, so the unmodified monitor reads tapes with its own
  timing loops. That works for your own recordings and for any KIM tape WAV (8/16/24/32-bit PCM or float,
  mono or stereo).
- **"Record program" and "Play and load"** do the monitor steps for you: they set SAL/EAL/ID and run `$1800`
  (DUMPT) or `$1873` (LOADT). Tape time is emulated time, so Machine › Speed › Unthrottled loads much faster.

**Machine › Sound** plays a pin through your speakers: the tape output (PB7, the pin most KIM music programs
toggle) or application port PA0/PB0. Audio goes through OpenAL Soft, which is bundled for Windows, macOS and
Linux.

## Test

```bash
./scripts/fetch-test-data.sh   # one-time: ~1.1 GB Harte suite into test-data/ (git-ignored)
dotnet test tests/Kimulator.Tests
```

The Harte tests skip themselves when the data is missing.

## Layout

| Project | Contents |
|---|---|
| `src/Kimulator.Core` | 6502 core, opcode table, `MachineRunner` (real-time emulation thread), assembler, debugger/disassembler/symbols, expansion-card contract, paper tape / Intel HEX / WAV formats, audio sampling, teletype text buffer |
| `src/Kimulator.Kim1` | 6530 RRIOT, KIM-1 board, LED display model, bit-level TTY interface, cassette with PLL model, save states, embedded ROMs and monitor symbols |
| `src/Kimulator.App` | Avalonia UI: board photo view, hotspot layout (`Assets/kim1-layout.json`), terminal, debugger, assembler and cassette windows, OpenAL audio, settings |
| `tests/Kimulator.Tests` | CPU suites, interrupt timing, headless KIM-1 keypad and TTY monitor tests, file formats, save states, cassette round trips, debugger, disassembler and assembler |

See [docs/PLAN.md](docs/PLAN.md) for the roadmap.

## Credits

- KIM-1 ROM images: the public 6530-002/-003 dumps from [Hans Otten's KIM-1 pages](http://retro.hansotten.nl/6502-sbc/kim-1-manuals-and-software/kim_1-roms/).
- [SingleStepTests/65x02](https://github.com/SingleStepTests/65x02) (MIT) and
  [Klaus Dormann's 6502 functional tests](https://github.com/Klaus2m5/6502_65C02_functional_tests) (GPL-3.0, the test binary only).
