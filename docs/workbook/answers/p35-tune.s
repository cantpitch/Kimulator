; Problem 35 - Play a tune
; Play the notes in TUNE on PA0, then BRK. Each entry is a pitch and a length
; in 1/16 s units; pitch 0 is a rest and length 0 ends the tune.
; The 6530-003 timer times the pitch, the 6530-002 timer the length.
; The last 1/16 s of every note is silent, so repeated notes don't run together.

        .org $0200
APORTA  = $1700
APORTAD = $1701
TIMER8  = $1705         ; 6530-003: start the /8 timer
TIMEOUT = $1707         ; 6530-003: bit 7 = timer ran out
; CLKKT ($1747) and CLKRDI ($1747) are the monitor's names for the
; 6530-002's /1024 timer (write) and its flag (read).
UNIT    = 60            ; (60 + 1) x 1024 us = 62.5 ms = 1/16 s

; Half periods in 8 us steps, less 1 (the timer runs N + 1 steps)
; and less 5 for the loop's own time (it watches two timers, so it is
; slower than problem 34's).
C4 = 233
D4 = 207
E4 = 184
F4 = 173
G4 = 153
A4 = 136

PITCH   = $10
LEFT    = $11           ; units left in this note

start:  lda #$01
        sta APORTAD
        ldx #0          ; X = offset of the current note in TUNE
@note:  lda TUNE+1,x
        beq @end        ; length 0: the end
        sta LEFT
        lda TUNE,x
        sta PITCH
        inx
        inx
        lda #UNIT
        sta CLKKT       ; start the first 1/16 s
        lda PITCH
        sta TIMER8

@play:  bit TIMEOUT     ; half period over?
        bpl @unit
        lda PITCH
        sta TIMER8      ; time the next one
        beq @unit       ; a rest: don't flip the pin
        ldy LEFT
        cpy #1
        beq @unit       ; the last unit of a note is silent
        lda APORTA
        eor #$01
        sta APORTA
@unit:  bit CLKRDI      ; 1/16 s over?
        bpl @play
        lda #UNIT
        sta CLKKT
        dec LEFT
        bne @play
        jmp @note
@end:   brk

; Twinkle, twinkle, little star (first line)
TUNE:   .byte C4,4, C4,4, G4,4, G4,4, A4,4, A4,4, G4,8
        .byte F4,4, F4,4, E4,4, E4,4, D4,4, D4,4, C4,8
        .byte 0,0
