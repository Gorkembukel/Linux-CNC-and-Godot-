using System.Linq;
using Godot;
using CncTrainer.Bridge;
using CncTrainer.Scheduling;

namespace CncTrainer;

/// <summary>
/// Phase 1/5 debug overlay: predicted vs. confirmed position, an error-magnitude
/// graph, stream jitter / command RTT, and (Phase 5) frame time + per-priority
/// scheduler budgets/queue lengths/drops. Lives in the workshop's UI canvas
/// (Scenes/Main.tscn); gets its bridge/scheduler injected via Configure()
/// from whichever machine the workshop wires it to.
/// </summary>
public partial class DebugOverlay : Control
{
    [Export] public bool Enabled = true;

    private BridgeClient _bridge;
    private JobScheduler _scheduler;
    private Label _label;
    private Control _graph;

    public override void _Ready()
    {
        _label = new Label { Position = new Vector2(6, 6) };
        _label.AddThemeFontSizeOverride("font_size", 11);
        AddChild(_label);

        _graph = new Control
        {
            Position = new Vector2(6, 150),
            Size = new Vector2(230, 60),
        };
        _graph.Draw += DrawGraph;
        AddChild(_graph);

        Visible = Enabled;
    }

    /// <summary>Called by Main._Ready once this rig's BridgeClient exists --
    /// replaces the old self-lookup via the fixed "/root/BridgeClient"
    /// autoload path, which a second live machine (Phase B) can't support.
    /// Takes the scheduler directly (not a whole Main/LatheMachine
    /// reference) -- that's the only thing SchedulerReport actually needs,
    /// and the scheduler now lives on whichever machine scene owns it, not
    /// on the workshop's own root.</summary>
    public void Configure(MachineRig rig, JobScheduler scheduler)
    {
        _bridge = rig.Bridge;
        _scheduler = scheduler;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.F3 })
        {
            Enabled = !Enabled;
            Visible = Enabled;
        }
    }

    public override void _Process(double delta)
    {
        if (!Enabled || _bridge == null)
            return;

        var p = _bridge.Prediction;
        _label.Text =
            $"[F3] DEBUG OVERLAY\n" +
            $"confirmed  X={_bridge.State.Position.X,8:F3}  Z={_bridge.State.Position.Z,8:F3} mm\n" +
            $"predicted  X={p.LastPredictedPosition.X,8:F3}  Z={p.LastPredictedPosition.Z,8:F3} mm\n" +
            $"visual     X={p.VisualPosition.X,8:F3}  Z={p.VisualPosition.Z,8:F3} mm\n" +
            $"error      {p.LastErrorMagnitudeMm,8:F3} mm  {(p.LastCorrectionWasSnap ? "(SNAP)" : "(smoothed)")}\n" +
            $"state stream jitter   {_bridge.StateStreamJitterMs,6:F2} ms\n" +
            $"last command RTT      {(double.IsNaN(_bridge.LastCommandRttMs) ? "n/a" : $"{_bridge.LastCommandRttMs:F1} ms")}\n" +
            $"packet age            {_bridge.LastPacketAgeSeconds * 1000:F0} ms\n" +
            $"connected: {_bridge.IsBridgeConnected}\n" +
            SchedulerReport();
        _graph.QueueRedraw();
    }

    private string SchedulerReport()
    {
        var s = _scheduler;
        double fps = Engine.GetFramesPerSecond();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- scheduler --- fps={fps:F0}  frame={1000.0 / System.Math.Max(fps, 0.001):F2} ms");
        foreach (JobPriority p in new[] { JobPriority.High, JobPriority.Normal, JobPriority.Low })
        {
            sb.AppendLine($"{p,-8} used={s.LastUsedMs[p],5:F2}ms budget={s.BudgetMs[p],5:F2}ms " +
                          $"queue={s.QueueLengths[p],3} dropped={s.DroppedCount[p]}");
        }
        return sb.ToString();
    }

    private void DrawGraph()
    {
        var history = _bridge.Prediction.ErrorHistory;
        if (history.Count < 2)
            return;

        var size = _graph.Size;
        _graph.DrawRect(new Rect2(Vector2.Zero, size), new Color(0, 0, 0, 0.5f));

        double max = history.Max();
        if (max < 0.001) max = 0.001;

        var points = history.ToArray();
        float stepX = size.X / (PredictionModelCapacity - 1);
        int offset = PredictionModelCapacity - points.Length;

        for (int i = 1; i < points.Length; i++)
        {
            float x0 = (offset + i - 1) * stepX;
            float x1 = (offset + i) * stepX;
            float y0 = size.Y - (float)(points[i - 1] / max) * size.Y;
            float y1 = size.Y - (float)(points[i] / max) * size.Y;
            _graph.DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), new Color(0.2f, 1f, 0.3f), 1.5f);
        }

        float snapY = size.Y - (float)(_bridge.Prediction.SnapThresholdMm / max) * size.Y;
        if (snapY >= 0 && snapY <= size.Y)
            _graph.DrawLine(new Vector2(0, snapY), new Vector2(size.X, snapY), new Color(1f, 0.3f, 0.2f, 0.6f), 1f);
    }

    private const int PredictionModelCapacity = Sim.PredictionModel.ErrorHistoryCapacity;
}
