namespace Kimulator.Core.Cpu;

/// <summary>
/// Cycle-accurate NMOS 6502 (MOS 6502 / Rockwell R6502), including the undocumented opcodes
/// and NMOS decimal-mode behaviour.
/// <para>
/// Every bus access the real chip makes (including dummy reads and the RMW double write) is
/// issued through <see cref="ICpuBus"/> in the correct order, one access per clock cycle.
/// Interrupt lines are sampled at the end of every cycle and acted on using the value from the
/// second-to-last cycle of an instruction, matching the hardware's polling behaviour.
/// </para>
/// </summary>
public sealed partial class Cpu6502
{
    public const ushort NmiVector = 0xFFFA;
    public const ushort ResetVector = 0xFFFC;
    public const ushort IrqVector = 0xFFFE;

    private const byte FlagC = (byte)StatusFlags.Carry;
    private const byte FlagZ = (byte)StatusFlags.Zero;
    private const byte FlagI = (byte)StatusFlags.InterruptDisable;
    private const byte FlagD = (byte)StatusFlags.Decimal;
    private const byte FlagB = (byte)StatusFlags.Break;
    private const byte FlagU = (byte)StatusFlags.Unused;
    private const byte FlagV = (byte)StatusFlags.Overflow;
    private const byte FlagN = (byte)StatusFlags.Negative;

    private readonly ICpuBus _bus;

    // Interrupt polling state (sampled at the end of every cycle).
    private bool _prevNmiLine;
    private bool _nmiPending;
    private bool _prevNmiPending;
    private bool _irqPending;
    private bool _prevIrqPending;
    private bool _interruptQueued;

    public Cpu6502(ICpuBus bus)
    {
        _bus = bus;
        P = FlagU | FlagI;
    }

    public byte A { get; set; }
    public byte X { get; set; }
    public byte Y { get; set; }
    public byte S { get; set; }
    public ushort PC { get; set; }

    private byte _p;

    /// <summary>Processor status. Bit 5 always reads as 1; the B flag only exists on the stack.</summary>
    public byte P
    {
        get => _p;
        set => _p = (byte)((value | FlagU) & ~FlagB);
    }

    /// <summary>NMI input, true = asserted. Edge-triggered: an NMI is taken on the false→true transition.</summary>
    public bool NmiLine { get; set; }

    /// <summary>IRQ input, true = asserted. Level-triggered and masked by the I flag.</summary>
    public bool IrqLine { get; set; }

    /// <summary>True after executing a JAM/KIL opcode. Only <see cref="Reset"/> recovers.</summary>
    public bool Jammed { get; private set; }

    /// <summary>Total clock cycles executed since construction.</summary>
    public long Cycles { get; private set; }

    /// <summary>True when the next <see cref="Step"/> will run an interrupt sequence instead of an instruction.</summary>
    public bool InterruptPending => _interruptQueued;

    /// <summary>Runs the 7-cycle reset sequence and loads PC from the reset vector.</summary>
    public void Reset()
    {
        Jammed = false;
        ReadCycle(PC, sync: true);
        ReadCycle(PC);
        // The stack pushes happen as reads on NMOS parts.
        ReadCycle((ushort)(0x0100 | S)); S--;
        ReadCycle((ushort)(0x0100 | S)); S--;
        ReadCycle((ushort)(0x0100 | S)); S--;
        _p |= FlagI;
        byte lo = ReadCycle(ResetVector);
        byte hi = ReadCycle(ResetVector + 1);
        PC = (ushort)(lo | hi << 8);
        _nmiPending = _prevNmiPending = false;
        _irqPending = _prevIrqPending = false;
        _interruptQueued = false;
    }

    /// <summary>
    /// Executes one instruction, or one interrupt sequence if an interrupt was recognised at the end of
    /// the previous instruction. Returns the number of cycles consumed.
    /// </summary>
    public int Step()
    {
        long start = Cycles;
        if (Jammed)
        {
            // A jammed CPU keeps the bus busy with reads of $FFFF.
            ReadCycle(0xFFFF);
            return 1;
        }

        if (_interruptQueued)
        {
            _interruptQueued = false;
            InterruptSequence();
        }
        else
        {
            byte opcode = ReadCycle(PC, sync: true);
            PC++;
            Execute(opcode);
            _interruptQueued = !Jammed && (_prevIrqPending || _prevNmiPending);
        }

        return (int)(Cycles - start);
    }

    // ---------------------------------------------------------------- bus cycles

