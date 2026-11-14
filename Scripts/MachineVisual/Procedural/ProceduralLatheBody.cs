using System.Linq;
using Godot;
using CncTrainer.Bridge;
using CncTrainer.Spec;
using static CncTrainer.Procedural.BodyParts;

namespace CncTrainer.Procedural;

/// <summary>
/// Builds a lathe from a MachineSpec. Local frame: X = machine Z (spindle
/// axis, the origin is program zero on the centreline), and the tool
/// approaches the centreline from the direction CrossSlideAngleDeg gives.
/// The carriage/cross-slide/turret live in a "slide" frame rotated about X
/// so that its +Y is that direction: there machine X (radius) is simply Y.
/// </summary>
[Tool]
public partial class ProceduralLatheBody : Node3D
{
    private const float M = WorldScale.MmToM;
    private const float IndexSpeedDegPerSec = 240f;

    private MachineSpec _spec;
    private Basis _slideBasis = Basis.Identity;
    private Node3D _carriage, _crossSlide, _turret, _postTool;
    private float _toolReach;
    private int _stationCount = 1;
    private float _indexAngle = 180f, _targetAngle = 180f;
    private int _toolNumber = int.MinValue;

    public void Build(MachineSpec spec)
    {
        _spec = spec;
        Clear(this);
        _toolNumber = int.MinValue;
        _indexAngle = _targetAngle = 180f;

        var l = spec.Lathe;
        var bodyMat = Mat(spec.BodyColor);
        var accent = Mat(spec.AccentColor, 0.5f);
        float floorY = -spec.WorkHeightMm * M;

        float stockR = l.StockDiameterMm / 2f * M, zMin = l.StockZMin * M;
        float chuckR = l.ChuckDiameterMm / 2f * M, grip = l.GripLengthMm * M;
        float headstockH = l.HeadstockHeightMm * M, bedDepth = l.BedDepthMm * M;
        float bedTop = -(chuckR + 0.03f);

        float chuckLength = 0.05f + grip * 0.3f;
        Cylinder(this, "Chuck", chuckR, chuckLength, new Vector3(zMin - 0.05f + chuckLength / 2f, 0f, 0f), Vector3.Right, Steel);
        float jawOuter = chuckR * 0.85f;
        for (int i = 0; i < l.ChuckJawCount; i++)
        {
            var jaw = new Node3D { Name = "Jaw", RotationDegrees = new Vector3(i * 360f / l.ChuckJawCount, 0f, 0f) };
            AddChild(jaw);
            Box(jaw, "JawBlock", new Vector3(grip * 0.7f, jawOuter - stockR, 0.022f),
                new Vector3(zMin + grip * 0.65f, (jawOuter + stockR) / 2f, 0f), DarkSteel);
        }

        float headstockRight = zMin - 0.05f, bedLeft = headstockRight - l.HeadstockLengthMm * M;
        float bedRight = bedLeft + l.BedLengthMm * M;
        float headstockTop = Mathf.Max(headstockH / 2f, chuckR * 0.8f);
        SolidBox(this, "Headstock", new Vector3(headstockRight - bedLeft, headstockTop - bedTop, bedDepth),
            new Vector3((headstockRight + bedLeft) / 2f, (headstockTop + bedTop) / 2f, 0f), bodyMat);
        SolidBox(this, "Bed", new Vector3(bedRight - bedLeft, bedTop - floorY, bedDepth),
            new Vector3((bedRight + bedLeft) / 2f, (bedTop + floorY) / 2f, 0f), bodyMat);

        var xAxis = spec.FindAxis(AxisId.X);
        var zAxis = spec.FindAxis(AxisId.Z);
        float xMax = (xAxis?.MaxLimit ?? 150f) * M;
        _toolReach = l.ToolReachMm * M;
        float turretR = l.TurretDiameterMm / 2f * M;

        _slideBasis = new Basis(Vector3.Right, -Mathf.DegToRad(l.CrossSlideAngleDeg));
        var slide = new Node3D { Name = "Slide", Basis = _slideBasis };
        AddChild(slide);

        // The ways sit behind the tool as seen from the centreline, which
        // puts them under the spindle on a flat bed whichever side the tool is on.
        float waysSide = l.CrossSlideAngleDeg < 0f ? 1f : -1f;
        float waysOffset = chuckR + 0.06f;
        float waysLow = -0.05f, waysHigh = xMax + _toolReach + 0.1f;
        Box(slide, "Ways", new Vector3(bedRight - headstockRight, waysHigh - waysLow, 0.05f),
            new Vector3((bedRight + headstockRight) / 2f, (waysHigh + waysLow) / 2f, waysSide * (waysOffset + 0.025f)), DarkSteel);

        _carriage = new Node3D { Name = "Carriage" };
        slide.AddChild(_carriage);
        Box(_carriage, "Saddle", new Vector3(0.2f, waysHigh - waysLow, 0.04f),
            new Vector3(0.06f, (waysHigh + waysLow) / 2f, waysSide * (waysOffset - 0.02f)), accent);

        _crossSlide = new Node3D { Name = "CrossSlide" };
        _carriage.AddChild(_crossSlide);
        Box(_crossSlide, "Plate", new Vector3(0.16f, 0.2f, 0.03f), new Vector3(0.06f, _toolReach, waysSide * (waysOffset - 0.055f)), accent);
        Box(_crossSlide, "Bracket", new Vector3(0.1f, 0.1f, waysOffset - 0.07f),
            new Vector3(0.075f, _toolReach, waysSide * (waysOffset - 0.07f) / 2f), accent);

        var tools = spec.Tools.Where(t => t != null).ToArray();
        if (l.Turret == TurretKind.Disc)
        {
            _stationCount = Mathf.Max(1, Mathf.Max(l.TurretStations, tools.Length));
            Box(_crossSlide, "TurretHousing", new Vector3(0.12f, turretR * 1.6f, turretR * 1.6f),
                new Vector3(0.075f, _toolReach, 0f), bodyMat);
            _turret = new Node3D { Name = "Turret", Position = new Vector3(0f, _toolReach, 0f), RotationDegrees = new Vector3(_indexAngle, 0f, 0f) };
            _crossSlide.AddChild(_turret);
            Cylinder(_turret, "Disc", turretR, 0.03f, Vector3.Zero, Vector3.Right, DarkSteel);
            for (int i = 0; i < tools.Length; i++)
            {
                var station = new Node3D { Name = "Station", RotationDegrees = new Vector3(i * 360f / _stationCount, 0f, 0f) };
                _turret.AddChild(station);
                var tool = ToolMeshBuilder.LatheTool(tools[i], Mathf.Max(0.01f, _toolReach - turretR * 0.8f));
                tool.Position = new Vector3(0f, _toolReach, 0f);
                tool.RotationDegrees = new Vector3(180f, 0f, 0f);
                station.AddChild(tool);
            }
        }
        else
        {
            _turret = null;
            Box(_crossSlide, "ToolPost", new Vector3(0.07f, 0.07f, 0.07f), new Vector3(0.03f, _toolReach, 0f), bodyMat);
            _postTool = new Node3D { Name = "ActiveTool" };
            _crossSlide.AddChild(_postTool);
        }

        if (l.Tailstock)
        {
            float tailX = Mathf.Min(bedRight - 0.08f, Mathf.Max((zAxis?.MaxLimit ?? 50f) * M, l.StockZMax * M) + 0.2f);
            Box(this, "Tailstock", new Vector3(0.14f, 0.06f - bedTop, 0.14f), new Vector3(tailX, (0.06f + bedTop) / 2f, 0f), bodyMat);
            Cylinder(this, "Quill", 0.02f, 0.1f, new Vector3(tailX - 0.11f, 0f, 0f), Vector3.Right, Steel);
            Cylinder(this, "LiveCentre", 0f, 0.03f, new Vector3(tailX - 0.175f, 0f, 0f), Vector3.Right, Steel, topRadius: 0.02f);
        }

        if (spec.Enclosure)
        {
            var sheet = Mat(new Color(0.8f, 0.81f, 0.83f), 0.1f, 0.7f);
            float left = bedLeft - 0.05f, right = bedRight + 0.05f, depth = bedDepth + 0.5f;
            float top = Mathf.Max(headstockTop, waysHigh) + 0.2f, height = top - floorY, midY = (top + floorY) / 2f;
            SolidBox(this, "EnclosureBack", new Vector3(right - left, height, 0.02f), new Vector3((right + left) / 2f, midY, -depth / 2f), sheet);
            SolidBox(this, "EnclosureLeft", new Vector3(0.02f, height, depth), new Vector3(left, midY, 0f), sheet);
            SolidBox(this, "EnclosureRight", new Vector3(0.02f, height, depth), new Vector3(right, midY, 0f), sheet);
            Box(this, "EnclosureRoof", new Vector3(right - left, 0.02f, depth), new Vector3((right + left) / 2f, top, 0f), sheet);
        }

        SetPose(0f, (xAxis?.MaxLimit ?? 100f) * 0.5f, tools.FirstOrDefault()?.Number ?? 0, 0.0);
    }

