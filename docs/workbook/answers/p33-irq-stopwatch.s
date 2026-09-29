; Problem 33 - Interrupt-driven stopwatch
; Show  MMSS hh  (minutes, seconds, hundredths). Any key starts and stops it.
; A timer interrupt every 10 ms does the counting, so the display loop
; can take as long as it likes.
; Needs the 6530-003 timer's IRQ output (PB7) wired to the CPU's IRQ line:
; Kimulator: Machine > Timer IRQ jumper.

        .org $0200
TIMER64I = $170E        ; write: start the /64 timer with its interrupt enabled
TICK     = 155          ; the interrupt comes (155 + 1) x 64 us = 9.984 ms later
RUNNING  = $10          ; 0 = stopped, 1 = running

start:  sei             ; no interrupts while we set things up
        lda #<ISR       ; the monitor's IRQ handler jumps through IRQV ($17FE)
        sta IRQV
        lda #>ISR
        sta IRQV+1
        lda #0
        sta POINTH
        sta POINTL
        sta INH
        sta RUNNING
        lda #TICK
        sta TIMER64I
        cli             ; interrupts on

@wait:  jsr SCANDS
        beq @wait       ; no key down
        lda RUNNING
        eor #1          ; start <-> stop
        sta RUNNING
@release:
        jsr SCANDS
        bne @release
        jmp @wait

; Runs every 10 ms, in the middle of whatever the main loop is doing.
; The CPU has already saved PC and P; we must save any register we change.
ISR:    pha
        lda #TICK
        sta TIMER64I    ; restart the timer; writing it also clears the interrupt
        lda RUNNING
        beq @out
        sed             ; RTI restores the main program's D flag
        clc
        lda INH
        adc #1          ; hundredths
        sta INH
        bcc @out        ; no carry: 00-99 still
        lda POINTL
        adc #0          ; carry is set: one more second
        cmp #$60
        bne @sec
        lda #0
@sec:   sta POINTL
        bne @out        ; not a new minute
        clc
        lda POINTH
        adc #1
        sta POINTH
@out:   pla
        rti
