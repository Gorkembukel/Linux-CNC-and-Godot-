using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using CncTrainer.Sim;

namespace CncTrainer.Bridge;

/// <summary>
/// Autoload singleton: owns the UDP sockets to the bridge, parses incoming
/// packets, and exposes the latest MachineState plus a connection-health flag.
/// Never blocks and never crashes the game if the bridge is down or restarts --
/// see brief section 2 ("Godot must not freeze... show connection state and
/// reconnect automatically"). UDP is connectionless, so "reconnect" here just
/// means: keep listening, and flip IsBridgeConnected back to true the moment packets
/// resume; nothing needs to be torn down and rebuilt on the Godot side.
/// </summary>
public partial class BridgeClient : Node
{
    [Export] public int StatePort = 9200;
    [Export] public int CommandPort = 9201;
    [Export] public string CommandHost = "127.0.0.1";
    [Export] public double ConnectionTimeoutSeconds = 1.0;

    public MachineState State { get; private set; } = new();
    public PredictionModel Prediction { get; private set; } = new();
    public bool IsBridgeConnected { get; private set; }
    public double LastPacketAgeSeconds { get; private set; }

    /// <summary>Last command round-trip time (send -> its ack), in ms. This is a
    /// real, locally-measured latency figure; a true one-way bridge->Godot
    /// latency is NOT computable from packet timestamps alone because the
    /// bridge's monotonic clock (Linux/WSL2) and Godot's (Windows) are two
    /// different machines' clocks with no NTP-style sync assumed -- comparing
    /// them directly would silently produce a meaningless number. RTT/2 is
    /// offered as a rough one-way estimate only.</summary>
    public double LastCommandRttMs { get; private set; } = double.NaN;

    /// <summary>Jitter in the STATE stream's inter-arrival times: the standard
    /// deviation of (actual gap - expected gap) over the recent window, in ms.
    /// This only needs Godot's own clock, so it's always meaningful (unlike
    /// cross-machine latency).</summary>
    public double StateStreamJitterMs { get; private set; }

    [Signal] public delegate void StateUpdatedEventHandler();
    [Signal] public delegate void ConnectionChangedEventHandler(bool connected);
    [Signal] public delegate void EventReceivedEventHandler(int eventKind, string text);
    [Signal] public delegate void PreviewReceivedEventHandler(Godot.Collections.Array segments);

    private PacketPeerUdp _stateSocket;
    private PacketPeerUdp _commandSocket;
    private ulong _lastPacketMsec;
    private ulong _clientSeq;
    private ulong _ackSeqSeen;

    private int _previewTotal = -1;
    private readonly SortedDictionary<int, PreviewSegment[]> _previewChunks = new();

    private readonly Dictionary<ulong, ulong> _pendingCommandSendMsec = new();
    private ulong _lastStatePacketMsec;
    private double _expectedStateIntervalMsec = -1;
    private readonly Queue<double> _recentIntervalDeviations = new();
    private const int JitterWindow = 60;

    public override void _Ready()
    {
        _stateSocket = new PacketPeerUdp();
        var err = _stateSocket.Bind(StatePort, "0.0.0.0");
        if (err != Error.Ok)
            GD.PrintErr($"[BridgeClient] failed to bind state port {StatePort}: {err}");

        _commandSocket = new PacketPeerUdp();
        _commandSocket.ConnectToHost(CommandHost, CommandPort);

        IsBridgeConnected = false;
    }

    public override void _Process(double delta)
    {
        DrainStatePackets();

        double ageMs = Time.GetTicksMsec() - _lastPacketMsec;
        LastPacketAgeSeconds = _lastPacketMsec == 0 ? double.PositiveInfinity : ageMs / 1000.0;
        bool connectedNow = _lastPacketMsec != 0 && LastPacketAgeSeconds < ConnectionTimeoutSeconds;
        if (connectedNow != IsBridgeConnected)
        {
            IsBridgeConnected = connectedNow;
            EmitSignal(SignalName.ConnectionChanged, IsBridgeConnected);
        }

        Prediction.Update(Time.GetTicksUsec() / 1_000_000.0, delta);
    }

    private void DrainStatePackets()
    {
        int guard = 0;
        while (_stateSocket.GetAvailablePacketCount() > 0 && guard++ < 500)
        {
            byte[] packet = _stateSocket.GetPacket();
            try
            {
                HandlePacket(packet);
            }
            catch (Exception e)
            {
                GD.PrintErr($"[BridgeClient] malformed packet ignored: {e.Message}");
            }
        }
    }

    private void HandlePacket(byte[] packet)
    {
        using var ms = new MemoryStream(packet);
        using var r = new BinaryReader(ms);
        var header = PacketHeader.Read(r);
        _lastPacketMsec = Time.GetTicksMsec();

        switch ((PacketType)header.PacketType)
        {
            case PacketType.State:
                var sample = StateSample.Read(r);
                State.ApplyState(header, sample);
                Prediction.OnConfirmedSample(State.Position, State.Velocity, Time.GetTicksUsec() / 1_000_000.0);
                TrackStateJitter();
                EmitSignal(SignalName.StateUpdated);
                break;
            case PacketType.Event:
                byte eventKind = r.ReadByte();
                ushort textLen = r.ReadUInt16();
                string text = System.Text.Encoding.UTF8.GetString(r.ReadBytes(textLen));
                EmitSignal(SignalName.EventReceived, eventKind, text);
                break;
            case PacketType.Preview:
                HandlePreviewChunk(r);
                break;
            case PacketType.Ack:
                ulong ackedClientSeq = r.ReadUInt64();
                r.ReadByte(); // ok flag -- not yet surfaced in UI
                _ackSeqSeen = ackedClientSeq;
                if (_pendingCommandSendMsec.TryGetValue(ackedClientSeq, out ulong sentAt))
                {
                    LastCommandRttMs = Time.GetTicksMsec() - sentAt;
                    _pendingCommandSendMsec.Remove(ackedClientSeq);
                }
                break;
            default:
                break;
        }
    }

