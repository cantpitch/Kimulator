; Problem 26 - Say something
; Make the LEDs show  C0DE 42  until the reader presses ST or RS.
; SCANDS lights the six digits once from POINTH, POINTL and INH,
; so it has to be called over and over.

        .org $0200
start:  lda #$C0
        sta POINTH      ; $FB: the two leftmost digits
        lda #$DE
        sta POINTL      ; $FA: the next two
        lda #$42
        sta INH         ; $F9: the two data digits
@loop:  jsr SCANDS      ; one pass over all six digits (about 4 ms)
        jmp @loop
