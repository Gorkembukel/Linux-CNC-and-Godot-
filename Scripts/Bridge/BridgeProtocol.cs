using System;
using System.IO;
using System.Text;

namespace CncTrainer.Bridge;

// Mirrors bridge/protocol.py exactly -- see bridge/protocol.md for the spec.
// Keep the two in sync by hand; there is no shared schema compiler in this project.

public enum PacketType : byte
{
    State = 1,
    Event = 2,
    Preview = 3,
    ToolTable = 4,
    Command = 16,
    Ack = 17,
    Subscribe = 18,
}

public enum TaskMode : byte { Manual = 0, Auto = 1, Mdi = 2 }
public enum InterpState : byte { Idle = 0, Reading = 1, Paused = 2, Waiting = 3 }
public enum EventKind : byte { Alarm = 0, ProgramLoaded = 1, ToolChangeRequest = 2, Info = 3 }
public enum MotionType : byte { Rapid = 0, Linear = 1, ArcCw = 2, ArcCcw = 3 }

public enum CommandKind : byte
{
    JogStart = 0,
    JogStop = 1,
    JogIncremental = 2,
    SetMode = 3,
    Mdi = 4,
    ProgramOpen = 5,
    ProgramRun = 6,
    ProgramPause = 7,
    ProgramStop = 8,
    ProgramStep = 9,
    SetFeedOverride = 10,
    SetSpindleOverride = 11,
    SpindleOn = 12,
    SpindleOff = 13,
    ToolChangeConfirm = 14,
    HomeAxis = 15,
    EstopReset = 16,
    MachineOn = 17,
    MachineOff = 18,
    Unhome = 19,
    SetRapidOverride = 20,
    SetBlockDelete = 21,
    SetOptionalStop = 22,
    CoolantFlood = 23,
    ProbeInput = 24,
}

public readonly struct PacketHeader
{
    public const byte MagicByte = 0xC0;
    public const int Size = 1 + 1 + 8 + 8; // magic, packet_type, seq, t_mono_ns

    public byte PacketType { get; }
    public ulong Seq { get; }
    public long TMonoNs { get; }

    public PacketHeader(byte packetType, ulong seq, long tMonoNs)
    {
        PacketType = packetType;
        Seq = seq;
        TMonoNs = tMonoNs;
    }

    public static PacketHeader Read(BinaryReader r)
    {
        byte magic = r.ReadByte();
        if (magic != MagicByte)
            throw new InvalidDataException($"bad magic byte 0x{magic:x2}");
        byte packetType = r.ReadByte();
        ulong seq = r.ReadUInt64();
        long tMonoNs = r.ReadInt64();
        return new PacketHeader(packetType, seq, tMonoNs);
    }

    public void Write(BinaryWriter w)
    {
        w.Write(MagicByte);
        w.Write(PacketType);
        w.Write(Seq);
        w.Write(TMonoNs);
    }
}

public struct StateSample
{
    // The 6 conventional CNC axis letters -- same fixed capacity as
    // bridge/protocol.py's AXIS_CAPACITY. Fixed rather than variable-length
    // on the wire: no framing complexity, and a machine with a different
    // active-axis set needs zero protocol change (it just sets a different
    // ActiveAxisMask bit pattern over the same fixed slots).
    public const int AxisCapacity = 6;

    public AxisVector Position, Velocity; // indexed via AxisVector.Get(AxisId)
    public double FeedRate, SpindleRpm;
    public float FeedOverride, SpindleOverride;
    public int ToolNumber;
    public float ToolNoseRadius;
    public bool SpindleEnabled;
    public byte TaskModeRaw, InterpStateRaw, ExecState;
    public int MotionLine;
    public bool Estop, MachineOn, InCycle;
    public byte ActiveAxisMask, HomedMask;
    public bool ToolChangePending;
    public AxisVector WorkOffset;
    public byte MotionTypeRaw, Coolant;
    /// <summary>The active work system's own offset; G5xIndex 1 = G54 .. 9 = G59.3.</summary>
    public AxisVector G5xOffset;
    public byte G5xIndex;

