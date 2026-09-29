; Problem 6 - Add two bytes
; $12 = $10 + $11  (8-bit, ignore any carry out)

        .org $0200
start:  clc             ; ADC always adds the carry too, so clear it first
        lda $10
        adc $11         ; A = $10 + $11 + C
        sta $12
        brk
