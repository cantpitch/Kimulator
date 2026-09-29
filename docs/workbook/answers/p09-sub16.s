; Problem 9 - 16-bit subtraction
; ($15:$14) = ($11:$10) - ($13:$12)

        .org $0200
start:  sec
        lda $10         ; low bytes first
        sbc $12
        sta $14
        lda $11         ; the borrow (carry clear) flows into the high byte
        sbc $13
        sta $15
        brk