    public bool IsHomed(AxisId axis) => (HomedMask & (1 << (int)axis)) != 0;
    public bool HasAxis(AxisId axis) => (ActiveAxisMask & (1 << (int)axis)) != 0;

    public static StateSample Read(BinaryReader r)
    {
        var position = ReadAxes(r);
        var velocity = ReadAxes(r);
        return new StateSample
        {
            Position = position,
            Velocity = velocity,
            FeedRate = r.ReadDouble(),
            SpindleRpm = r.ReadDouble(),
            FeedOverride = r.ReadSingle(),
            SpindleOverride = r.ReadSingle(),
            ToolNumber = r.ReadInt32(),
            ToolNoseRadius = r.ReadSingle(),
            SpindleEnabled = r.ReadByte() != 0,
            TaskModeRaw = r.ReadByte(),
            InterpStateRaw = r.ReadByte(),
            ExecState = r.ReadByte(),
            MotionLine = r.ReadInt32(),
            Estop = r.ReadByte() != 0,
            MachineOn = r.ReadByte() != 0,
            InCycle = r.ReadByte() != 0,
            ActiveAxisMask = r.ReadByte(),
            HomedMask = r.ReadByte(),
            ToolChangePending = r.ReadByte() != 0,
            WorkOffset = ReadAxes(r),
            MotionTypeRaw = r.ReadByte(),
            Coolant = r.ReadByte(),
            G5xOffset = ReadAxes(r),
            G5xIndex = r.ReadByte(),
        };
    }

    internal static AxisVector ReadAxes(BinaryReader r) => new(
        (float)r.ReadDouble(), (float)r.ReadDouble(), (float)r.ReadDouble(),
        (float)r.ReadDouble(), (float)r.ReadDouble(), (float)r.ReadDouble());
}

public struct PreviewSegment
{
    public MotionType MotionType;
    public int LineNumber;
    public AxisVector Start, End;
    public double FeedRate;

    public static PreviewSegment Read(BinaryReader r)
    {
        var motionType = (MotionType)r.ReadByte();
        var lineNumber = r.ReadInt32();
        var start = StateSample.ReadAxes(r);
        var end = StateSample.ReadAxes(r);
        return new PreviewSegment
        {
            MotionType = motionType,
            LineNumber = lineNumber,
            Start = start,
            End = end,
            FeedRate = r.ReadDouble(),
        };
    }
}

/// <summary>One tool of LinuxCNC's tool table (TOOL_TABLE packet).
/// Diameter is the nose radius on a lathe.</summary>
public readonly record struct ToolTableEntry(int Number, double X, double Z, double Diameter, int Orientation)
{
    public static ToolTableEntry[] ReadAll(BinaryReader r)
    {
        var entries = new ToolTableEntry[r.ReadUInt16()];
        for (int i = 0; i < entries.Length; i++)
            entries[i] = new ToolTableEntry(r.ReadInt32(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadInt32());
        return entries;
    }
}

public static class CommandPacket
{
    public static byte[] Build(CommandKind kind, ulong seq, ulong clientSeq, byte[] args = null)
    {
        args ??= Array.Empty<byte>();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        new PacketHeader((byte)PacketType.Command, seq, DateTime.UtcNow.Ticks).Write(w);
        w.Write((byte)kind);
        w.Write(clientSeq);
        w.Write(args);
        return ms.ToArray();
    }

    public static byte[] ArgsMdi(string line) => Encoding.UTF8.GetBytes(line);
    public static byte[] ArgsU8(byte v) => new[] { v };
    public static byte[] ArgsU8F32(byte v, float f) => Concat(new[] { v }, BitConverter.GetBytes(f));
    public static byte[] ArgsU8F32F32(byte v, float f1, float f2) =>
        Concat(new[] { v }, BitConverter.GetBytes(f1), BitConverter.GetBytes(f2));
    public static byte[] ArgsF32(float f) => BitConverter.GetBytes(f);
    public static byte[] ArgsI32(int v) => BitConverter.GetBytes(v);

    private static byte[] Concat(params byte[][] parts)
    {
        int len = 0;
        foreach (var p in parts) len += p.Length;
        var result = new byte[len];
        int offset = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, result, offset, p.Length);
            offset += p.Length;
        }
        return result;
    }
}
