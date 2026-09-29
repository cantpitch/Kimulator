; Problem 2 - Copy cat
; Copy the byte at $10 into $11, $12 and $13.

        .org $0200
start:  lda $10         ; no '#': load the byte stored AT address $10
        sta $11         ; storing doesn't change A,
        sta $12         ; so one load is enough
        sta $13
        brk
