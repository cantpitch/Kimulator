; Problem 32 - Egg timer
; Count down from the minutes and seconds in MINS and SECS (BCD),
; showing  MMSS 00 . At 0000 stop and show  0000 EE .
; Keep time by polling the 6530-003 interval timer between display scans.

        .org $0200
TIMER1024 = $1707       ; write: start the timer, one count per 1024 cycles
TIMEOUT   = $1707       ; read: bit 7 set when the timer has run out
QUARTER   = 243         ; the flag rises after (243 + 1) x 1024 us = 0.2499 s
QUARTERS  = $10         ; quarter seconds left in this second

start:  lda MINS
        sta POINTH
        lda SECS
        sta POINTL
        lda #0
        sta INH
        lda #4
        sta QUARTERS
        lda #QUARTER
        sta TIMER1024

@tick:  jsr SCANDS      ; about 4 ms
        bit TIMEOUT
        bpl @tick
        lda #QUARTER    ; restart at once, so the time the rest of the
        sta TIMER1024   ; loop takes doesn't add up
        dec QUARTERS
        bne @tick
        lda #4
        sta QUARTERS

        sed             ; one second less, in BCD
        sec
        lda POINTL
        sbc #1
        bcs @store      ; no borrow: seconds were 01-59
        lda #$59        ; 00 -> 59 and one minute less
        sta POINTL
        lda POINTH
        sbc #0          ; carry is still clear, so this subtracts 1
        sta POINTH
        jmp @check
@store: sta POINTL
@check: cld
        lda POINTL
        ora POINTH
        bne @tick       ; not 0000 yet

        lda #$EE        ; done
        sta INH
@done:  jsr SCANDS
        jmp @done

MINS:   .byte $03       ; change these to set the time
SECS:   .byte $00
