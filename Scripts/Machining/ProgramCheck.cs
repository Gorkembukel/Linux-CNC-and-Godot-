using System;
using System.Collections.Generic;
using CncTrainer.Bridge;

namespace CncTrainer.Machining;

/// <summary>A part of the setup a program's path can run into: the
/// fixture, the table, the chuck, the machine's own frame.</summary>
public interface IPathObstacle
{
    /// <summary>Why the machine at this position (machine coordinates, mm)
    /// would crash, or null.</summary>
    string CrashAt(AxisVector machinePosition);
}

/// <summary>Where a program's path first runs into the setup.</summary>
public readonly record struct PathConflict(int LineNumber, AxisVector WorkPosition, string Reason);

/// <summary>Walks a program's whole path (the control's preview of it)
/// against the setup before it runs, the way an operator checks the GRAPH
/// for rapids through the vise. Uses today's work and tool offsets for the
/// whole program. Engine-free.</summary>
public static class ProgramCheck
{
    /// <summary>Finest spacing of the checked points along the path (mm).</summary>
    public const float StepMm = 2f;
    /// <summary>Most points checked; a longer path is checked more coarsely.</summary>
    public const int MaxSamples = 20000;

    /// <param name="path">The program's moves, in work coordinates.</param>
    /// <param name="workOffset">Machine minus work position (G5x + G92 + tool offset).</param>
    /// <param name="startWork">Where the machine is now: the first move starts here.</param>
    public static PathConflict? FirstConflict(IReadOnlyList<PreviewSegment> path, AxisVector workOffset,
        AxisVector startWork, IReadOnlyList<IPathObstacle> obstacles)
    {
        if (path.Count == 0 || obstacles.Count == 0)
            return null;
        float total = 0f;
        for (int i = 0; i < path.Count; i++)
            total += Travel(i == 0 ? startWork : path[i].Start, path[i].End);
        float step = Math.Max(StepMm, total / MaxSamples);

        for (int i = 0; i < path.Count; i++)
        {
            var from = i == 0 ? startWork : path[i].Start;
            var to = path[i].End;
            int samples = Math.Max(1, (int)Math.Ceiling(Travel(from, to) / step));
            for (int k = 1; k <= samples; k++)
            {
                var work = from + (to - from) * ((float)k / samples);
                foreach (var obstacle in obstacles)
                    if (obstacle.CrashAt(work + workOffset) is string reason)
                        return new PathConflict(path[i].LineNumber, work, reason);
            }
        }
        return null;
    }

    private static float Travel(AxisVector a, AxisVector b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
