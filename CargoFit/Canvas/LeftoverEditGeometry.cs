using System;
using System.Collections.Generic;
using System.Linq;

namespace CargoFit;

/// <summary>
/// Pure geometry for the manual leftover editor (Feature 2). No Avalonia dependency, so it is unit
/// testable on its own. All coordinates are container-world centimetres; the editor works top-down,
/// so a "footprint" is the box's X/Y rectangle and Z is height (gravity axis).
/// </summary>
internal static class LeftoverEditGeometry
{
    private const double Eps = 0.01;

    internal readonly record struct DropResult(bool Ok, double X, double Y, double Z);

    // ── overlap ────────────────────────────────────────────────────────────────
    private static bool Overlaps1D(double a, double aLen, double b, double bLen)
        => a < b + bLen - Eps && b < a + aLen - Eps;

    internal static bool FootprintsOverlap(
        double x1, double y1, double w1, double l1,
        double x2, double y2, double w2, double l2)
        => Overlaps1D(x1, w1, x2, w2) && Overlaps1D(y1, l1, y2, l2);

    // ── gravity ──────────────────────────────────────────────────────────────--
    /// <summary>Highest box-top under the footprint (where a dropped box comes to rest); 0 = floor.</summary>
    internal static double GravityZ(IEnumerable<BoxPlacement> others, double x, double y, double w, double l)
    {
        double z = 0;
        foreach (var o in others)
            if (FootprintsOverlap(x, y, w, l, o.X, o.Y, o.BW, o.BL))
                z = Math.Max(z, o.Z + o.BH);
        return z;
    }

    // ── support (no float) ──────────────────────────────────────────────────────
    /// <summary>
    /// True when the whole footprint is supported at <paramref name="restZ"/> — either it sits on the
    /// floor (restZ ≈ 0) or the tops of the boxes at that height fully cover it (no overhang / no gap).
    /// </summary>
    internal static bool IsFullySupported(
        IEnumerable<BoxPlacement> others, double x, double y, double w, double l, double restZ)
    {
        if (restZ <= Eps) return true;   // resting on the container floor

        var support = others
            .Where(o => Math.Abs(o.Z + o.BH - restZ) <= Eps
                     && FootprintsOverlap(x, y, w, l, o.X, o.Y, o.BW, o.BL))
            .ToList();
        if (support.Count == 0) return false;

        return RectCoveredByUnion(x, y, w, l, support);
    }

    // Exact "is rect fully covered by the union of these rects?" via coordinate compression.
    private static bool RectCoveredByUnion(double x, double y, double w, double l, List<BoxPlacement> rects)
    {
        double x2 = x + w, y2 = y + l;
        var xsSet = new SortedSet<double> { x, x2 };
        var ysSet = new SortedSet<double> { y, y2 };
        foreach (var r in rects)
        {
            xsSet.Add(Math.Clamp(r.X, x, x2));
            xsSet.Add(Math.Clamp(r.X + r.BW, x, x2));
            ysSet.Add(Math.Clamp(r.Y, y, y2));
            ysSet.Add(Math.Clamp(r.Y + r.BL, y, y2));
        }
        var xs = xsSet.ToList();
        var ys = ysSet.ToList();

        for (int i = 0; i < xs.Count - 1; i++)
        for (int j = 0; j < ys.Count - 1; j++)
        {
            if (xs[i + 1] - xs[i] < Eps || ys[j + 1] - ys[j] < Eps) continue;   // degenerate cell
            double cx = (xs[i] + xs[i + 1]) / 2, cy = (ys[j] + ys[j + 1]) / 2;
            bool covered = rects.Any(r =>
                cx > r.X - Eps && cx < r.X + r.BW + Eps &&
                cy > r.Y - Eps && cy < r.Y + r.BL + Eps);
            if (!covered) return false;
        }
        return true;
    }

