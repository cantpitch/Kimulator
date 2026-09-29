; Problem 19 - Multiply
; ($13:$12) = $10 x $11, both unsigned 8-bit.
; Shift and add: for every 1 bit of the multiplier, add the
; multiplicand shifted to that bit's position.

        .org $0200
MCAND   = $14           ; 16-bit copy of the multiplicand we can shift
start:  lda $10
        sta MCAND
        lda #0
        sta MCAND+1
        sta $12
        sta $13
        lda $11         ; A = multiplier, used up one bit at a time
@loop:  lsr a           ; lowest bit of the multiplier into C
        tax             ; save the rest (the addition needs A)
        bcc @skip
        clc
        lda $12
        adc MCAND
        sta $12
        lda $13
        adc MCAND+1
        sta $13
@skip:  asl MCAND       ; double the multiplicand:
        rol MCAND+1     ; ASL the low byte, ROL its carry into the high byte
        txa             ; the multiplier's remaining bits
        bne @loop       ; stop when none are left
        brk
