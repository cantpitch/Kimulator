; Problem 12 - Negate a 16-bit number
; ($13:$12) = -($11:$10), in two's complement.
; Negating is subtracting from zero.

        .org $0200
start:  sec
        lda #0
        sbc $10         ; 0 - low byte
        sta $12
        lda #0
        sbc $11         ; 0 - high byte - borrow
        sta $13
        brk
