using System;
using Godot;
using CncTrainer.Chips;
using CncTrainer.Inspection;
using CncTrainer.Workshop;

namespace CncTrainer.UI;

/// <summary>New game: the free workshop, or one of the work orders as a
/// training scenario (the order open on the board, its stock on the tray).</summary>
public partial class ScenarioPanel : VBoxContainer
{
    public event Action Closed;

    public override void _Ready()
    {
        MenuUi.Heading(this, "Yeni oyun");
        MenuUi.Button(this, "Serbest atölye", () => Session.Start(GetTree(), new StartRequest()),
            "Boş atölye: makineler, takımlar ve kütük istasyonu senin.");
        AddChild(new Label { Text = "Eğitim senaryoları (iş emirleri):" });
        foreach (var order in WorkOrder.LoadAll())
        {
            string machine = order.Machine == Spec.MachineKind.Lathe ? "Torna" : "Freze";
            MenuUi.Button(this, $"{order.OrderNumber}  {order.Title}  ({machine}, {StockBlank.NameOf(order.Material)})",
                () => Session.Start(GetTree(), new StartRequest(order)),
                $"{order.Stock.Describe()}\n{order.Notes}");
        }
        MenuUi.Button(this, "Geri", () => Closed?.Invoke());
    }
}
