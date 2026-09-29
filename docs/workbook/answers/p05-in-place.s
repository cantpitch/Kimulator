; Problem 5 - Change it in place
; Add 2 to the byte at $10 and subtract 3 from the byte at $11
; without using A, X or Y.

        .org $0200
start:  inc $10         ; INC and DEC work directly on memory:
        inc $10         ; the CPU reads the byte, changes it and
        dec $11         ; writes it back in one instruction
        dec $11
        dec $11
        brk
