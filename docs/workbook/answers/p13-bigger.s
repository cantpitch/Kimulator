; Problem 13 - The bigger of two
; Store the larger of the unsigned bytes at $10 and $11 in $12.

        .org $0200
start:  lda $10
        cmp $11         ; compare A with $11: C = 1 when A >= $11
        bcs @done       ; $10 is already the larger (or equal)
        lda $11         ; otherwise $11 is larger
@done:  sta $12
        brk
