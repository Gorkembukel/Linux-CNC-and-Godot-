using System.Linq;
using Godot;
using CncTrainer.Fixtures;
using CncTrainer.Interaction;
using CncTrainer.Player;

namespace CncTrainer.Machining;

/// <summary>
/// Where tools sit in a machine: the one spindle of a mill, or the stations
/// of a lathe turret. The player puts tools in and takes them out by hand;
/// what is cut with is whatever really sits there, whichever tool number
/// the control believes it has. A mill's control calling T2 while T1 is
/// still in the spindle cuts with T1.
/// </summary>
public partial class ToolSocket : StaticBody3D, IInteractable, IToolSource
{
    private ToolItem[] _pockets;
    private bool _turret;

    /// <summary>Asked before a tool is handled: is the spindle turning?</summary>
    public System.Func<bool> Turning;
    /// <summary>Pocket a control tool number means (turret only).</summary>
    public System.Func<int, int> PocketOf;
    /// <summary>Tool number the control has active now.</summary>
    public System.Func<int> ActiveNumber;
    /// <summary>A pocket was filled or emptied (pocket index).</summary>
    public event System.Action<int> Changed;

    public int PocketCount => _pockets.Length;
    public int LoadedCount => _pockets.Count(p => p != null);

    public static ToolSocket Create(int pockets, bool turret, Vector3 colliderSize, Vector3 colliderCentre)
    {
        var socket = new ToolSocket
        {
            Name = "ToolSocket",
            _pockets = new ToolItem[Mathf.Max(1, pockets)],
            _turret = turret,
            CollisionLayer = Fastener.InteractionLayer,
            CollisionMask = 0,
        };
        socket.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = colliderSize }, Position = colliderCentre });
        return socket;
    }

    public ToolItem Pocket(int index) => index >= 0 && index < _pockets.Length ? _pockets[index] : null;

    private int ActivePocket(int controlToolNumber) => _turret ? PocketOf?.Invoke(controlToolNumber) ?? controlToolNumber - 1 : 0;

    public MountedTool Active(int controlToolNumber) => Pocket(ActivePocket(controlToolNumber))?.Mounted ?? default;

    /// <summary>Puts the tool in a pocket (the first free one when none is
    /// given). The tool leaves the world while it is in the machine.</summary>
    public bool Load(ToolItem item, int pocket = -1)
    {
        if (pocket < 0)
            pocket = System.Array.IndexOf(_pockets, null);
        if (pocket < 0 || pocket >= _pockets.Length || _pockets[pocket] != null)
            return false;
        item.GetParent()?.RemoveChild(item);
        _pockets[pocket] = item;
        Changed?.Invoke(pocket);
        return true;
    }

    /// <summary>Takes the tool out of a pocket and back into the world.</summary>
    public ToolItem Unload(int pocket)
    {
        var item = Pocket(pocket);
        if (item == null)
            return null;
        _pockets[pocket] = null;
        item.DisplayName = item.Label;
        GetTree().CurrentScene.AddChild(item);
        item.GlobalPosition = GlobalPosition;
        Changed?.Invoke(pocket);
        return item;
    }

    // The pocket an empty hand takes from: the active one, else the last filled.
    private int PocketToUnload()
    {
        int active = ActivePocket(ActiveNumber?.Invoke() ?? 0);
        return Pocket(active) != null ? active : System.Array.FindLastIndex(_pockets, p => p != null);
    }

    public string Prompt(PlayerController player)
    {
        if (Turning?.Invoke() == true)
            return "İş mili dönerken takıma dokunulmaz";
        if (player.Held is ToolItem item)
        {
            if (item.Spec.IsLatheTool != _turret)
                return _turret ? "Bu bir freze takımı: tarete takılmaz" : "Bu bir torna kalemi: iş miline takılmaz";
            int free = System.Array.IndexOf(_pockets, null);
            if (free < 0)
                return _turret ? "Tarette boş istasyon yok" : "İş milinde takım var: önce onu çıkar";
            return _turret ? $"[Tık] Takımı {free + 1}. istasyona tak" : "[Tık] Takımı iş miline tak";
        }
        if (player.Held != null)
            return null;
        int pocket = PocketToUnload();
        if (pocket < 0)
            return _turret ? "Taret boş: takım arabasından kalem getir" : "İş mili boş: takım arabasından takım getir";
        return _turret ? $"[Tık] {pocket + 1}. istasyondaki takımı al: {_pockets[pocket].Label}" : $"[Tık] Takımı iş milinden al: {_pockets[pocket].Label}";
    }

    public void Use(PlayerController player)
    {
        if (Turning?.Invoke() == true)
            return;
        if (player.Held is ToolItem item)
        {
            if (item.Spec.IsLatheTool == _turret && System.Array.IndexOf(_pockets, null) >= 0)
            {
                player.ReleaseHeld();
                Load(item);
            }
        }
        else if (player.Held == null && PocketToUnload() is int pocket and >= 0)
            player.Give(Unload(pocket));
    }
}
