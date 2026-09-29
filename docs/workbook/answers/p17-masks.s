; Problem 17 - Masks
; From the byte at $10 compute:
;   $11 = its low nibble                   (AND)
;   $12 = it with bits 7 and 0 set         (ORA)
;   $13 = it with every bit inverted       (EOR)
;   $14 = 1 if bit 6 of it is set, else 0  (BIT)

        .org $0200
start:  lda $10
        and #%00001111  ; keep only bits 0-3
        sta $11
        lda $10
        ora #%10000001  ; force bits 7 and 0 on
        sta $12
        lda $10
        eor #%11111111  ; flip every bit
        sta $13

        ldx #0
        bit $10         ; BIT copies bit 7 to N and bit 6 to V
        bvc @clear      ; (and leaves A alone)
        inx
@clear: stx $14
        brk
