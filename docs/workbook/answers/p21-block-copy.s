; Problem 21 - Block copy
; Copy COUNT bytes (1-255, at $14) from the address in $10/$11
; to the address in $12/$13. The blocks don't overlap.

        .org $0200
SRC     = $10
DST     = $12
COUNT   = $14
start:  ldy #0
@loop:  lda (SRC),y     ; indirect indexed: address = (word at SRC) + Y
        sta (DST),y
        iny
        cpy COUNT
        bne @loop
        brk
