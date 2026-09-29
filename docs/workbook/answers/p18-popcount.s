; Problem 18 - Count the ones
; Store the number of 1 bits in the byte at $10 in $11. Leave $10 unchanged.

        .org $0200
start:  lda $10
        ldx #0          ; X counts the ones
        cmp #0
@loop:  beq @done       ; no 1 bits left in A
        lsr a           ; shift bit 0 into the carry; Z tells if A is now 0
        bcc @loop
        inx             ; INX changes Z, so test A again
        cmp #0
        jmp @loop
@done:  stx $11
        brk
