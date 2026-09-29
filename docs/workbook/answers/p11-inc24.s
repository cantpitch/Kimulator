; Problem 11 - Increment a 24-bit number
; Add 1 to the 24-bit number at $10 (low), $11, $12 (high),
; without any branch instructions.

        .org $0200
start:  clc
        lda $10
        adc #1          ; add 1 to the low byte...
        sta $10
        lda $11
        adc #0          ; ...and only the carry to the others:
        sta $11         ; it is 1 exactly when the byte below
        lda $12         ; wrapped from $FF to $00
        adc #0
        sta $12
        brk
