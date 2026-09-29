; Problem 41 - Hello, world
; Write PUTCH: draw the character in A in text cell (COL, ROW), where the
; screen is 40 columns x 25 rows of 8 x 8 dot cells, then advance COL.
; Write PRINT: draw the zero-terminated string at STR with PUTCH.
; Demo: "HELLO, WORLD!" in the middle of the screen, "KIM-1 6502" below it.
;
; Cell (COL, ROW) starts at byte SCREEN + ROW x 320 + COL, and its 8 dot
; lines are 40 bytes apart. ROW x 320 = ROW x 256 + ROW x 64.
; A full 8 x 8 font takes 768 bytes for ASCII 32-127, more than the KIM's
; free RAM, so this one only has the characters in CHARS.

        .org $0200
SCREEN  = $2000
STR     = $10           ; string pointer
SIDX    = $12           ; index into the string
COL     = $13
ROW     = $14
ADDR    = $15           ; 16 bits
LINES   = $17           ; dot lines left to draw

start:  lda #10
        sta ROW
        lda #13
        sta COL
        lda #<HELLO
        sta STR
        lda #>HELLO
        sta STR+1
        jsr PRINT
        lda #12
        sta ROW
        lda #15
        sta COL
        lda #<KIM
        sta STR
        lda #>KIM
        sta STR+1
        jsr PRINT
        brk

HELLO:  .byte "HELLO, WORLD!", 0
KIM:    .byte "KIM-1 6502", 0

PRINT:  lda #0
        sta SIDX
@loop:  ldy SIDX
        lda (STR),y
        beq @done
        jsr PUTCH
        inc SIDX
        bne @loop
@done:  rts

PUTCH:  ldx #NCHARS-1   ; find the character in CHARS
@find:  cmp CHARS,x
        beq @found
        dex
        bne @find       ; not found: X = 0, the space
@found: txa             ; X = index x 8: where its glyph starts in FONT
        asl a
        asl a
        asl a
        tax

        lda #0          ; ADDR = ROW x 64 ...
        sta ADDR+1
        lda ROW
        ldy #6
@x64:   asl a
        rol ADDR+1
        dey
        bne @x64
        clc             ; ... + COL
        adc COL
        sta ADDR
        lda ADDR+1      ; ... + ROW x 256 + SCREEN
        adc ROW
        adc #>SCREEN    ; (no carry: the sum is under $40)
        sta ADDR+1

        lda #8
        sta LINES
        ldy #0
@line:  lda FONT,x
        sta (ADDR),y
        inx
        clc             ; next dot line: 40 bytes on
        lda ADDR
        adc #40
        sta ADDR
        bcc @same
        inc ADDR+1
@same:  dec LINES
        bne @line
        inc COL
        rts

CHARS:  .byte " !,-01256DEHIKLMORW"
NCHARS  = * - CHARS
FONT:   .byte $00, $00, $00, $00, $00, $00, $00, $00   ; ' '
        .byte $10, $10, $10, $10, $10, $00, $10, $00   ; '!'
        .byte $00, $00, $00, $00, $00, $30, $10, $20   ; ','
        .byte $00, $00, $00, $7C, $00, $00, $00, $00   ; '-'
        .byte $38, $44, $4C, $54, $64, $44, $38, $00   ; '0'
        .byte $10, $30, $10, $10, $10, $10, $38, $00   ; '1'
        .byte $38, $44, $04, $08, $10, $20, $7C, $00   ; '2'
        .byte $7C, $40, $78, $04, $04, $44, $38, $00   ; '5'
        .byte $18, $20, $40, $78, $44, $44, $38, $00   ; '6'
        .byte $78, $44, $44, $44, $44, $44, $78, $00   ; 'D'
        .byte $7C, $40, $40, $78, $40, $40, $7C, $00   ; 'E'
        .byte $44, $44, $44, $7C, $44, $44, $44, $00   ; 'H'
        .byte $38, $10, $10, $10, $10, $10, $38, $00   ; 'I'
        .byte $44, $48, $50, $60, $50, $48, $44, $00   ; 'K'
        .byte $40, $40, $40, $40, $40, $40, $7C, $00   ; 'L'
        .byte $44, $6C, $54, $54, $44, $44, $44, $00   ; 'M'
        .byte $38, $44, $44, $44, $44, $44, $38, $00   ; 'O'
        .byte $78, $44, $44, $78, $50, $48, $44, $00   ; 'R'
        .byte $44, $44, $44, $54, $54, $54, $28, $00   ; 'W'
