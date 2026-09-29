; Problem 39 - Plot a dot
; Write PLOT: light the dot at (XC, YC), where XC is 16 bits (0-319, at
; $30/$31) and YC is 0-199 (at $32). Then use it to draw a frame around the
; edge of the screen and a diagonal from the top-left corner.
;
; The dot is in byte  SCREEN + YC x 40 + XC / 8,  at bit  7 - (XC mod 8).
; YC x 40 = (YC x 5) x 8, which needs 16 bits.

        .org $0200
SCREEN  = $2000
XC      = $30           ; 16-bit x
YC      = $32           ; y
ADDR    = $33           ; 16-bit address PLOT works out

start:  lda #0          ; top and bottom edges: x = 0-319
        sta XC
        sta XC+1
@hor:   lda #0
        sta YC
        jsr PLOT
        lda #199
        sta YC
        jsr PLOT
        inc XC          ; 16-bit increment
        bne @nocarry
        inc XC+1
@nocarry:
        lda XC+1        ; stop at x = 320 ($0140)
        cmp #>320
        bne @hor
        lda XC
        cmp #<320
        bne @hor

        lda #0          ; left and right edges: y = 0-199
        sta YC
@ver:   lda #0
        sta XC
        sta XC+1
        jsr PLOT
        lda #<319
        sta XC
        lda #>319
        sta XC+1
        jsr PLOT
        inc YC
        lda YC
        cmp #200
        bne @ver

        lda #0          ; diagonal: (0,0), (1,1), ..., (199,199)
        sta XC+1
        sta YC
@diag:  lda YC
        sta XC
        jsr PLOT
        inc YC
        lda YC
        cmp #200
        bne @diag
        brk

;>>> PLOT (problem 39)
; Light the dot at (XC, YC). Changes A, X and Y.
PLOT:   lda #0
        sta ADDR+1
        lda YC
        asl a           ; YC x 2 (the high bits go into ADDR+1 as we go)
        rol ADDR+1
        asl a           ; YC x 4
        rol ADDR+1
        clc
        adc YC          ; YC x 5
        bcc @x5
        inc ADDR+1
@x5:    asl a           ; YC x 10
        rol ADDR+1
        asl a           ; YC x 20
        rol ADDR+1
        asl a           ; YC x 40
        rol ADDR+1
        sta ADDR

        lda XC+1        ; XC / 8 (fits in a byte: at most 39)
        lsr a           ; bit 8 of XC into the carry...
        lda XC
        ror a           ; ...and from there into bit 7
        lsr a
        lsr a
        clc
        adc ADDR        ; ADDR = SCREEN + YC x 40 + XC / 8
        sta ADDR
        lda ADDR+1
        adc #>SCREEN
        sta ADDR+1

        lda XC
        and #7
        tax
        lda BITS,x      ; the dot's bit in its byte
        ldy #0
        ora (ADDR),y    ; set it, leaving the other 7 dots alone
        sta (ADDR),y
        rts

BITS:   .byte $80, $40, $20, $10, $08, $04, $02, $01
;<<<