    private byte ReadCycle(ushort address, bool sync = false)
    {
        byte value = _bus.Read(address, sync);
        EndCycle();
        return value;
    }

    private void WriteCycle(ushort address, byte value)
    {
        _bus.Write(address, value);
        EndCycle();
    }

    private void EndCycle()
    {
        Cycles++;
        _prevNmiPending = _nmiPending;
        if (NmiLine && !_prevNmiLine)
            _nmiPending = true;
        _prevNmiLine = NmiLine;

        _prevIrqPending = _irqPending;
        _irqPending = IrqLine && (_p & FlagI) == 0;
    }

    private byte FetchOperand() => ReadCycle(PC++);

    private void DummyReadPc() => ReadCycle(PC);

    private void Push(byte value)
    {
        WriteCycle((ushort)(0x0100 | S), value);
        S--;
    }

    private byte Pull()
    {
        S++;
        return ReadCycle((ushort)(0x0100 | S));
    }

    // ---------------------------------------------------------------- interrupts

    private void InterruptSequence()
    {
        ReadCycle(PC, sync: true); // opcode fetch, discarded (BRK forced into IR)
        ReadCycle(PC);             // operand fetch, discarded, PC not incremented
        Push((byte)(PC >> 8));
        Push((byte)PC);
        EnterInterruptVector(breakFlag: false);
    }

    /// <summary>Pushes P and jumps through the NMI or IRQ/BRK vector. An NMI that is pending by now hijacks the vector.</summary>
    private void EnterInterruptVector(bool breakFlag)
    {
        ushort vector = IrqVector;
        if (_nmiPending)
        {
            _nmiPending = false;
            vector = NmiVector;
        }

        Push((byte)(_p | FlagU | (breakFlag ? FlagB : 0)));
        _p |= FlagI;
        byte lo = ReadCycle(vector);
        byte hi = ReadCycle((ushort)(vector + 1));
        PC = (ushort)(lo | hi << 8);
    }

    // ---------------------------------------------------------------- addressing modes

    private ushort AddrZeroPage() => FetchOperand();

    private ushort AddrZeroPageIndexed(byte index)
    {
        byte baseAddr = FetchOperand();
        ReadCycle(baseAddr); // dummy read while adding the index
        return (byte)(baseAddr + index);
    }

    private ushort AddrAbsolute()
    {
        byte lo = FetchOperand();
        byte hi = FetchOperand();
        return (ushort)(lo | hi << 8);
    }

    /// <summary>
    /// Absolute,X / Absolute,Y. The CPU first reads from the address with an un-carried high byte;
    /// for reads that is only needed when a page is crossed, stores and RMW always do it.
    /// </summary>
    private ushort AddrAbsoluteIndexed(byte index, bool alwaysDummyRead)
    {
        ushort baseAddr = AddrAbsolute();
        return AddIndexWithDummyRead(baseAddr, index, alwaysDummyRead);
    }

    private ushort AddrIndexedIndirect()
    {
        byte pointer = FetchOperand();
        ReadCycle(pointer); // dummy read while adding X
        pointer += X;
        byte lo = ReadCycle(pointer);
        byte hi = ReadCycle((byte)(pointer + 1));
        return (ushort)(lo | hi << 8);
    }

    private ushort AddrIndirectIndexed(bool alwaysDummyRead)
    {
        ushort baseAddr = ReadIndirectPointer();
        return AddIndexWithDummyRead(baseAddr, Y, alwaysDummyRead);
    }

    private ushort ReadIndirectPointer()
    {
        byte pointer = FetchOperand();
        byte lo = ReadCycle(pointer);
        byte hi = ReadCycle((byte)(pointer + 1));
        return (ushort)(lo | hi << 8);
    }

    private ushort AddIndexWithDummyRead(ushort baseAddr, byte index, bool alwaysDummyRead)
    {
        ushort address = (ushort)(baseAddr + index);
        bool pageCrossed = ((baseAddr ^ address) & 0xFF00) != 0;
        if (pageCrossed || alwaysDummyRead)
            ReadCycle((ushort)((baseAddr & 0xFF00) | (address & 0x00FF)));
        return address;
    }

    // ---------------------------------------------------------------- flag helpers

    private void SetNZ(byte value)
    {
        _p = (byte)((_p & ~(FlagN | FlagZ)) | (value & FlagN) | (value == 0 ? FlagZ : 0));
    }

    private void SetFlag(byte flag, bool on)
    {
        if (on) _p |= flag;
        else _p = (byte)(_p & ~flag);
    }

    private bool GetFlag(byte flag) => (_p & flag) != 0;
}
