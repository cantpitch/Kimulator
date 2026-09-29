; Problem 27 - Calculator entry
; Start with 0000 00 on the display. Each hex key (0-F) shifts the six digits
; one place left and appears at the right, like a calculator. Ignore other keys.
; One key press must give exactly one digit, however long it is held.

        .org $0200
start:  lda #0
        sta POINTH
        sta POINTL
        sta INH
@wait:  jsr SCANDS      ; show the digits; returns Z = 1 while no key is down
        beq @wait
        jsr GETKEY      ; A = the key's code: 0-F, $10-$14 for AD DA + GO PC
        cmp #$10
        bcs @release    ; not a hex key (or $15: released too soon)
        ldx #4          ; shift POINTH:POINTL:INH left by one digit (4 bits)
@shift: asl INH
        rol POINTL
        rol POINTH
        dex
        bne @shift
        ora INH         ; the new digit goes into the low 4 bits, now 0
        sta INH
@release:
        jsr SCANDS      ; keep the display lit until the key is let go
        bne @release
        jmp @wait
