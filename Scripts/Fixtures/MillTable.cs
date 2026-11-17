using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Interaction;
using CncTrainer.Player;
using CncTrainer.Spec;
using CncTrainer.Workshop;

namespace CncTrainer.Fixtures;

/// <summary>The mill table as something the player can set a fixture on:
/// a vise, a centring plate (rotary tables), or the stock itself, to be held
/// with step clamps. Origin at the centre of the table's top face (the
/// rotary centre on a rotary table); T-slots run along X.</summary>
public partial class MillTable : StaticBody3D, IInteractable
{
    /// <summary>Chips on the table: anything set down on them sits crooked.</summary>
    public bool Dirty;

    private float _halfX, _halfZ;
    private float[] _slotZ;

    public Vise Vise => GetChildren().OfType<Vise>().FirstOrDefault();
    public IStockFixture Fixture => GetChildren().OfType<IStockFixture>().FirstOrDefault();

    public static MillTable Create(float sizeX, float sizeZ, float thickness, float[] slotZ)
    {
        var table = new MillTable { Name = "TableSurface", _halfX = sizeX / 2f, _halfZ = sizeZ / 2f, _slotZ = slotZ };
        table.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(sizeX, thickness, sizeZ) },
            Position = new Vector3(0f, -thickness / 2f, 0f),
        });
        return table;
    }

    /// <summary>The distinct T-slots closest to each wanted position.</summary>
    public IEnumerable<float> SlotsNear(params float[] wanted) =>
        wanted.Select(w => _slotZ.OrderBy(z => Mathf.Abs(z - w)).First()).Distinct();

    /// <summary>Why a tool with its tip here (table frame) is in the table, or null.</summary>
    public string ToolCrash(Vector3 tip, float radius) =>
        tip.Y < -0.0005f && Mathf.Abs(tip.X) < _halfX + radius && Mathf.Abs(tip.Z) < _halfZ + radius
            ? "ÇARPMA: takım tablaya girdi"
            : null;

    private string OnChips => Dirty ? " (tabla talaşlı: önce fırçayla temizle)" : "";

    public string Prompt(PlayerController player)
    {
        bool taken = Fixture != null;
        if (player.Held is Vise)
            return taken ? "Tablada zaten bir bağlama var" : "[Tık] Mengeneyi tablaya koy" + OnChips;
        if (player.Held is CentringPlate)
            return taken ? "Tablada zaten bir bağlama var" : "[Tık] Merkezleme plakasını tabla merkezine oturt" + OnChips;
        if (player.Held is StockItem item)
            return taken ? "Tablada zaten bir bağlama var"
                : item.Blank.Shape != StockShape.Block ? "Tablaya yalnızca blok kütük pabuçla bağlanır"
                : "[Tık] Kütüğü tablaya koy (pabuçla bağlanacak)" + OnChips;
        if (player.HeldTool?.Kind == HandToolKind.Brush)
            return Dirty ? "[Tık] Tabladaki talaşı temizle" : "Tabla temiz";
        return null;
    }

    public void Use(PlayerController player)
    {
        var look = ToLocal(player.LookPoint);
        if (player.HeldTool?.Kind == HandToolKind.Brush)
            Dirty = false;
        else if (Fixture != null)
            return;
        else if (player.Held is Vise vise)
        {
            player.ReleaseHeld();
            vise.MountOn(this, Mathf.Clamp(look.X, -_halfX + 0.14f, _halfX - 0.14f));
        }
        else if (player.Held is CentringPlate plate)
        {
            player.ReleaseHeld();
            plate.MountOn(this);
        }
        else if (player.Held is StockItem item && item.Blank.Shape == StockShape.Block)
        {
            var turn = player.HeldTurnIn(this);
            player.ReleaseHeld();
            var half = SquareTurn.Extents(turn, item.Blank.BoundsM) / 2f;
            TableClamping.Place(this, item, new Vector3(
                Mathf.Clamp(look.X, -_halfX + half.X, _halfX - half.X), 0f, Mathf.Clamp(look.Z, -_halfZ + half.Z, _halfZ - half.Z)), turn: turn);
        }
    }
}
