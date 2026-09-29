# Hardware notes

Behaviour of the emulated KIM-1 that isn't obvious from the code and that programs depend on. Each point was
checked by running code on the headless board (see `tests/Kimulator.Tests/Workbook`).

## 6530 interval timer

- Writing N to a timer address starts it. Bit 7 of the flag register (offset +7, e.g. `$1707`) goes to 1 after
  **N + 1** prescaler periods, not N. For exactly T periods, write T − 1. (`Riot6530.Tick`: the flag is set when the
  count passes through zero.)
- After the time-out the counter keeps going down at the full clock rate, once per cycle.
- Writing the timer clears the flag, and so does reading the timer value. Reading the flag register doesn't.
- Offsets +4…+7 select ÷1, ÷8, ÷64, ÷1024. Adding 8 (+$C…+$F) also enables the timer interrupt.
- Examples: 10 ms with ÷64 is 155 (156 × 64 = 9984 µs). ¼ s with ÷1024 is 243. 1/16 s with ÷1024 is 60.
- A polling loop adds its own time on top. The workbook's single-timer tone loop takes about 2 ÷8 steps, and its
  two-timer tune loop about 5.

## Timer interrupt

- Only the 6530-003's timer can interrupt. Its IRQ output is PB7, which reaches the CPU only when
  `Kim1Board.Riot003TimerIrqJumper` is set. In the app that's **Machine › Timer IRQ jumper**, saved in settings and
  in save states. It is off by default, as on a stock KIM-1.
- The 6530-002's PB7 is the tape output, not an interrupt.
- IRQ and BRK go through `$FFFE` → monitor ROM → `JMP ($17FE)` (IRQV). NMI (ST, SST) goes through `$17FA` (NMIV).
  The app's **Preset NMI/IRQ vectors at power-on** points both at SAVE (`$1C00`), so a program that uses IRQs has
  to change IRQV itself.

## Monitor keypad and display routines

| Routine | Takes | Returns / needs |
|---|---|---|
| `SCANDS` `$1F1F` | ~4.2 ms (4237 cycles) | Shows `POINTH` `POINTL` `INH` once. Ends in `AK`: Z = 1 no key, Z = 0 key down. Leaves `PADD` = `$00`, `PBDD` = `$3F`. |
| `GETKEY` `$1F6A` | 140–260 cycles | A = key code, or `$15` for none. **Doesn't set `PADD`**: port A must already be an input, which `SCANDS` leaves it as. Call it with `PADD` = `$7F` (as the monitor often leaves it mid-scan) and it reads the output latch, returning a bogus key. |
| `AK` `$1EFE` | ~78 cycles | A ≠ 0 and Z = 0 if a key is down. |

Key codes: `$00`–`$0F` hex keys, `$10` AD, `$11` DA, `$12` +, `$13` GO, `$14` PC. ST is NMI and RS is RES, not
matrix keys.

## Display and keypad wiring (6530-002)

- Segments a–g are PA0–PA6 (`SAD` `$1740`), 1 = lit, with `PADD` = `$7F`.
- PB1–PB4 (`SBD` `$1742`) drive the 74145. Outputs 4–9 select digits 0–5 from the left, so write (n + 4) × 2.
  Outputs 0–2 select keypad rows.
- A pressed key pulls its column low. Code = row × 7 + (6 − PA bit): PA6 is the first key of a row. PA7 is the TTY
  input.

## Memory

- Base RAM is `$0000`–`$03FF`. The monitor uses `$EF`–`$FF` and `$17E7`–`$17FF`. `$1780`–`$17BF` (6530-003 RAM)
  is free.
- Cards are checked before on-board decoding, so a K-1008 at `$2000` works without a KIM-4.
- K-1008: 40 bytes per line, bit 7 is the leftmost dot, 1 = lit. `$2000`–`$3F3F` is shown, and the last 192 bytes
  aren't.

## Sound

**Machine › Sound** samples one pin: tape out (6530-002 PB7), or application port PA0 (`$1700` bit 0) or PB0
(`$1702` bit 0). A pin only sounds while its direction bit is set to output.
