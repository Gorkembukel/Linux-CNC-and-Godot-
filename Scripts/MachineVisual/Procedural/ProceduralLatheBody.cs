using System.Linq;
using Godot;
using CncTrainer.Bridge;
using CncTrainer.Fixtures;
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
    private int _activeStation = -1;
    private ToolSpec[] _stationTools = System.Array.Empty<ToolSpec>();
    private readonly System.Collections.Generic.List<Node3D> _stations = new();

    /// <summary>The cross slide, which carries the turret or tool post;
    /// the tool tip is at (0, ToolReachM, 0) in its frame.</summary>
    public Node3D CrossSlide => _crossSlide;
    public float ToolReachM => _toolReach;
    public int StationCount => _stationTools.Length;

    /// <summary>With manual setup: the chuck the player loads, its jaw
    /// front at machine Z = JawFrontZMm.</summary>
    public JawChuck Chuck { get; private set; }
    public float JawFrontZMm { get; private set; }
    /// <summary>The enclosure's open front (centre and size, this body's
    /// frame), null on an open machine.</summary>
    public (Vector3 Centre, Vector2 Size)? DoorOpening { get; private set; }

    /// <summary>A stretch of machine Z (mm) and how far from the centreline,
    /// toward the tool side, something fills it: fixed parts fill up to
    /// Reach, parts riding on the carriage fill from Reach outward.</summary>
    public readonly record struct Zone(string What, float ZMin, float ZMax, float Reach);

    private readonly System.Collections.Generic.List<Zone> _fixedZones = new();
    private float _wallLeftMm = float.NegativeInfinity, _wallRightMm = float.PositiveInfinity, _outerReachMm = float.PositiveInfinity;
    private float _turretRadiusMm;

    public void Build(MachineSpec spec)
    {
        _spec = spec;
        Clear(this);
        _fixedZones.Clear();
        _wallLeftMm = float.NegativeInfinity;
        _wallRightMm = _outerReachMm = float.PositiveInfinity;
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

        JawFrontZMm = l.StockZMin + l.GripLengthMm;
        Chuck = null;
        if (spec.ManualSetup)
        {
            Chuck = JawChuck.Create(l.ChuckDiameterMm, l.ChuckJawCount, l.GripLengthMm, l.HeadstockLengthMm);
            Chuck.Position = new Vector3(JawFrontZMm * M, 0f, 0f);
            AddChild(Chuck);
        }
        else
        {
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
        _turretRadiusMm = l.TurretDiameterMm / 2f;
        AddZone("fener mili gövdesine", bedLeft, headstockRight,
            BoxReach(new Vector3(0f, (headstockTop + bedTop) / 2f, 0f), new Vector3(0f, (headstockTop - bedTop) / 2f, bedDepth / 2f)));
        AddZone("aynaya", headstockRight, JawFrontZMm * M, chuckR);

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
        _stations.Clear();
        _activeStation = -1;
        _stationCount = l.Turret == TurretKind.Disc ? Mathf.Max(1, Mathf.Max(l.TurretStations, tools.Length)) : Mathf.Max(1, tools.Length);
        // With manual setup the stations start empty: the player loads them.
        _stationTools = new ToolSpec[_stationCount];
        if (!spec.ManualSetup)
            tools.CopyTo(_stationTools, 0);
        if (l.Turret == TurretKind.Disc)
        {
            Box(_crossSlide, "TurretHousing", new Vector3(0.12f, turretR * 1.6f, turretR * 1.6f),
                new Vector3(0.075f, _toolReach, 0f), bodyMat);
            _turret = new Node3D { Name = "Turret", Position = new Vector3(0f, _toolReach, 0f), RotationDegrees = new Vector3(_indexAngle, 0f, 0f) };
            _crossSlide.AddChild(_turret);
            Cylinder(_turret, "Disc", turretR, 0.03f, Vector3.Zero, Vector3.Right, DarkSteel);
            _stationShank = Mathf.Max(0.01f, _toolReach - turretR * 0.8f);
            for (int i = 0; i < _stationCount; i++)
            {
                var station = new Node3D { Name = "Station", RotationDegrees = new Vector3(i * 360f / _stationCount, 0f, 0f) };
                _turret.AddChild(station);
                _stations.Add(station);
                RefreshStation(i);
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
            AddZone("gezer puntaya", tailX - 0.07f, tailX + 0.07f,
                BoxReach(new Vector3(0f, (0.06f + bedTop) / 2f, 0f), new Vector3(0f, (0.06f - bedTop) / 2f, 0.07f)));
            AddZone("gezer punta kovanına", tailX - 0.19f, tailX - 0.07f, 0.02f);
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
            DoorOpening = (new Vector3((right + left) / 2f, midY, depth / 2f), new Vector2(right - left, height));
            _wallLeftMm = (left + 0.01f) / M;
            _wallRightMm = (right - 0.01f) / M;
            var d = _slideBasis * Vector3.Up;
            if (d.Y > 1e-3f)
                _outerReachMm = Mathf.Min(_outerReachMm, (top - 0.01f) / d.Y / M);
            if (d.Z < -1e-3f)
                _outerReachMm = Mathf.Min(_outerReachMm, (depth / 2f - 0.01f) / -d.Z / M);
        }
        else
            DoorOpening = null;

        SetPose(0f, (xAxis?.MaxLimit ?? 100f) * 0.5f, tools.FirstOrDefault()?.Number ?? 0, 0.0);
    }

    private float _stationShank;

    private void AddZone(string what, float xMin, float xMax, float reachM) =>
        _fixedZones.Add(new Zone(what, xMin / M, xMax / M, reachM / M));

    /// <summary>How far a box (centre and half size across the spindle,
    /// metres) reaches from the centreline toward the tool.</summary>
    private float BoxReach(Vector3 centre, Vector3 half)
    {
        var d = _slideBasis * Vector3.Up;
        return d.Dot(centre) + Mathf.Abs(d.Y) * half.Y + Mathf.Abs(d.Z) * half.Z;
    }

    /// <summary>The turret or tool post with the tip at (zMm, xMm).</summary>
    public System.Collections.Generic.IEnumerable<Zone> Carried(float zMm, float xMm)
    {
        float reach = _toolReach / M;
        if (_turret != null)
        {
            yield return new Zone("Taret", zMm - 15f, zMm + 15f, xMm + reach - _turretRadiusMm);
            yield return new Zone("Taret gövdesi", zMm + 15f, zMm + 135f, xMm + reach - 0.8f * _turretRadiusMm);
        }
        else
            yield return new Zone("Kalemlik", zMm - 5f, zMm + 65f, xMm + reach - 35f);
    }

    /// <summary>Why the carriage, with the tip at (zMm, xMm), is inside a
    /// fixed part of the machine, or null.</summary>
    public string Interference(float zMm, float xMm)
    {
        var tool = new Zone("Takım", zMm - 0.5f, zMm + 12f, xMm);
        foreach (var part in Carried(zMm, xMm).Prepend(tool))
        {
            foreach (var zone in _fixedZones)
                if (part.ZMin < zone.ZMax && part.ZMax > zone.ZMin && part.Reach < zone.Reach)
                    return $"ÇARPMA: {part.What} {zone.What} çarptı";
            if (part.ZMin < _wallLeftMm || part.ZMax > _wallRightMm)
                return $"ÇARPMA: {part.What} kabin duvarına çarptı";
        }
        float outer = xMm + _toolReach / M + (_turret != null ? _turretRadiusMm : 35f);
        return outer > _outerReachMm ? "ÇARPMA: taret kabine çarptı (X kursu kabinden uzun)" : null;
    }

    private void RefreshStation(int index)
    {
        if (index >= _stations.Count)
            return;
        Clear(_stations[index]);
        if (_stationTools[index] == null)
            return;
        var tool = ToolMeshBuilder.LatheTool(_stationTools[index], _stationShank);
        tool.Position = new Vector3(0f, _toolReach, 0f);
        tool.RotationDegrees = new Vector3(180f, 0f, 0f);
        _stations[index].AddChild(tool);
    }

    private void RefreshPost()
    {
        if (_postTool == null)
            return;
        Clear(_postTool);
        if (_activeStation >= 0 && _activeStation < _stationTools.Length && _stationTools[_activeStation] != null)
            _postTool.AddChild(ToolMeshBuilder.LatheTool(_stationTools[_activeStation], _toolReach));
    }

    /// <summary>Station (0-based) the control's tool number means, -1 = none.</summary>
    public int StationOf(int toolNumber) => _spec.Tools.Where(t => t != null).ToList().FindIndex(t => t.Number == toolNumber);

    /// <summary>Shows this tool in a station (null = empty).</summary>
    public void SetStationTool(int index, ToolSpec tool)
    {
        if (index < 0 || index >= _stationTools.Length)
            return;
        _stationTools[index] = tool;
        RefreshStation(index);
        if (index == _activeStation)
            RefreshPost();
    }

    private MeshInstance3D _coolant;

    /// <summary>Shows or hides the coolant stream at the tool.</summary>
    public void SetCoolant(bool on)
    {
        if (_coolant == null)
        {
            if (!on)
                return;
            _coolant = Cylinder(_crossSlide, "Coolant", 0.004f, 0.1f, new Vector3(0.03f, 0.04f, 0f), new Vector3(0.5f, 1f, 0f), CoolantLook());
        }
        _coolant.Visible = on;
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
            int index = _activeStation = StationOf(toolNumber);
            if (_turret != null && index >= 0)
                _targetAngle = MachineBody.TargetIndexAngleDeg(index + 1, _stationCount);
            RefreshPost();
        }
        if (_turret != null)
        {
            _indexAngle = MachineBody.StepAngleToward(_indexAngle, _targetAngle, IndexSpeedDegPerSec * (float)delta);
            _turret.RotationDegrees = new Vector3(_indexAngle, 0f, 0f);
        }
    }
}
