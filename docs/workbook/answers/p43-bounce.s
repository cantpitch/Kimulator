; Problem 43 - Bouncing ball
; Move an 8 x 8 ball around the screen, 2 dots per step, 50 steps a second,
; bouncing off the edges. Draw it with EOR: drawing it a second time in the
; same place erases it and restores whatever was underneath.
;
; At x, the ball's 8 dots start at bit 7 - (x mod 8) of byte x / 8, so each
; row of the shape is shifted right by (x mod 8) across two bytes.

        .org $0200
SCREEN  = $2000
TIMER1024 = $1707       ; 6530-003: write starts the /1024 timer
TIMEOUT   = $1707       ; read: bit 7 = timer ran out
FRAMETIME = 19          ; (19 + 1) x 1024 us = 20.5 ms
MAXX    = 320 - 8
MAXY    = 200 - 8
BX      = $30           ; 16-bit x of the ball's left edge, even
BY      = $32           ; y of its top line, even
DX      = $33           ; 16 bits: +2 or -2
DY      = $35           ; +2 or -2
ADDR    = $36           ; 16 bits
SHIFT   = $38           ; x mod 8
LEFT    = $39           ; a shifted shape row: left byte...
RIGHT   = $3A           ; ...and right byte

start:  lda #100
        sta BX
        lda #0
        sta BX+1
        sta DX+1
        lda #50
        sta BY
        lda #2
        sta DX
        sta DY
        jsr DRAW
        lda #FRAMETIME
        sta TIMER1024

FRAME:  bit TIMEOUT     ; wait for the next 1/50 s
        bpl FRAME
        lda #FRAMETIME
        sta TIMER1024
        jsr DRAW        ; erase the ball (EOR it again)

        clc             ; x = x + dx
        lda BX
        adc DX
        sta BX
        lda BX+1
        adc DX+1
        sta BX+1
        clc             ; y = y + dy
        lda BY
        adc DY
        sta BY

        lda BX          ; at the left edge? go right
        ora BX+1
        bne @notleft
        lda #2
        sta DX
        lda #0
        sta DX+1
@notleft:
        lda BX          ; at the right edge? go left
        cmp #<MAXX
        bne @notright
        lda BX+1
        cmp #>MAXX
        bne @notright
        lda #<-2
        sta DX
        lda #>-2
        sta DX+1
@notright:
        lda BY          ; at the top? go down
        bne @nottop
        lda #2
        sta DY
@nottop:
        lda BY          ; at the bottom? go up
        cmp #MAXY
        bne @notbottom
        lda #<-2
        sta DY
@notbottom:
        jsr DRAW        ; draw it in its new place
        jmp FRAME

; EOR the ball onto the screen at (BX, BY).
DRAW:   lda #0          ; ADDR = SCREEN + BY x 40 + BX / 8, as in problem 39
        sta ADDR+1
        lda BY
        asl a
        rol ADDR+1
        asl a
        rol ADDR+1
        clc
        adc BY
        bcc @x5
        inc ADDR+1
@x5:    asl a
        rol ADDR+1
        asl a
        rol ADDR+1
        asl a
        rol ADDR+1
        sta ADDR
        lda BX+1
        lsr a
        lda BX
        ror a
        lsr a
        lsr a
        clc
        adc ADDR
        sta ADDR
        lda ADDR+1
        adc #>SCREEN
        sta ADDR+1

        lda BX
        and #7
        sta SHIFT
        ldx #0          ; X = row of the shape
@row:   lda SHAPE,x
        sta LEFT
        lda #0
        sta RIGHT
        ldy SHIFT
        beq @shifted
@shift: lsr LEFT        ; shift the 16 bits LEFT:RIGHT right by one
        ror RIGHT
        dey
        bne @shift
@shifted:
        ldy #0          ; Y = 0 here either way
        lda (ADDR),y
        eor LEFT
        sta (ADDR),y
        iny
        lda (ADDR),y
        eor RIGHT
        sta (ADDR),y
        clc             ; next dot line
        lda ADDR
        adc #40
        sta ADDR
        bcc @same
        inc ADDR+1
@same:  inx
        cpx #8
        bne @row
        rts

SHAPE:  .byte %00111100
        .byte %01111110
        .byte %11111111
        .byte %11111111
        .byte %11111111
        .byte %11111111
        .byte %01111110
        .byte %00111100
