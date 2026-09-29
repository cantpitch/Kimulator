; Problem 23 - Decimal addition
; $10/$11 and $12/$13 hold 4-digit BCD numbers, low byte first
; (so 1234 is $34, $12). Store their sum in $14/$15 and the
; carry (the ten-thousands digit) in $16 as 0 or 1.

        .org $0200
start:  sed             ; decimal mode: ADC and SBC work in BCD
        clc
        lda $10
        adc $12
        sta $14
        lda $11
        adc $13
        sta $15
        cld             ; always leave decimal mode when done!
        lda #0
        rol a           ; the final carry into bit 0
        sta $16
        brk
