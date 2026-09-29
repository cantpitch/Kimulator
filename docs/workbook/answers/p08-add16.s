; Problem 8 - 16-bit addition
; The 16-bit numbers are little-endian: low byte first.
; ($15:$14) = ($11:$10) + ($13:$12)

        .org $0200
start:  clc
        lda $10         ; low bytes first...
        adc $12
        sta $14
        lda $11         ; ...then the high bytes, adding the carry
        adc $13         ; that the low-byte addition left behind
        sta $15
        brk
