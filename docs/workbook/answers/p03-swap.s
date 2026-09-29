; Problem 3 - Swap
; Exchange the bytes at $10 and $11 without using the accumulator.

        .org $0200
start:  ldx $10         ; X = old $10
        ldy $11         ; Y = old $11
        stx $11         ; each register holds one value, so no
        sty $10         ; temporary memory location is needed
        brk
