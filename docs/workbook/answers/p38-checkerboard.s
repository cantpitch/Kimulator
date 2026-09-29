; Problem 38 - Checkerboard
; Cover the screen with 8 x 8 dot squares, alternately black and white,
; starting with black in the top-left corner.
; Each line is 40 bytes (320 dots); byte c of line y is white when
; (y / 8 + c) is odd, i.e. when bit 0 of (y / 8) XOR c is 1.

        .org $0200
SCREEN  = $2000
PTR     = $20           ; start of the current line
LINE    = $22           ; 0-199
ROWBIT  = $23           ; bit 0 of (LINE / 8)

start:  lda #<SCREEN
        sta PTR
        lda #>SCREEN
        sta PTR+1
        lda #0
        sta LINE

@line:  lda LINE
        lsr a           ; LINE / 8
        lsr a
        lsr a
        and #1
        sta ROWBIT
        ldy #0          ; Y = byte within the line, 0-39
@byte:  tya
        eor ROWBIT
        and #1
        tax
        lda PATTERN,x   ; 0 -> black byte, 1 -> white byte
        sta (PTR),y
        iny
        cpy #40
        bne @byte

        clc             ; PTR = PTR + 40: the next line
        lda PTR
        adc #40
        sta PTR
        bcc @same
        inc PTR+1
@same:  inc LINE
        lda LINE
        cmp #200
        bne @line
        brk

PATTERN: .byte $00, $FF
