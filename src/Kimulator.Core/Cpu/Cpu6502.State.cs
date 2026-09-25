namespace Kimulator.Core.Cpu;

public sealed partial class Cpu6502
{
    /// <summary>Writes registers and internal interrupt state. Only valid between <see cref="Step"/> calls.</summary>
    public void SaveState(BinaryWriter writer)
    {
        writer.Write(A);
        writer.Write(X);
        writer.Write(Y);
        writer.Write(S);
        writer.Write(_p);
        writer.Write(PC);
        writer.Write(Cycles);
        writer.Write(Jammed);
        writer.Write(NmiLine);
        writer.Write(IrqLine);
        writer.Write(_prevNmiLine);
        writer.Write(_nmiPending);
        writer.Write(_prevNmiPending);
        writer.Write(_irqPending);
        writer.Write(_prevIrqPending);
        writer.Write(_interruptQueued);
    }

    public void LoadState(BinaryReader reader)
    {
        A = reader.ReadByte();
        X = reader.ReadByte();
        Y = reader.ReadByte();
        S = reader.ReadByte();
        _p = reader.ReadByte();
        PC = reader.ReadUInt16();
        Cycles = reader.ReadInt64();
        Jammed = reader.ReadBoolean();
        NmiLine = reader.ReadBoolean();
        IrqLine = reader.ReadBoolean();
        _prevNmiLine = reader.ReadBoolean();
        _nmiPending = reader.ReadBoolean();
        _prevNmiPending = reader.ReadBoolean();
        _irqPending = reader.ReadBoolean();
        _prevIrqPending = reader.ReadBoolean();
        _interruptQueued = reader.ReadBoolean();
    }
}
