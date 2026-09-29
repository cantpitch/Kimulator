; Problem 28 - Tally counter
; Count key presses in decimal (0000-9999) in the address digits
; and show the code of the last key pressed in the data digits.

        .org $0200
start:  lda #0
        sta POINTH
        sta POINTL
        sta INH
@wait:  jsr SCANDS
        beq @wait       ; no key down
        jsr GETKEY
        cmp #$15
        bcs @release    ; no valid key after all
        sta INH
        sed             ; count in BCD so the display shows decimal
        clc
        lda POINTL
        adc #1
        sta POINTL
        lda POINTH
        adc #0          ; carry from 99 -> 00 in the low byte
        sta POINTH
        cld             ; SCANDS must run in binary mode
@release:
        jsr SCANDS
        bne @release
        jmp @wait
