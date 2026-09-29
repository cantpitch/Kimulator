; Problem 4 - Neighbours
; N is at $10. Store N+1 at $11 and N-1 at $12.
; Load N only once, into A, then use transfers and INX/DEY.

        .org $0200
start:  lda $10         ; A = N
        tax             ; X = N
        tay             ; Y = N
        inx             ; X = N+1   (wraps from $FF to $00)
        dey             ; Y = N-1   (wraps from $00 to $FF)
        stx $11
        sty $12
        brk
