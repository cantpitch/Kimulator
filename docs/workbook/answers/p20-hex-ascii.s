; Problem 20 - Byte to hex text
; Convert the byte at $10 to two ASCII hex digits:
; $11 = the high digit, $12 = the low digit. E.g. $3C -> "3", "C" ($33, $43).
; Write a subroutine NIBBLE that turns 0-15 in A into its ASCII digit.

        .org $0200
start:  lda $10
        pha             ; keep the byte for the low digit
        lsr a           ; move the high nibble down
        lsr a
        lsr a
        lsr a
        jsr NIBBLE
        sta $11
        pla             ; the original byte again
        and #$0F
        jsr NIBBLE
        sta $12
        brk

; A = 0-15  ->  A = '0'-'9' or 'A'-'F'
NIBBLE: cmp #10
        bcc @digit      ; 0-9 (carry clear)
        adc #6          ; 10-15: carry is set, so this adds 7, the gap from '9'+1 to 'A'
@digit: adc #'0'        ; carry is clear on both paths
        rts
