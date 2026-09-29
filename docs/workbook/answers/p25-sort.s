; Problem 25 - Sort
; $0300 holds a count N (2-255), followed by N unsigned bytes.
; Sort them into ascending order in place (bubble sort).

        .org $0200
LIST    = $0301         ; the first element
SWAPPED = $10
start:
@pass:  lda #0
        sta SWAPPED
        ldx #0
@inner: inx             ; compare elements X-1 and X (counting from 0)
        cpx $0300
        beq @endpass    ; X = N: every pair has been compared
        lda LIST-1,x
        cmp LIST,x
        bcc @inner      ; already in order (A < next)
        beq @inner      ; equal: leave them
        tay             ; swap them, using Y as the spare register
        lda LIST,x
        sta LIST-1,x
        tya
        sta LIST,x
        inc SWAPPED
        jmp @inner
@endpass:
        lda SWAPPED
        bne @pass       ; a pass with no swaps means the list is sorted
        brk
