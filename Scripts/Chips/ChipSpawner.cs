using System;
using Godot;
using CncTrainer;

namespace CncTrainer.Chips;

/// <summary>
/// Brief section 6: procedural chips, pooled, with a hard upper limit and
/// simplified physics (gravity + a coarse floor, then sleep). Simplification
/// stated plainly: "ribbon mesh" geometry for continuous/spiral chips is NOT
/// implemented as true curled ribbons -- all chip types use the same pooled
/// stretched-box mesh, distinguished by size/elongation/spin/color, which is
/// visually distinct (small tumbling flakes vs. long twisting strands) without
/// per-type mesh generation. This is the single biggest scope cut in the
/// project relative to the brief's literal wording; a real ribbon-curl system
/// would be the natural next increment. Visual plausibility and the *direction*
/// of the cutting-parameter effect (the brief's actual bar) are both met.
/// </summary>
public partial class ChipSpawner : Node3D
{
    // World units are meters (WorldScale.MmToM) throughout the scene.
    [Export] public int MaxActiveChips = 250;
    // Not the true floor (WorldScale.FloorY, ~1.1m down) -- a nominal "lands
    // on/near the machine bed" catch height, since no chip tray/splash guard
    // is modelled; a real 1.1m freefall would look wrong for small swarf.
    [Export] public float FloorWorldY = -0.15f;
    [Export] public float Gravity = 9.8f; // real gravity, m/s^2, now that units are real-world meters
    [Export] public float MaxLifetimeSeconds = 6f;
    [Export] public float VisualSizeMultiplier = 6f;

    private struct ChipInstance
    {
        public MeshInstance3D Node;
        public Vector3 Velocity;
        public float AngularSpeed;
        public float Age;
        public bool Alive;
        public bool AtRest;
    }

    private ChipInstance[] _pool;
    private int _nextFreeHint;
    private readonly Random _rng = new();
    private float _spawnAccumulator;
    private StandardMaterial3D _sharedMaterialTemplate;

    public override void _Ready()
    {
        _pool = new ChipInstance[MaxActiveChips];
        for (int i = 0; i < MaxActiveChips; i++)
        {
            var mesh = new BoxMesh { Size = Vector3.One };
            var mi = new MeshInstance3D { Mesh = mesh, Visible = false };
            mi.MaterialOverride = new StandardMaterial3D();
            AddChild(mi);
            _pool[i] = new ChipInstance { Node = mi, Alive = false };
        }
    }

    /// <summary>Call every frame with the current chip formation (or null if not
    /// cutting) and the world-space contact point.</summary>
    public void Tick(double delta, ChipFormation? formation, Vector3 contactPointWorld)
    {
        if (formation is ChipFormation f && f.SpawnRatePerSecond > 0f)
        {
            _spawnAccumulator += f.SpawnRatePerSecond * (float)delta;
            while (_spawnAccumulator >= 1f)
            {
                SpawnOne(f, contactPointWorld);
                _spawnAccumulator -= 1f;
            }
        }

        for (int i = 0; i < _pool.Length; i++)
        {
            ref var c = ref _pool[i];
            if (!c.Alive)
                continue;

            c.Age += (float)delta;
            if (c.Age > MaxLifetimeSeconds)
            {
                c.Alive = false;
                c.Node.Visible = false;
                continue;
            }

            if (!c.AtRest)
            {
                c.Velocity.Y -= Gravity * (float)delta;
                var pos = c.Node.Position + c.Velocity * (float)delta;
                if (pos.Y <= FloorWorldY)
                {
                    pos.Y = FloorWorldY;
                    c.Velocity = Vector3.Zero;
                    c.AtRest = true;
                }
                c.Node.Position = pos;
                c.Node.RotateY(c.AngularSpeed * (float)delta);
            }
        }
    }

    private void SpawnOne(ChipFormation f, Vector3 contactPointWorld)
    {
        int slot = FindFreeSlot();
        ref var c = ref _pool[slot];

        // Scatter direction: outward-ish in the Y/Z cross-section plane (away
        // from the spindle centerline), with a little random spread and a
        // consistent upward bias so chips are visible arcing away from the cut.
        float angle = (float)(_rng.NextDouble() * Math.Tau);
        float speed = 0.15f + (float)_rng.NextDouble() * 0.25f; // m/s
        var dir = new Vector3(
            (float)(_rng.NextDouble() - 0.5) * 0.6f,
            Mathf.Abs(Mathf.Sin(angle)) * 0.7f + 0.3f,
            Mathf.Cos(angle) * 0.7f
        ).Normalized();

        c.Node.Position = contactPointWorld;
        c.Velocity = dir * speed;
        c.Age = 0f;
        c.Alive = true;
        c.AtRest = false;

        float elongation = f.Type switch
        {
            ChipType.Continuous => 4.0f,
            ChipType.Segmented => 2.0f,
            _ => 1.0f,
        };
        // VisualSizeMultiplier: real chip sizes (sub-mm to a few mm) are only a
        // handful of pixels at a camera distance that can see the whole scene
        // -- found live, the user correctly couldn't see any. This is a
        // deliberate visibility exaggeration for a training tool, not a claim
        // of physical accuracy (same spirit as the brief's "visual plausibility
        // is enough").
        float s = f.SizeMm * WorldScale.MmToM * VisualSizeMultiplier;
        c.Node.Scale = new Vector3(s * 0.4f, s * 0.2f, s * 0.4f * elongation);
        c.AngularSpeed = f.Type == ChipType.Continuous
            ? Mathf.DegToRad(180f + (float)_rng.NextDouble() * 360f)
            : Mathf.DegToRad((float)(_rng.NextDouble() - 0.5) * 180f);

        if (c.Node.MaterialOverride is StandardMaterial3D mat)
        {
            mat.AlbedoColor = f.Color;
            mat.Metallic = 0.6f;
            mat.Roughness = 1.0f - Mathf.Clamp(f.Gloss, 0f, 1f);
        }
        c.Node.Visible = true;
    }

    private int FindFreeSlot()
    {
        for (int tries = 0; tries < _pool.Length; tries++)
        {
            int idx = (_nextFreeHint + tries) % _pool.Length;
            if (!_pool[idx].Alive)
            {
                _nextFreeHint = (idx + 1) % _pool.Length;
                return idx;
            }
        }
        // Pool exhausted (hard upper limit reached): steal the oldest one.
        int oldest = 0;
        float oldestAge = -1f;
        for (int i = 0; i < _pool.Length; i++)
        {
            if (_pool[i].Age > oldestAge)
            {
                oldestAge = _pool[i].Age;
                oldest = i;
            }
        }
        return oldest;
    }
}
