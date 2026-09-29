; Problem 34 - Concert A
; Play a 440 Hz square wave on PA0 of the application port (6530-003).
; Kimulator: Machine > Sound > Application port PA0.
;
; 440 Hz means the pin flips every 1/880 s = 1136 us, 142 steps of the /8 timer.
; The flag rises N + 1 steps after writing N, and the loop itself takes about
; 15 us (2 steps) on top, so N = 142 - 1 - 2 = 139.

        .org $0200
APORTA  = $1700         ; application port A data
APORTAD = $1701         ; its data direction register (1 = output)
TIMER8  = $1705         ; write: start the timer, one count per 8 cycles
TIMEOUT = $1707         ; read: bit 7 set when the timer has run out
HALF    = 139           ; half a period, in 8 us steps

start:  lda #$01
        sta APORTAD     ; PA0 is an output
@loop:  lda #HALF
        sta TIMER8      ; start timing the next half period first...
        lda APORTA
        eor #$01        ; ...then flip PA0
        sta APORTA
@wait:  bit TIMEOUT
        bpl @wait
        jmp @loop
