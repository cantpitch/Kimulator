; Problem 44 - Sketch pad
; A pen starts in the middle of a clear screen and leaves a trail of dots.
; Holding a key moves it one dot every 1/50 s:
;         9 = up
;   4 = left    6 = right
;         1 = down
; and 0 clears the screen. The pen stops at the edges.

        .org $0200
SCREEN  = $2000
TIMER1024 = $1707
TIMEOUT   = $1707
STEPTIME  = 19          ; (19 + 1) x 1024 us = 20.5 ms
XC      = $30           ; pen position, as for PLOT
YC      = $32
ADDR    = $33
PTR     = $20           ; for CLEAR

start:  lda #0
        sta PADD        ; keypad columns are inputs, for GETKEY
        lda #<160
        sta XC
        lda #>160
        sta XC+1
        lda #100
        sta YC
        jsr CLEAR

@step:  jsr PLOT        ; the pen leaves a dot wherever it is
        lda #STEPTIME
        sta TIMER1024
        jsr GETKEY
        cmp #0
        bne @up
        jsr CLEAR
        jmp @wait
@up:    cmp #9
        bne @down
        lda YC
        beq @wait       ; already at the top
        dec YC
        jmp @wait
@down:  cmp #1
        bne @left
        lda YC
        cmp #199
        beq @wait       ; already at the bottom
        inc YC
        jmp @wait
@left:  cmp #4
        bne @right
        lda XC
        ora XC+1
        beq @wait       ; x = 0
        lda XC          ; 16-bit decrement
        bne @nb
        dec XC+1
@nb:    dec XC
        jmp @wait
@right: cmp #6
        bne @wait
        lda XC
        cmp #<319
        bne @inc
        lda XC+1
        cmp #>319
        beq @wait       ; x = 319
@inc:   inc XC          ; 16-bit increment
        bne @wait
        inc XC+1
@wait:  bit TIMEOUT
        bpl @wait
        jmp @step

;>>> CLEAR (problem 37 with A = 0)
CLEAR:  lda #<SCREEN
        sta PTR
        lda #>SCREEN
        sta PTR+1
        ldx #32
        lda #0
        tay
@page:  sta (PTR),y
        iny
        bne @page
        inc PTR+1
        dex
        bne @page
        rts
;<<<

;>>> PLOT (problem 39)
PLOT:   lda #0
        sta ADDR+1
        lda YC
        asl a
        rol ADDR+1
        asl a
        rol ADDR+1
        clc
        adc YC
        bcc @x5
        inc ADDR+1
@x5:    asl a
        rol ADDR+1
        asl a
        rol ADDR+1
        asl a
        rol ADDR+1
        sta ADDR
        lda XC+1
        lsr a
        lda XC
        ror a
        lsr a
        lsr a
        clc
        adc ADDR
        sta ADDR
        lda ADDR+1
        adc #>SCREEN
        sta ADDR+1
        lda XC
        and #7
        tax
        lda BITS,x
        ldy #0
        ora (ADDR),y
        sta (ADDR),y
        rts

BITS:   .byte $80, $40, $20, $10, $08, $04, $02, $01
;<<<
