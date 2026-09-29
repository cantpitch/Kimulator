; Problem 36 - Keypad organ
; While a hex key is held, play a note on PA0: key 0 = C4 up the C major
; scale to key F = D6. No key (or another key): silence.
; GETKEY takes up to about 0.3 ms, so read the keypad while the timer
; is timing the current half period, not in between.

        .org $0200
APORTA  = $1700
APORTAD = $1701
TIMER8  = $1705
TIMEOUT = $1707
NOTE    = $10           ; half period for the key that is down, 0 = none

start:  lda #$01
        sta APORTAD
        lda #$00
        sta PADD        ; keypad columns are inputs (GETKEY expects SCANDS to have done this)
        lda #0
        sta NOTE
@loop:  lda NOTE
        beq @key        ; silent: just read the keypad
        sta TIMER8      ; time this half period...
        lda APORTA
        eor #$01
        sta APORTA
@key:   jsr GETKEY      ; ...while we read the keypad
        cmp #$10
        bcc @hex
        lda #0          ; no key, or not a hex key
        beq @store
@hex:   tax
        lda NOTES,x
@store: sta NOTE
        beq @loop       ; no note: don't wait for the timer
@wait:  bit TIMEOUT
        bpl @wait
        jmp @loop

; Half periods in 8 us steps, less 3 as in problem 35: C4 D4 E4 F4 G4 A4 B4 C5 ... D6
NOTES:  .byte 236, 210, 187, 176, 156, 139, 124, 116
        .byte 103,  92,  86,  77,  68,  60,  57,  50
