namespace CncTrainer;

/// <summary>
/// Single source of truth for the project's unit convention: **1 Godot unit =
/// 1 real meter.** Machine data from the bridge is in mm (LinuxCNC's own
/// units, per sim_lathe.ini); everything that turns that into a Godot
/// Position/Scale must go through MmToM here.
///
/// Why this exists, stated plainly: earlier code used raw mm values directly
/// as Godot units (a 150mm axis position became Position.Y = 150). That's
/// fine in isolation, but makes the scene physically absurd relative to
/// anything built at real-world scale -- a human character (planned: a
/// first/third-person controller that walks up to and interacts with the
/// machine) would need to be ~1800 units tall to match, and Godot's physics
/// defaults (gravity, character controller speeds, collision margins) all
/// assume roughly meter-scale units. Converting at the boundary instead of
/// changing the underlying mm-based data model (LatheRadiusProfile, the wire
/// protocol, ChipClassifier) keeps those pieces' existing tests/units intact.
/// </summary>
public static class WorldScale
{
    public const float MmToM = 0.001f;

    /// <summary>World Y of the floor, relative to the spindle centerline
    /// (world Y = 0, matching machine X = 0 = on-center). A small benchtop
    /// trainer lathe's spindle centerline sits roughly at standing work
    /// height above the floor.</summary>
    public const float FloorY = -1.1f;
}
