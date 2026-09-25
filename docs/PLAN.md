# Kimulator roadmap

## Decisions

| Topic | Decision |
|---|---|
| UI | Avalonia 12, targeting Windows, macOS and Linux (all three are real targets, built in CI) |
| CPU | Our own cycle-accurate NMOS 6502 with undocumented opcodes. Each bus access is exactly one cycle; interrupts are polled on the second-to-last cycle |
| Fidelity | Emulate the hardware rather than patching the ROM. The original monitor drives the display, keypad and TTY itself |
| ROMs | Public 6530-002/-003 dumps, kept in the repo (`assets/roms`) and embedded in `Kimulator.Kim1` |
| Board view | Photo plus a JSON hotspot layout in the photo's pixel coordinates. The LED segments are drawn over the photo's display window. Full and compact (display + keypad) views |
| Assembler (later) | Modern syntax (ca65/64tass style) |

## Architecture

- **`ICpuBus`**: the CPU's only link to the rest of the system. Each `Read`/`Write` is one clock cycle, and the board
  ticks every device (RRIOT timers, cards) inside that call. SYNC is passed through for the SST logic and, later,
  debugger hooks.
- **`IMachine` + `MachineRunner`**: the emulation thread runs in 1 ms slices paced against the wall clock. Other threads
  change the machine only through `Post()`/`InvokeAsync()`.
- **`IExpansionCard`**: sees every cycle with the full 16-bit address, can claim reads and writes, can assert IRQ/NMI,
  and can switch off the base board's decoding (the KIM-1 DECODE ENABLE line) so memory above 8 KB works
  without the base board's mirror images.
- **LED display**: integrates how many cycles each segment is on, so multiplexing, brightness and flicker come out
  naturally.

## Phases

| # | Phase | Status |
|---|---|---|
| 0 | Solution skeleton, CI on Windows/macOS/Linux | ✅ |
| 1 | Cycle-accurate 6502; Harte (all 256 opcodes) and Dormann tests | ✅ |
| 2 | 6530 RRIOT, KIM-1 board, address map, keypad matrix, display, RS/ST/SST; headless monitor tests | ✅ |
| 3 | Avalonia board view: photo overlay, LED rendering, clickable keys, PC keyboard, SST switch, compact view, speed control | ✅ first version |
| 4 | TTY serial mode with a terminal window (bit-banged through PA7/PB0 at the real bit timing, PB5-gated hardware echo); paper-tape load/dump; load/save `.ptp`/`.hex`/`.bin`; save states; persisted settings | ✅ |
| 5 | Debugger window: registers, disassembly, memory hex view/edit, breakpoints/watchpoints, step by instruction or cycle, bus trace | |
| 6 | Text editor + built-in assembler (modern syntax) that assembles straight into memory | |
| 7 | Cassette: WAV record/playback through the PB7 audio path; speaker audio for music programs | |
| 8 | Expansion cards with their own board images: KIM-4 motherboard, KIM-2/KIM-3 RAM, KIM-5 ROM, KIM-6 prototyping; card rack view | |

## Remaining polish

- Configurable key bindings.
- Optional key-click sound.
- Terminal: a blinking cursor and an optional "paper" look (it's a plain read-only text box today).
- Save states from other versions: the format is versioned but there is no migration yet.
- Fine-tune segment brightness and size against the photo.
- App icon; packaging (`dotnet publish` single-file per OS, macOS `.app` bundle).
