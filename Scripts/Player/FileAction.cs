using Godot;
using CncTrainer.Interaction;
using CncTrainer.Workshop;

namespace CncTrainer.Player;

/// <summary>
/// Taking a burr off with a file or the deburring blade: the crosshair on a
/// burred part, the left button held, mouse forward / back strokes along the
/// edge. The view stays put while the button is held on the part.
/// </summary>
public partial class FileAction : Node
{
    public PlayerController Player;

    /// <summary>Stroke (m) that takes a burr off: the blade is quicker on an edge.</summary>
    private const float FileStrokeM = 1.2f, BladeStrokeM = 0.5f;
    private const float MetresPerPixel = 0.0006f;

    private StockItem _part;
    private float _done;
    private bool _stroking;

    public string Status { get; private set; }

    private float Needed => Player.HeldTool?.Kind == HandToolKind.Deburrer ? BladeStrokeM : FileStrokeM;

    private bool HoldingFile => Player.HeldTool?.Kind is HandToolKind.File or HandToolKind.Deburrer;

    public void Tick(Node3D lookedAt)
    {
        Status = null;
        if (!HoldingFile)
        {
            _stroking = false;
            return;
        }
        var part = Find(lookedAt);
        if (part != _part)
        {
            _part = part;
            _done = 0f;
        }
        if (_part == null)
            return;
        Status = _part.Burred
            ? System.FormattableString.Invariant($"[Sol tuş basılı + fare ileri-geri] çapağı al  %{_done / Needed * 100f:0}")
            : "Çapak yok";
    }

    private static StockItem Find(Node node)
    {
        for (; node != null; node = node.GetParent())
            if (node is StockItem item)
                return item;
        return null;
    }

    /// <summary>True when filing took the event.</summary>
    public bool HandleInput(InputEvent @event)
    {
        if (!HoldingFile || _part is not { Burred: true })
            return false;
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _stroking = button.Pressed;
                return true;
            case InputEventMouseMotion motion when _stroking:
                _done += Mathf.Abs(motion.Relative.Y) * MetresPerPixel;
                if (_done >= Needed)
                {
                    _part.Burred = false;
                    _stroking = false;
                    PlayerController.Notify(this, "Çapak alındı");
                }
                return true;
        }
        return false;
    }
}
