using System.Linq;
using Godot;
using CncTrainer.Interaction;
using CncTrainer.Player;
using CncTrainer.Procedural;
using CncTrainer.Workshop;

namespace CncTrainer.Inspection;

/// <summary>
/// Quality control table: put a finished part on it and it is measured
/// against the open work order's drawing; the report card goes up on the
/// job board and on the HUD. The node's origin is on the floor.
/// </summary>
[Tool]
public partial class InspectionTable : Node3D, IInteractable
{
    private const float TopY = 0.9f;

    public override void _Ready()
    {
        BodyParts.Clear(this);
        BodyParts.SolidBox(this, "Plate", new Vector3(1.0f, 0.08f, 0.7f), new Vector3(0f, TopY - 0.04f, 0f), BodyParts.Mat(new Color(0.15f, 0.15f, 0.17f), 0.3f, 0.2f));
        BodyParts.SolidBox(this, "Stand", new Vector3(0.8f, TopY - 0.08f, 0.5f), new Vector3(0f, (TopY - 0.08f) / 2f, 0f), BodyParts.Mat(new Color(0.3f, 0.45f, 0.35f)));
        AddChild(new Label3D { Text = "KALİTE KONTROL", PixelSize = 0.002f, FontSize = 36, Position = new Vector3(0f, TopY - 0.2f, 0.251f) });
    }

    private JobBoard Board => GetTree().GetFirstNodeInGroup(JobBoard.Group) as JobBoard;

    public string Prompt(PlayerController player)
    {
        if (player.Held is not StockItem)
            return "Kalite kontrol masası: bitmiş parçayı elinde getir";
        return Board?.Active is WorkOrder order
            ? $"[Tık] parçayı ölç: {order.Title}"
            : "Önce iş emri panosundan bir iş al";
    }

    public void Use(PlayerController player)
    {
        if (player.Held is not StockItem part || Board is not JobBoard board || board.Active is not WorkOrder order)
            return;
        player.ReleaseHeld();
        part.Freeze = true;
        part.GlobalTransform = new Transform3D(part.GlobalBasis, GlobalTransform * new Vector3(0f, TopY + 0.06f, 0f));
        var report = Inspect(part, order, board.StartedAt);
        board.Post(report);
        PlayerController.Notify(this, report.Pass ? $"KABUL: {order.Title} (puan {report.Score})" : $"RED: {order.Title} (puan {report.Score}). Karne sol üstte.");
    }

    public InspectionReport Inspect(StockItem part, WorkOrder order, double since)
    {
        IPartGeometry geometry = part.Machined is VoxelStockVisual milled ? new VoxelGeometry(milled.Stock)
            : part.Turned is { } t ? new TurnedGeometry(t.RadiiMm, t.InnerMm, t.StepMm)
            : new VoxelGeometry(part.Voxels());
        var results = PartInspector.Inspect(order.FeatureSpecs(), new WithSurface(geometry, part.Surface.RaUm));
        var incidents = GetTree().GetNodesInGroup(ProceduralMachine.Group).OfType<ProceduralMachine>()
            .Where(m => m.Safety != null)
            .SelectMany(m => m.Safety.Incidents.Where(i => i.AtSeconds >= since).Select(i => $"{m.Name}: {i.Text}"))
            .ToList();
        bool materialOk = part.Blank.Material == order.Material;
        return new InspectionReport(order.Title, results, materialOk,
            $"{StockBlank.NameOf(part.Blank.Material)}, istenen {StockBlank.NameOf(order.Material)}", incidents, order.IncidentsFailThePart, part.SlipMm, part.Surface.SpringMm);
    }
}
