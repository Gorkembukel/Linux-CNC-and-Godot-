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
    Command = 16,
    Ack = 17,
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
    public double PosX, PosZ, VelX, VelZ, FeedRate, SpindleRpm;
    public float FeedOverride, SpindleOverride;
    public int ToolNumber;
    public float ToolNoseRadius;
    public bool SpindleEnabled;
    public byte TaskModeRaw, InterpStateRaw, ExecState;
    public int MotionLine;
    public bool Estop, MachineOn, HomedX, HomedZ, InCycle;

    public static StateSample Read(BinaryReader r)
    {
        var s = new StateSample
        {
            PosX = r.ReadDouble(),
            PosZ = r.ReadDouble(),
            VelX = r.ReadDouble(),
            VelZ = r.ReadDouble(),
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
            HomedX = r.ReadByte() != 0,
            HomedZ = r.ReadByte() != 0,
            InCycle = r.ReadByte() != 0,
        };
        r.ReadByte(); // pad
        return s;
    }
}

public struct PreviewSegment
{
    public MotionType MotionType;
    public int LineNumber;
    public double StartX, StartZ, EndX, EndZ;
    public double FeedRate;

    public static PreviewSegment Read(BinaryReader r)
    {
        return new PreviewSegment
        {
            MotionType = (MotionType)r.ReadByte(),
            LineNumber = r.ReadInt32(),
            StartX = r.ReadDouble(),
            StartZ = r.ReadDouble(),
            EndX = r.ReadDouble(),
            EndZ = r.ReadDouble(),
            FeedRate = r.ReadDouble(),
        };
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
