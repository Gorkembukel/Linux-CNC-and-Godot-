using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using CncTrainer.Sim;

namespace CncTrainer.Bridge;

/// <summary>
/// Owns the UDP sockets to ONE bridge/LinuxCNC instance, parses incoming
/// packets, and exposes the latest MachineState plus a connection-health flag.
/// Never blocks and never crashes the game if the bridge is down or restarts --
/// see brief section 2 ("Godot must not freeze... show connection state and
/// reconnect automatically"). UDP is connectionless, so "reconnect" here just
/// means: keep listening, and flip IsBridgeConnected back to true the moment packets
/// resume; nothing needs to be torn down and rebuilt on the Godot side.
///
/// Plain instantiable node, NOT a Godot autoload singleton -- running a
/// second live machine (Phase B's mill) alongside this one means a second
/// BridgeClient instance with its own ports/endpoint file, which an autoload
/// (always exactly one, always at /root/BridgeClient) can't support. Callers
/// get their reference injected (see Main.cs's Configure calls on
/// OperatorPanel/ConsoleStand/DebugOverlay) instead of looking it up by a
/// fixed absolute path.
/// </summary>
public partial class BridgeClient : Node
{
    /// <summary>UDP port the bridge sends this machine's state packets to.</summary>
    [Export] public int StatePort = 9200;
    /// <summary>UDP port the bridge listens on for this machine's commands.</summary>
    [Export] public int CommandPort = 9201;
    /// <summary>Where commands go when the endpoint file does not say otherwise.</summary>
    [Export] public string CommandHost = "127.0.0.1";
    /// <summary>Without a state packet for this long the bridge counts as disconnected (s).</summary>
    [Export] public double ConnectionTimeoutSeconds = 1.0;
    /// <summary>File the bridge writes its reachable address into when it starts.</summary>
    [Export] public string BridgeEndpointResourcePath = "res://bridge/.bridge_endpoint";

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

        // WSL2's documented "localhost forwarding" (Windows -> 127.0.0.1 reaches
        // a socket bound inside the VM) does NOT work for UDP on this machine --
        // confirmed live with a raw socket test: packets to 127.0.0.1:9201 were
        // never delivered, while the VM's real eth0 address worked immediately.
        // So the bridge writes its own address to this file on every start (see
        // bridge/main.py / bridge/net.py's discover_own_ip), and this is read
        // here instead of trusting the CommandHost default. If the file is
        // missing or unreadable, falls back to the [Export] CommandHost as
        // configured (useful for a non-WSL/Linux-native dev setup where
        // 127.0.0.1 might actually be correct).
        string endpointPath = ProjectSettings.GlobalizePath(BridgeEndpointResourcePath);
        if (System.IO.File.Exists(endpointPath))
        {
            string ip = System.IO.File.ReadAllText(endpointPath).Trim();
            if (ip.Length > 0)
            {
                GD.Print($"[BridgeClient] using bridge-reported command host: {ip} (from {endpointPath})");
                CommandHost = ip;
            }
        }
        else
        {
            GD.Print($"[BridgeClient] no endpoint file at {endpointPath}, using configured CommandHost={CommandHost}");
        }

        _commandSocket = new PacketPeerUdp();
        _commandSocket.ConnectToHost(CommandHost, CommandPort);

        IsBridgeConnected = false;
    }

    public override void _Process(double delta)
    {
        SubscribeToStateStream(delta);
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

    // The bridge sends its state stream back to the source address of this
    // packet. Sent from the state socket itself, so a stateful firewall
    // treats the incoming stream as replies and needs no inbound rule.
    private void SubscribeToStateStream(double delta)
    {
        _subscribeCountdown -= delta;
        if (_subscribeCountdown > 0)
            return;
        _subscribeCountdown = 1.0;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        new PacketHeader((byte)PacketType.Subscribe, 0, DateTime.UtcNow.Ticks).Write(w);
        _stateSocket.SetDestAddress(CommandHost, CommandPort);
        _stateSocket.PutPacket(ms.ToArray());
    }

    private double _subscribeCountdown;

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
                        ["start_x"] = seg.Start.X,
                        ["start_z"] = seg.Start.Z,
                        ["end_x"] = seg.End.X,
                        ["end_z"] = seg.End.Z,
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
    public void SetRapidOverride(float value) => SendCommand(CommandKind.SetRapidOverride, CommandPacket.ArgsF32(value));
    public void SetBlockDelete(bool on) => SendCommand(CommandKind.SetBlockDelete, CommandPacket.ArgsU8((byte)(on ? 1 : 0)));
    public void SetOptionalStop(bool on) => SendCommand(CommandKind.SetOptionalStop, CommandPacket.ArgsU8((byte)(on ? 1 : 0)));
    public void SetCoolant(bool on) => SendCommand(CommandKind.CoolantFlood, CommandPacket.ArgsU8((byte)(on ? 1 : 0)));
    public void SpindleOn(float rpm) => SendCommand(CommandKind.SpindleOn, CommandPacket.ArgsF32(rpm));
    public void SpindleOff() => SendCommand(CommandKind.SpindleOff);
    public void ConfirmToolChange() => SendCommand(CommandKind.ToolChangeConfirm);
    public void HomeAxis(byte axis) => SendCommand(CommandKind.HomeAxis, CommandPacket.ArgsU8(axis)); // 0xFF = all
    public void ResetEstop() => SendCommand(CommandKind.EstopReset);
    public void MachineOn() => SendCommand(CommandKind.MachineOn);
    public void MachineOff() => SendCommand(CommandKind.MachineOff);
    /// <summary>The machine forgets where it is: every axis must be homed again.</summary>
    public void Unhome() => SendCommand(CommandKind.Unhome);
}
