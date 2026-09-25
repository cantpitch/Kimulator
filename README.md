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

By default, power-on points the NMI and IRQ vectors (`$17FA`, `$17FE`) at the monitor (`$1C00`) so ST, SST
and BRK work right away. On a real KIM-1 you enter these by hand. You can turn this off in the Machine menu.

## Test

```bash
./scripts/fetch-test-data.sh   # one-time: ~1.1 GB Harte suite into test-data/ (git-ignored)
dotnet test tests/Kimulator.Tests
```

The Harte tests skip themselves when the data is missing.

## Layout

| Project | Contents |
|---|---|
| `src/Kimulator.Core` | 6502 core, opcode table, `MachineRunner` (real-time emulation thread), expansion-card contract |
| `src/Kimulator.Kim1` | 6530 RRIOT, KIM-1 board, LED display model, embedded ROMs |
| `src/Kimulator.App` | Avalonia UI: board photo view, hotspot layout (`Assets/kim1-layout.json`) |
| `tests/Kimulator.Tests` | CPU suites, interrupt timing, headless KIM-1 monitor tests |

See [docs/PLAN.md](docs/PLAN.md) for the roadmap.

## Credits

- KIM-1 ROM images: the public 6530-002/-003 dumps from [Hans Otten's KIM-1 pages](http://retro.hansotten.nl/6502-sbc/kim-1-manuals-and-software/kim_1-roms/).
- [SingleStepTests/65x02](https://github.com/SingleStepTests/65x02) (MIT) and
  [Klaus Dormann's 6502 functional tests](https://github.com/Klaus2m5/6502_65C02_functional_tests) (GPL-3.0, the test binary only).
