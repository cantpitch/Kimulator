; Problem 29 - HELLO
; Show "HELLO" on the LEDs without the monitor's display routines.
; Port A of the 6530-002 drives segments a-g (PA0-PA6, 1 = lit);
; PB1-PB4 go to the 74145 decoder, whose outputs 4-9 select digits 1-6.
; So digit n (0-5, left to right) is selected by writing (n + 4) x 2 to SBD.

        .org $0200
start:  lda #$7F
        sta PADD        ; $1741: PA0-PA6 are outputs
        lda #$1E
        sta PBDD        ; $1743: PB1-PB4 are outputs
@frame: ldx #0          ; X = digit number
@digit: lda #0
        sta SAD         ; blank the segments while switching digits
        txa
        asl a           ; X x 2 ...
        adc #8          ; ... + 8 = (X + 4) x 2  (ASL left the carry clear)
        sta SBD         ; select digit X
        lda MSG,x
        sta SAD         ; light its segments
        ldy #100        ; keep it lit for about 0.5 ms
@delay: dey
        bne @delay
        inx
        cpx #6
        bne @digit
        jmp @frame

;               H    E    L    L    O   (blank)
MSG:    .byte $76, $79, $38, $38, $3F, $00
