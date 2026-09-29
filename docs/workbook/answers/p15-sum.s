; Problem 15 - Sum a table
; Add the 32 bytes at $0300-$031F. Store the 16-bit sum at $10 (low), $11 (high).

        .org $0200
start:  lda #0
        sta $10
        sta $11
        ldx #0
@loop:  clc
        lda $10
        adc $0300,x     ; add the next byte to the low byte of the sum
        sta $10
        bcc @next       ; no carry: high byte unchanged
        inc $11         ; carry: bump the high byte
@next:  inx
        cpx #32
        bne @loop
        brk
