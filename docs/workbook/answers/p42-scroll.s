; Problem 42 - Scroll up
; Move the whole picture up by one text row (8 dot lines) and clear the
; bottom row, the way a terminal scrolls.
;
; Row 1 starts at SCREEN + 320 = $2140. Moving $2140-$3F3F down to
; $2000-$3DFF is 7680 bytes = exactly 30 pages. The destination is below
; the source, so copying from the bottom address up never overwrites a
; byte before it has been copied. The bottom row, $3E00-$3F3F, is 320
; bytes: one page and 64 more.

        .org $0200
SCREEN  = $2000
SRC     = $20
DST     = $22

start:  lda #<(SCREEN+320)
        sta SRC
        lda #>(SCREEN+320)
        sta SRC+1
        lda #<SCREEN
        sta DST
        lda #>SCREEN
        sta DST+1
        ldx #30         ; pages to move
        ldy #0
@copy:  lda (SRC),y
        sta (DST),y
        iny
        bne @copy
        inc SRC+1       ; both pointers move on a page
        inc DST+1
        dex
        bne @copy

        lda #0          ; DST is now $3E00: clear 256 + 64 bytes
@clear: sta (DST),y     ; Y is 0 again here
        iny
        bne @clear
        inc DST+1
@clear2:
        sta (DST),y
        iny
        cpy #64
        bne @clear2
        brk
