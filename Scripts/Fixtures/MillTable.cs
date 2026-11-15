using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Interaction;
using CncTrainer.Player;

namespace CncTrainer.Fixtures;

/// <summary>The mill table as something the player can set a fixture on.
/// Origin at the centre of the table's top face; T-slots run along X.</summary>
public partial class MillTable : StaticBody3D, IInteractable
{
    /// <summary>Chips on the table: anything set down on them sits crooked.</summary>
    public bool Dirty;

    private float _halfX;
    private float[] _slotZ;

    public Vise Vise => GetChildren().OfType<Vise>().FirstOrDefault();

    public static MillTable Create(float sizeX, float sizeZ, float thickness, float[] slotZ)
    {
        var table = new MillTable { Name = "TableSurface", _halfX = sizeX / 2f, _slotZ = slotZ };
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

    public string Prompt(PlayerController player)
    {
        if (player.Held is Vise)
            return Vise != null ? "Tablada zaten bir mengene var"
                : Dirty ? "[Tık] Mengeneyi tablaya koy (tabla talaşlı: önce fırçayla temizle)" : "[Tık] Mengeneyi tablaya koy";
        if (player.HeldTool?.Kind == HandToolKind.Brush)
            return Dirty ? "[Tık] Tabladaki talaşı temizle" : "Tabla temiz";
        return null;
    }

    public void Use(PlayerController player)
    {
        if (player.Held is Vise vise && Vise == null)
        {
            player.ReleaseHeld();
            vise.MountOn(this, Mathf.Clamp(ToLocal(player.LookPoint).X, -_halfX + 0.14f, _halfX - 0.14f));
        }
        else if (player.HeldTool?.Kind == HandToolKind.Brush)
            Dirty = false;
    }
}
