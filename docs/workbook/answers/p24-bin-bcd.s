; Problem 24 - Binary to decimal
; Convert the byte at $10 (0-255) to BCD:
; $11 = the hundreds digit (0-2), $12 = tens and units as packed BCD.
; Method: for each bit from the top, double the result and add the bit.
; In decimal mode, "double and add the bit" is ADC of the value to itself,
; with the bit in the carry.

        .org $0200
start:  lda $10
        sta $13         ; working copy we shift bits out of
        lda #0
        sta $11
        sta $12
        ldx #8
        sed
@loop:  asl $13         ; next bit (from the top) into C
        lda $12
        adc $12         ; tens and units: 2 x value + bit, in BCD
        sta $12
        lda $11
        adc $11         ; hundreds: 2 x value + carry out of the tens
        sta $11
        dex
        bne @loop
        cld
        brk
