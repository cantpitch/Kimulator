; Problem 22 - SHOUT
; $10/$11 points to a zero-terminated ASCII string (under 256 bytes).
; Change its lowercase letters to uppercase in place and store its length in $12.

        .org $0200
STR     = $10
start:  ldy #0
@loop:  lda (STR),y
        beq @end        ; the terminating zero
        cmp #'a'
        bcc @next       ; below 'a'
        cmp #'z'+1
        bcs @next       ; above 'z'
        and #%11011111  ; lowercase and uppercase differ only in bit 5
        sta (STR),y
@next:  iny
        bne @loop
@end:   sty $12
        brk