    private void HandlePreviewChunk(BinaryReader r)
    {
        int totalSegments = (int)r.ReadUInt32();
        int chunkIndex = (int)r.ReadUInt32();
        int count = (int)r.ReadUInt32();
        var segs = new PreviewSegment[count];
        for (int i = 0; i < count; i++)
            segs[i] = PreviewSegment.Read(r);

        if (_previewTotal != totalSegments)
        {
            _previewTotal = totalSegments;
            _previewChunks.Clear();
        }
        _previewChunks[chunkIndex] = segs;

        int collected = 0;
        foreach (var chunk in _previewChunks.Values)
            collected += chunk.Length;

        if (collected >= totalSegments && totalSegments > 0)
        {
            var all = new Godot.Collections.Array();
            foreach (var chunk in _previewChunks.Values)
                foreach (var seg in chunk)
                    all.Add(new Godot.Collections.Dictionary
                    {
                        ["motion_type"] = (int)seg.MotionType,
                        ["line_number"] = seg.LineNumber,
                        ["start_x"] = seg.StartX,
                        ["start_z"] = seg.StartZ,
                        ["end_x"] = seg.EndX,
                        ["end_z"] = seg.EndZ,
                        ["feed_rate"] = seg.FeedRate,
                    });
            EmitSignal(SignalName.PreviewReceived, all);
            _previewTotal = -1;
            _previewChunks.Clear();
        }
    }

    private void SendCommand(CommandKind kind, byte[] args = null)
    {
        _clientSeq++;
        byte[] packet = CommandPacket.Build(kind, _clientSeq, _clientSeq, args);
        _commandSocket.PutPacket(packet);
        _pendingCommandSendMsec[_clientSeq] = Time.GetTicksMsec();
        if (_pendingCommandSendMsec.Count > 64) // drop anything that never got an ack
        {
            foreach (var staleKey in new List<ulong>(_pendingCommandSendMsec.Keys))
                if (staleKey < _clientSeq - 32)
                    _pendingCommandSendMsec.Remove(staleKey);
        }
    }

    private void TrackStateJitter()
    {
        ulong now = Time.GetTicksMsec();
        if (_lastStatePacketMsec != 0)
        {
            double interval = now - _lastStatePacketMsec;
            if (_expectedStateIntervalMsec < 0)
                _expectedStateIntervalMsec = interval; // first observed interval seeds the estimate
            else
                _expectedStateIntervalMsec = _expectedStateIntervalMsec * 0.98 + interval * 0.02; // slow EMA

            _recentIntervalDeviations.Enqueue(interval - _expectedStateIntervalMsec);
            while (_recentIntervalDeviations.Count > JitterWindow)
                _recentIntervalDeviations.Dequeue();

            double mean = 0;
            foreach (var d in _recentIntervalDeviations) mean += d;
            mean /= _recentIntervalDeviations.Count;
            double variance = 0;
            foreach (var d in _recentIntervalDeviations) variance += (d - mean) * (d - mean);
            variance /= _recentIntervalDeviations.Count;
            StateStreamJitterMs = Math.Sqrt(variance);
        }
        _lastStatePacketMsec = now;
    }

    // -- public command API, one method per CommandKind --
    public void JogStart(byte axis, float velocity) => SendCommand(CommandKind.JogStart, CommandPacket.ArgsU8F32(axis, velocity));
    public void JogStop(byte axis) => SendCommand(CommandKind.JogStop, CommandPacket.ArgsU8(axis));
    public void JogIncremental(byte axis, float velocity, float increment) =>
        SendCommand(CommandKind.JogIncremental, CommandPacket.ArgsU8F32F32(axis, velocity, increment));
    public void SetMode(TaskMode mode) => SendCommand(CommandKind.SetMode, CommandPacket.ArgsU8((byte)mode));
    public void SendMdi(string line) => SendCommand(CommandKind.Mdi, CommandPacket.ArgsMdi(line));
    public void OpenProgram(string path) => SendCommand(CommandKind.ProgramOpen, CommandPacket.ArgsMdi(path));
    public void RunProgram(int startLine = 0) => SendCommand(CommandKind.ProgramRun, CommandPacket.ArgsI32(startLine));
    public void PauseProgram() => SendCommand(CommandKind.ProgramPause);
    public void StopProgram() => SendCommand(CommandKind.ProgramStop);
    public void StepProgram() => SendCommand(CommandKind.ProgramStep);
    public void SetFeedOverride(float value) => SendCommand(CommandKind.SetFeedOverride, CommandPacket.ArgsF32(value));
    public void SetSpindleOverride(float value) => SendCommand(CommandKind.SetSpindleOverride, CommandPacket.ArgsF32(value));
    public void SpindleOn(float rpm) => SendCommand(CommandKind.SpindleOn, CommandPacket.ArgsF32(rpm));
    public void SpindleOff() => SendCommand(CommandKind.SpindleOff);
    public void ConfirmToolChange() => SendCommand(CommandKind.ToolChangeConfirm);
    public void HomeAxis(byte axis) => SendCommand(CommandKind.HomeAxis, CommandPacket.ArgsU8(axis)); // 0xFF = all
    public void ResetEstop() => SendCommand(CommandKind.EstopReset);
    public void MachineOn() => SendCommand(CommandKind.MachineOn);
    public void MachineOff() => SendCommand(CommandKind.MachineOff);
}