    /// <summary>Where the tool tip is in this body's frame (metres).</summary>
    public Vector3 TipLocal(float zMm, float radiusMm) => new Vector3(zMm * M, 0f, 0f) + _slideBasis * new Vector3(0f, radiusMm * M, 0f);

    public void SetPose(float zMm, float radiusMm, int toolNumber, double delta)
    {
        _carriage.Position = new Vector3(zMm * M, 0f, 0f);
        _crossSlide.Position = new Vector3(0f, radiusMm * M, 0f);

        if (toolNumber != _toolNumber)
        {
            _toolNumber = toolNumber;
            var tools = _spec.Tools.Where(t => t != null).ToList();
            int index = tools.FindIndex(t => t.Number == toolNumber);
            if (_turret != null && index >= 0)
                _targetAngle = MachineBody.TargetIndexAngleDeg(index + 1, _stationCount);
            if (_postTool != null)
            {
                Clear(_postTool);
                if (index >= 0)
                    _postTool.AddChild(ToolMeshBuilder.LatheTool(tools[index], _toolReach));
            }
        }
        if (_turret != null)
        {
            _indexAngle = MachineBody.StepAngleToward(_indexAngle, _targetAngle, IndexSpeedDegPerSec * (float)delta);
            _turret.RotationDegrees = new Vector3(_indexAngle, 0f, 0f);
        }
    }
}