    // ── snap ─────────────────────────────────────────────────────────────────--
    /// <summary>Snap X/Y to align flush with a nearby box/wall edge, else to a coarse grid; clamped to the container.</summary>
    internal static (double X, double Y) Snap(
        double x, double y, double w, double l, double contW, double contL,
        IEnumerable<BoxPlacement> others, double grid = 1.0, double edgeSnap = 3.0)
    {
        var list = others as ICollection<BoxPlacement> ?? others.ToList();
        double sx = SnapAxis(x, w, contW, list.SelectMany(o => new[] { o.X, o.X + o.BW }), grid, edgeSnap);
        double sy = SnapAxis(y, l, contL, list.SelectMany(o => new[] { o.Y, o.Y + o.BL }), grid, edgeSnap);
        sx = Math.Clamp(sx, 0, Math.Max(0, contW - w));
        sy = Math.Clamp(sy, 0, Math.Max(0, contL - l));
        return (sx, sy);
    }

    private static double SnapAxis(double v, double size, double cont, IEnumerable<double> edges, double grid, double edgeSnap)
    {
        double best = v, bestD = edgeSnap;
        foreach (var e in edges.Concat(new[] { 0.0, cont }))
            foreach (var cand in new[] { e, e - size })   // align near edge to e, or far edge to e
            {
                double d = Math.Abs(cand - v);
                if (d < bestD) { bestD = d; best = cand; }
            }
        return bestD < edgeSnap ? best : Math.Round(v / grid) * grid;
    }

    // ── full drop test ──────────────────────────────────────────────────────────
    /// <summary>Snap, then validate (inside container, under ceiling, fully supported). <paramref name="others"/> must exclude the box being moved.</summary>
    internal static DropResult TryDrop(
        IReadOnlyList<BoxPlacement> others,
        double x, double y, double w, double l, double h,
        double contW, double contL, double contH)
    {
        var (sx, sy) = Snap(x, y, w, l, contW, contL, others);

        if (sx < -Eps || sy < -Eps || sx + w > contW + Eps || sy + l > contL + Eps)
            return new DropResult(false, sx, sy, 0);

        double z = GravityZ(others, sx, sy, w, l);
        if (z + h > contH + Eps) return new DropResult(false, sx, sy, z);   // pokes through ceiling
        if (!IsFullySupported(others, sx, sy, w, l, z)) return new DropResult(false, sx, sy, z);

        return new DropResult(true, sx, sy, z);
    }

    // ── landing stack ───────────────────────────────────────────────────────--
    /// <summary>
    /// StackIndex of the box this footprint comes to rest on (largest-overlap support at restZ), so a
    /// moved leftover joins the "family" of the stack it now sits on. Falls back when on the floor.
    /// </summary>
    internal static int LandingStackIndex(
        IEnumerable<BoxPlacement> others, double x, double y, double w, double l, double restZ, int fallback)
    {
        if (restZ <= Eps) return fallback;   // resting on the floor — nothing to inherit from
        int best = fallback;
        double bestArea = 0;
        foreach (var o in others)
        {
            if (Math.Abs(o.Z + o.BH - restZ) > Eps) continue;
            double ox = Math.Min(x + w, o.X + o.BW) - Math.Max(x, o.X);
            double oy = Math.Min(y + l, o.Y + o.BL) - Math.Max(y, o.Y);
            if (ox <= 0 || oy <= 0) continue;
            double area = ox * oy;
            if (area > bestArea) { bestArea = area; best = o.StackIndex; }
        }
        return best;
    }

    // ── hit-test ─────────────────────────────────────────────────────────────--
    /// <summary>Index of the topmost editable (Scatter) box whose footprint contains the point and that is visible under the layer cut; -1 if none.</summary>
    internal static int PickTopScatter(IReadOnlyList<BoxPlacement> boxes, double wx, double wy, double cutZ)
    {
        int best = -1;
        double bestZ = double.MinValue;
        for (int i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            if (b.Kind != PlacementKind.Scatter) continue;
            if (b.Z >= cutZ - Eps) continue;
            if (wx >= b.X && wx <= b.X + b.BW && wy >= b.Y && wy <= b.Y + b.BL && b.Z > bestZ)
            {
                bestZ = b.Z;
                best = i;
            }
        }
        return best;
    }
}
