; Problem 14 - Counting tables
; Fill $0300-$033F with $00, $01, ..., $3F
; and  $0340-$037F with $3F, $3E, ..., $00.

        .org $0200
start:  ldx #0
@up:    txa
        sta $0300,x     ; absolute,X: address $0300 + X
        inx
        cpx #$40        ; stop after X = $3F
        bne @up

        ldx #$3F        ; a second loop counting down:
        ldy #0          ; X is the value, Y the offset
@down:  txa
        sta $0340,y
        iny
        dex
        bpl @down       ; loop while X >= 0 (bit 7 clear): DEX of 0 gives $FF
        brk
