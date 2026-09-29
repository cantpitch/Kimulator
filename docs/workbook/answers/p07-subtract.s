; Problem 7 - Subtract two bytes
; $12 = $10 - $11  (8-bit, ignore any borrow)

        .org $0200
start:  sec             ; for SBC the carry means "no borrow", so set it first
        lda $10
        sbc $11         ; A = $10 - $11 - (1 - C)
        sta $12
        brk
