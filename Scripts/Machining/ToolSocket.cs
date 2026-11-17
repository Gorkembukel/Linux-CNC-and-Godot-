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
    private bool _turret, _latheTools;

    /// <summary>Asked before a tool is handled: is the spindle turning?</summary>
    public System.Func<bool> Turning;
    /// <summary>Why this tool cannot go in (too big, too long), or null.</summary>
    public System.Func<Spec.ToolSpec, string> Refuse;
    /// <summary>Pocket a control tool number means (turret only).</summary>
    public System.Func<int, int> PocketOf;
    /// <summary>Tool number the control has active now.</summary>
    public System.Func<int> ActiveNumber;
    /// <summary>A pocket was filled or emptied (pocket index).</summary>
    public event System.Action<int> Changed;

    public int PocketCount => _pockets.Length;
    public int LoadedCount => _pockets.Count(p => p != null);

    /// <summary>turret: tool N sits in its own pocket (a lathe turret, or a
    /// mill's magazine when latheTools is false).</summary>
    public static ToolSocket Create(int pockets, bool turret, Vector3 colliderSize, Vector3 colliderCentre, bool? latheTools = null)
    {
        var socket = new ToolSocket
        {
            Name = "ToolSocket",
            _pockets = new ToolItem[Mathf.Max(1, pockets)],
            _turret = turret,
            _latheTools = latheTools ?? turret,
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
        bool magazine = _turret && !_latheTools;
        string place = magazine ? "Magazin" : _turret ? "Taret" : "İş mili";
        if (player.Held is ToolItem item)
        {
            if (item.ForLathe != _latheTools)
                return _latheTools ? "Bu bir freze takımı: tarete takılmaz" : "Bu bir torna kalemi: freze iş miline takılmaz";
            if (Refuse?.Invoke(item.Spec) is string refusal)
                return refusal;
            int target = LoadPocket(item);
            string slot = magazine ? "cebi" : "istasyonu";
            if (_turret && target < 0)
                return OwnPocket(item) < 0 ? $"T{item.Spec.Number} bu makinenin takım listesinde yok"
                    : $"T{item.Spec.Number} {slot} dolu: önce oradaki takımı al";
            if (target < 0)
                return "İş milinde takım var: önce onu çıkar";
            return _turret ? $"[Tık] Takımı {target + 1}. {(magazine ? "cebe" : "istasyona")} tak (T{item.Spec.Number})" : "[Tık] Takımı iş miline tak";
        }
        if (player.Held != null)
            return null;
        int pocket = PocketToUnload();
        if (pocket < 0)
            return $"{place} boş: takım arabasından takım getir";
        return _turret ? $"[Tık] {pocket + 1}. {(magazine ? "cepteki" : "istasyondaki")} takımı al: {_pockets[pocket].Label}" : $"[Tık] Takımı iş milinden al: {_pockets[pocket].Label}";
    }

    // Tool N goes where the control will look for it when the program calls T N.
    private int OwnPocket(ToolItem item)
    {
        int pocket = ActivePocket(item.Spec.Number);
        return pocket >= 0 && pocket < _pockets.Length ? pocket : -1;
    }

    private int LoadPocket(ToolItem item)
    {
        if (!_turret)
            return System.Array.IndexOf(_pockets, null);
        int pocket = OwnPocket(item);
        return pocket >= 0 && _pockets[pocket] == null ? pocket : -1;
    }

    public void Use(PlayerController player)
    {
        if (Turning?.Invoke() == true)
            return;
        if (player.Held is ToolItem item)
        {
            if (item.ForLathe == _latheTools && Refuse?.Invoke(item.Spec) == null && LoadPocket(item) is int target and >= 0)
            {
                player.ReleaseHeld();
                Load(item, target);
            }
        }
        else if (player.Held == null && PocketToUnload() is int pocket and >= 0)
            player.Give(Unload(pocket));
    }
}
