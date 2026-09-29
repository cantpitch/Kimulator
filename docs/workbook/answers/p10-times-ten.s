; Problem 10 - Times ten
; N (0-25) is at $10. Store 10 x N at $11 using only additions.
; 10N = 2N + 2N + 2N + 2N + 2N, and 2N is N added to itself.

        .org $0200
start:  lda $10
        clc
        adc $10         ; A = 2N
        sta $12         ; keep 2N in a scratch location
        clc
        adc $12         ; A = 4N
        clc
        adc $12         ; A = 6N
        clc
        adc $12         ; A = 8N
        clc
        adc $12         ; A = 10N  (at most 250, so no carry ever)
        sta $11
        brk
