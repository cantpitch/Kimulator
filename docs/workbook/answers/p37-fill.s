; Problem 37 - Paint the screen
; Fill all 8K of the Visible Memory ($2000-$3FFF) with the byte at $10.
; $00 clears the screen, $FF makes it white, $AA draws thin vertical stripes.

        .org $0200
SCREEN  = $2000
PTR     = $20           ; pointer to the page being filled

start:  lda #<SCREEN    ; always 0: we fill whole pages
        sta PTR
        lda #>SCREEN
        sta PTR+1
        ldx #32         ; 32 pages of 256 bytes = 8K
        lda $10
        ldy #0
@page:  sta (PTR),y     ; fill one page, Y = 0, 1, ..., 255
        iny
        bne @page
        inc PTR+1       ; next page
        dex
        bne @page
        brk
