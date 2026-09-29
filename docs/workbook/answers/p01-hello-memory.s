; Problem 1 - Hello, memory
; Store the value $42 in zero-page location $10.

        .org $0200
start:  lda #$42        ; '#' means immediate: A gets the number $42 itself
        sta $10         ; store A at address $0010 (zero page, 2-byte instruction)
        brk             ; back to the monitor
