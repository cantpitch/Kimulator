; Problem 31 - Read the keypad yourself
; Write READKEY: it returns the code of the key that is down in A
; (0-F, AD=$10, DA=$11, +=$12, GO=$13, PC=$14) or $15 if none.
; The main program shows the last key's code in the data digits.
;
; The keypad is a 3 x 7 matrix. Writing (row x 2) to SBD pulls one row low
; through the 74145 (outputs 0-2); a key that is down in that row pulls its
; column low on PA6 (first key of the row) down to PA0 (seventh key).
; Row 0: 0-6, row 1: 7-D, row 2: E F AD DA + GO PC.

        .org $0200
start:  lda #0
        sta POINTH
        sta POINTL
        sta INH
@loop:  jsr READKEY
        cmp #$15
        beq @show       ; nothing pressed: keep the old code
        sta INH
@show:  jsr SCANDS
        jmp @loop

READKEY:
        lda #$00
        sta PADD        ; PA0-PA7 are inputs
        lda #$1E
        sta PBDD        ; PB1-PB4 drive the 74145
        ldx #0          ; X = row
        ldy #0          ; Y = code of the first key in the row
@row:   txa
        asl a
        sta SBD         ; pull row X low
        lda SAD
        ora #$80        ; PA7 is the TTY input, not a key
        eor #$FF        ; now a 1 marks a pressed key
        bne @found
        tya
        clc
        adc #7          ; the next row starts 7 codes later
        tay
        inx
        cpx #3
        bne @row
        lda #$15        ; no key
        rts

@found: asl a           ; drop bit 7 (always 0 here)
@col:   asl a           ; next column into C: PA6 first, down to PA0
        bcs @done
        iny             ; not this one: the code is one higher
        bne @col        ; always taken
@done:  tya
        rts
