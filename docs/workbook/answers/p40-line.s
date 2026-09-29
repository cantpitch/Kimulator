; Problem 40 - Draw a line
; Write LINE: draw a straight line from (X0, Y0) to (X1, Y1), both ends
; included, in any direction. X is 16 bits (0-319), Y is 0-199.
; Demo: a star of lines through the middle of the screen.
;
; Bresenham's algorithm, in the form that works for every direction:
;   dx = |x1 - x0|,  sx = +1 or -1 (towards x1)
;   dy = -|y1 - y0|, sy = +1 or -1 (towards y1)
;   err = dx + dy
;   loop: plot (x0, y0)
;         if x0 = x1 and y0 = y1: done
;         e2 = 2 x err
;         if e2 >= dy: err = err + dy, x0 = x0 + sx
;         if e2 <= dx: err = err + dx, y0 = y0 + sy
; All the values fit easily in 16 signed bits, so a comparison is a
; subtraction followed by a look at the sign of the result.

        .org $0200
SCREEN  = $2000
XC      = $30           ; PLOT's inputs, as in problem 39
YC      = $32
ADDR    = $33
X0      = $40           ; 16 bits; LINE moves it to X1
Y0      = $42           ; LINE moves it to Y1
X1      = $43           ; 16 bits
Y1      = $45
DX      = $46           ; 16 bits, >= 0
DY      = $48           ; 16 bits, <= 0
SX      = $4A           ; 16 bits: $0001 or $FFFF
SY      = $4C           ; 8 bits: $01 or $FF
ERR     = $4D           ; 16 bits
E2      = $4F           ; 16 bits

start:  ldx #0          ; each ENDS entry: x0, y0, x1, y1 (x as words)
@next:  lda ENDS,x
        sta X0
        lda ENDS+1,x
        sta X0+1
        lda ENDS+2,x
        sta Y0
        lda ENDS+3,x
        sta X1
        lda ENDS+4,x
        sta X1+1
        lda ENDS+5,x
        sta Y1
        txa
        pha             ; LINE changes X
        jsr LINE
        pla
        clc
        adc #6
        tax
        cpx #ENDSLEN
        bne @next
        brk

ENDS:   .word 0
        .byte 0
        .word 319
        .byte 199
        .word 319
        .byte 0
        .word 0
        .byte 199
        .word 160
        .byte 0
        .word 160
        .byte 199
        .word 0
        .byte 100
        .word 319
        .byte 100
        .word 100
        .byte 20
        .word 220
        .byte 180
        .word 220
        .byte 20
        .word 100
        .byte 180
ENDSLEN = * - ENDS

LINE:   ldx #$00        ; dx = x1 - x0, sx = +1 ...
        sec
        lda X1
        sbc X0
        sta DX
        lda X1+1
        sbc X0+1
        sta DX+1
        bpl @dxpos
        sec             ; ... or, if that's negative, dx = -dx, sx = -1
        lda #0
        sbc DX
        sta DX
        lda #0
        sbc DX+1
        sta DX+1
        ldx #$FF
@dxpos: stx SX+1        ; sx = $0001 or $FFFF
        txa
        ora #$01
        sta SX

        ldx #$01        ; |y1 - y0| and sy
        sec
        lda Y1
        sbc Y0
        bcs @dypos      ; no borrow: y1 >= y0
        eor #$FF        ; negate: y0 - y1
        adc #1          ; (carry is clear here)
        ldx #$FF
@dypos: stx SY
        sta DY          ; dy = -|y1 - y0|
        sec
        lda #0
        sbc DY
        sta DY
        lda #0
        sbc #0
        sta DY+1

        clc             ; err = dx + dy
        lda DX
        adc DY
        sta ERR
        lda DX+1
        adc DY+1
        sta ERR+1

@loop:  lda X0          ; plot (x0, y0)
        sta XC
        lda X0+1
        sta XC+1
        lda Y0
        sta YC
        jsr PLOT

        lda X0          ; at the end?
        cmp X1
        bne @go
        lda X0+1
        cmp X1+1
        bne @go
        lda Y0
        cmp Y1
        bne @go
        rts

@go:    lda ERR         ; e2 = 2 x err
        asl a
        sta E2
        lda ERR+1
        rol a
        sta E2+1

        sec             ; e2 >= dy  <=>  e2 - dy >= 0
        lda E2
        sbc DY
        lda E2+1
        sbc DY+1
        bmi @noxstep
        clc             ; err = err + dy
        lda ERR
        adc DY
        sta ERR
        lda ERR+1
        adc DY+1
        sta ERR+1
        clc             ; x0 = x0 + sx
        lda X0
        adc SX
        sta X0
        lda X0+1
        adc SX+1
        sta X0+1

@noxstep:
        sec             ; e2 <= dx  <=>  dx - e2 >= 0
        lda DX
        sbc E2
        lda DX+1
        sbc E2+1
        bmi @loop
        clc             ; err = err + dx
        lda ERR
        adc DX
        sta ERR
        lda ERR+1
        adc DX+1
        sta ERR+1
        clc             ; y0 = y0 + sy
        lda Y0
        adc SY
        sta Y0
        jmp @loop

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
