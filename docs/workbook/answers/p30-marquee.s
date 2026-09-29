; Problem 30 - Marquee
; Scroll the message "HELLO 6502" (with a gap) across the display,
; one position every quarter second, forever.
; The 6530-003 interval timer keeps time while the program scans the digits.

        .org $0200
TIMER1024 = $1707       ; write: start the timer counting every 1024 cycles
TIMEOUT   = $1707       ; read: bit 7 is set once the timer has run out
QUARTER   = 243         ; the flag rises after (243 + 1) x 1024 us = 0.2499 s
POS     = $10           ; message position shown in the leftmost digit
COUNT   = $11           ; delay counter

start:  lda #$7F
        sta PADD
        lda #$1E
        sta PBDD
        lda #0
        sta POS
        lda #QUARTER
        sta TIMER1024

@frame: ldx #0          ; X = digit 0-5
        ldy POS         ; Y = message index for that digit
@digit: lda #0
        sta SAD
        txa
        asl a
        adc #8
        sta SBD
        lda MSG,y
        sta SAD
        lda #60         ; about 0.5 ms
        sta COUNT
@delay: dec COUNT
        bne @delay
        iny             ; next character, wrapping at the end of the message
        cpy #LEN
        bne @nowrap
        ldy #0
@nowrap:
        inx
        cpx #6
        bne @digit

        bit TIMEOUT     ; BIT puts bit 7 of the flag register in N
        bpl @frame      ; not time to move yet
        lda #QUARTER
        sta TIMER1024   ; start the next quarter second (also clears the flag)
        inc POS
        lda POS
        cmp #LEN
        bne @frame
        lda #0
        sta POS
        jmp @frame

;               H    E    L    L    O         6    5    0    2
MSG:    .byte $76, $79, $38, $38, $3F, $00, $7D, $6D, $3F, $5B, $00, $00
LEN     = * - MSG
