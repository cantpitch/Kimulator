; Problem 16 - Smallest and largest
; $0300 holds a count N (1-255), followed by N unsigned bytes.
; Store the smallest at $10 and the largest at $11.

        .org $0200
start:  lda $0301       ; the first element is both min and max so far
        sta $10
        sta $11
        ldx #1          ; X = number of elements examined
@loop:  cpx $0300
        beq @done       ; examined all N
        inx
        lda $0300,x     ; element X (they start at $0301)
        cmp $10
        bcs @notmin     ; A >= min
        sta $10
@notmin:
        cmp $11
        bcc @loop       ; A < max
        sta $11
        jmp @loop
@done:  brk
