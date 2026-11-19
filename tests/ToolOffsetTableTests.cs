using System.IO;
using CncTrainer.Bridge;

namespace CncTrainer.Tests;

public class ToolOffsetTableTests
{
    private static ToolOffsetTable Table(params ToolTableEntry[] entries)
    {
        var table = new ToolOffsetTable();
        table.Update(entries);
        return table;
    }

    [Fact]
    public void WearIsSplitOffLinuxCncsOffset_AndTheGeometryStays()
    {
        var table = Table(new ToolTableEntry(1, 0, 110, 10, 0), new ToolTableEntry(2, 0, 110, 6, 0));

        Assert.Equal(109.98f, table.SetWear(1, AxisId.Z, -0.02f)!.Value, 4);
        table.Update(new[] { new ToolTableEntry(1, 0, 109.98, 10, 0), new ToolTableEntry(2, 0, 110, 6, 0) });

        var row = table.Rows[0];
        Assert.Equal(110f, row.GeometryZ, 4);
        Assert.Equal(-0.02f, row.WearZ, 4);
        Assert.Equal(110f, table.Rows[1].GeometryZ, 4);
        Assert.Equal(115.98f, table.TotalForGeometry(1, AxisId.Z, 116f), 4);
    }

    [Fact]
    public void ChangingWearAgain_ReplacesIt_RatherThanStacking()
    {
        var table = Table(new ToolTableEntry(1, 0, 109.98, 10, 0));
        table.SetWear(1, AxisId.Z, -0.02f);
        Assert.Equal(109.97f, table.SetWear(1, AxisId.Z, -0.03f)!.Value, 4);
    }

    [Fact]
    public void UnknownTool_HasNoWearToSet()
    {
        Assert.Null(Table(new ToolTableEntry(1, 0, 0, 0, 0)).SetWear(7, AxisId.Z, 0.1f));
    }

    [Fact]
    public void ToolTablePacketBody_ReadsBackInOrder()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write((ushort)2);
            w.Write(1); w.Write(0.0); w.Write(110.0); w.Write(10.0); w.Write(0);
            w.Write(9); w.Write(-0.25); w.Write(1.5); w.Write(0.4); w.Write(3);
        }
        ms.Position = 0;
        var entries = ToolTableEntry.ReadAll(new BinaryReader(ms));
        Assert.Equal(new[] { new ToolTableEntry(1, 0, 110, 10, 0), new ToolTableEntry(9, -0.25, 1.5, 0.4, 3) }, entries);
        Assert.Equal(2 + 2 * 32, ms.Position);
    }
}
