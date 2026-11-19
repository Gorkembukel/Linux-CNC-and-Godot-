using System.Collections.Generic;
using System.Linq;
using Godot;
using CncTrainer.Interaction;
using CncTrainer.Player;
using CncTrainer.Procedural;

namespace CncTrainer.Inspection;

/// <summary>
/// Work order board on the shop wall: the wheel leafs through the orders,
/// a click takes one (its drawing stays on the HUD) or hands it back. The
/// inspection table grades parts against the open order and posts the
/// report card here. The node's origin is on the floor.
/// </summary>
[Tool]
public partial class JobBoard : Node3D, IInteractable, IJobCard
{
    public const string Group = "job_boards";
    private const string OrdersFolder = "res://Machines/WorkOrders/";

    /// <summary>Orders on the board. Empty: every work order saved under
    /// res://Machines/WorkOrders/ is pinned up.</summary>
    [Export] public Godot.Collections.Array<WorkOrder> Orders = new();

    private readonly List<WorkOrder> _orders = new();
    private int _browse;
    private Label3D _sign;

    /// <summary>The job being worked on, null when none is open.</summary>
    public WorkOrder Active { get; private set; }
    /// <summary>When the open job was taken (s, engine ticks): incidents
    /// from then on count against it.</summary>
    public double StartedAt { get; private set; }
    public InspectionReport LastReport { get; private set; }

    public string CardText => LastReport?.Text() ?? Active?.DrawingText() ?? "";

    public override void _Ready()
    {
        BodyParts.Clear(this);
        BodyParts.SolidBox(this, "Board", new Vector3(1.2f, 0.8f, 0.03f), new Vector3(0f, 1.5f, 0f), BodyParts.Mat(new Color(0.55f, 0.42f, 0.28f), 0f, 0.9f));
        BodyParts.SolidBox(this, "Post", new Vector3(0.06f, 1.1f, 0.06f), new Vector3(0f, 0.55f, -0.03f), BodyParts.DarkSteel);
        AddChild(new Label3D { Text = "İŞ EMİRLERİ", PixelSize = 0.002f, FontSize = 44, Position = new Vector3(0f, 1.82f, 0.02f) });
        _sign = new Label3D { PixelSize = 0.0018f, FontSize = 40, Position = new Vector3(0f, 1.5f, 0.02f), Width = 900f, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_sign);
        if (Engine.IsEditorHint())
            return;
        AddToGroup(Group);
        AddToGroup(IJobCard.Group);
        _orders.AddRange(Orders.Where(o => o != null));
        if (_orders.Count == 0)
            _orders.AddRange(LoadFolder());
        UpdateSign();
    }

    private static IEnumerable<WorkOrder> LoadFolder() =>
        (DirAccess.GetFilesAt(OrdersFolder) ?? System.Array.Empty<string>())
            .Where(f => f.EndsWith(".tres") || f.EndsWith(".tres.remap"))
            .Select(f => f.Replace(".remap", ""))
            .OrderBy(f => f)
            .Select(f => GD.Load<WorkOrder>(OrdersFolder + f))
            .Where(o => o != null);

    private WorkOrder Browsed => _orders.Count == 0 ? null : _orders[_browse];

    public string Prompt(PlayerController player)
    {
        if (Browsed is not WorkOrder order)
            return "İş emri yok (res://Machines/WorkOrders/ boş)";
        string page = $"({_browse + 1}/{_orders.Count})";
        return order == Active
            ? $"Açık iş: {order.Title} {page}   [Tık] işi bırak   [Teker] diğer işler"
            : $"{order.OrderNumber} {order.Title} {page}   [Tık] bu işi al   [Teker] diğer işler";
    }

    public void Turn(PlayerController player, int steps)
    {
        if (_orders.Count == 0)
            return;
        _browse = ((_browse + steps) % _orders.Count + _orders.Count) % _orders.Count;
        UpdateSign();
    }

    public void Use(PlayerController player)
    {
        if (Browsed is not WorkOrder order)
            return;
        if (order == Active)
        {
            Active = null;
            LastReport = null;
            PlayerController.Notify(this, $"İş bırakıldı: {order.Title}");
        }
        else
            Take(order);
        UpdateSign();
    }

    /// <summary>Opens a job: its drawing goes on the HUD and the incident
    /// record starts from now.</summary>
    public void Take(WorkOrder order)
    {
        Active = order;
        StartedAt = Time.GetTicksMsec() / 1000.0;
        LastReport = null;
        PlayerController.Notify(this, $"İş alındı: {order.Title}. Teknik resim sol üstte.");
    }

    public void Post(InspectionReport report)
    {
        LastReport = report;
        UpdateSign();
    }

    private void UpdateSign()
    {
        if (_sign == null)
            return;
        var order = Browsed;
        _sign.Text = order == null ? "(boş)" : $"{order.OrderNumber}\n{order.Title}\n{(order == Active ? "AÇIK İŞ" : "")}";
    }
}
