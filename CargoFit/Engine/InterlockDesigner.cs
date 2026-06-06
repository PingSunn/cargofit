using System;
using System.Collections.Generic;
using System.Linq;

namespace CargoFit;

// Result of auto-designing an interlock layer pattern for one box on a fixed container width.
// The last three fields are filled only in depth-aware mode (a container length/height was given).
internal sealed record InterlockResult(
    LayerSection[] PatternA,
    LayerSection[] PatternB,
    int    BoxesPerLayer,
    double UsedWidth,
    double Depth,
    double AreaUtil,    // boxes·w·l / (containerW · depth)  — squared-off-footprint utilisation
    double WidthUtil,   // usedWidth / containerW
    bool   Interlocks,
    string Template,
    int    LayersUp        = 0,   // floor(containerH / boxH)
    int    StacksDeep      = 0,   // floor(containerL / depth)
    int    TotalInContainer = 0); // BoxesPerLayer × LayersUp × StacksDeep

// Auto-generates an interlocking layer pattern for a box of size W×L on a container of interior
// width `containerW` (default 235 — every CargoFit container is 235 wide). Goal: maximise the area
// utilisation of ONE squared-off layer footprint while the boxes cross-lock —
//   • IN-PLANE: a layer mixes box orientations (upright + rotated bands, or a windmill) so they fill
//     the width and lock side-by-side, and
//   • BETWEEN LAYERS: PatternB swaps the band order so vertical seams cross (brick bond).
// Output is the same LayerSection[] model used by products.json, so the result feeds the packing
// engine unchanged (PlaceLayerAt / CountLayerCapacity). See the plan: Interlock Designer (Feature 1).
//
// Scoring ranks INTERLOCKING candidates first (the user wants "ขัดกันเอง"), then by area density,
// then box count, then squareness (small depth mismatch), then fewer sections. Pure grids are kept
// only as a fallback for boxes too large to interlock.
internal static class InterlockDesigner
{
    private const int    MaxRows   = 6;     // cap rows per band — keeps the unit shallow & loadable
    private const double DepthCapK = 3.2;   // stack depth ≤ DepthCapK × longest box side
    private const double Eps       = 0.01;

    public const double DefaultContainerW = 235.0;

    // The chosen pattern is the SAME for every container — all CargoFit containers are 235 wide, so one
    // product has ONE pattern for all of them. Optional containerL / containerH do NOT change the
    // pattern; they only report how many of it fit in that container (stacks tile the length, layers tile
    // the height) → LayersUp / StacksDeep / TotalInContainer ("ใส่ได้กี่ลัง/ตู้").
    internal static InterlockResult Generate(double w, double l, double h,
        double containerW = DefaultContainerW, double containerL = 0, double containerH = 0)
    {
        if (w <= 0 || l <= 0 || containerW <= 0) return Empty();

        double depthCap = DepthCapK * Math.Max(w, l);

        var cands = new List<Cand>();
        cands.AddRange(GridCandidates(w, l, containerW));
        cands.AddRange(TwoBandCandidates(w, l, containerW, depthCap));
        if (PinwheelCandidate(w, l, containerW, depthCap) is { } pin) cands.Add(pin);

        var best = cands
            .OrderByDescending(c => c.Interlocks)   // interlocking patterns win (the user wants "ขัด")
            .ThenByDescending(c => c.AreaUtil)       // densest squared-off footprint
            .ThenBy(c => c.Mismatch)                 // ties → squarer (bands depth-matched)
            .ThenBy(c => c.Depth)                    // ties → shallower unit (easier to load)
            .ThenByDescending(c => c.Boxes)
            .ThenBy(c => c.A.Length)
            .FirstOrDefault();

        if (best is null) return Empty();

        // Container only reports capacity of THIS (fixed) pattern — it never alters the pattern.
        int    stacks = containerL > 0 && best.Depth > 0 ? (int)((containerL + Eps) / best.Depth) : 0;
        int    up     = containerH > 0 && h > 0 ? (int)((containerH + Eps) / h) : 0;
        int    total  = best.Boxes * up * stacks;
        double widthUtil = best.UsedWidth / containerW;

        return new InterlockResult(best.A, best.B, best.Boxes, best.UsedWidth, best.Depth,
                                   best.AreaUtil, widthUtil, best.Interlocks, best.Template,
                                   up, stacks, total);
    }

    // ── Candidate model ───────────────────────────────────────────────────────────────────────
    private sealed record Cand(
        LayerSection[] A, LayerSection[] B, int Boxes,
        double UsedWidth, double Depth, double AreaUtil, double Mismatch,
        bool Interlocks, string Template);

    // box footprint along X (width) and Y (depth) for a given orientation
    private static double BoxX(double w, double l, bool rot) => rot ? l : w;
    private static double BoxY(double w, double l, bool rot) => rot ? w : l;

    private static Cand MakeCand(
        double w, double l, double containerW, LayerSection[] a, LayerSection[] b,
        int boxes, double usedWidth, double depth, double mismatch, bool interlocks, string template)
    {
        double areaUtil = depth > 0 ? boxes * w * l / (containerW * depth) : 0;
        return new Cand(a, b, boxes, usedWidth, depth, areaUtil, mismatch, interlocks, template);
    }

    // ── Pure grid (fallback, NOT interlocking) ───────────────────────────────────────────────
    private static IEnumerable<Cand> GridCandidates(double w, double l, double containerW)
    {
        foreach (bool rot in new[] { false, true })
        {
            double bx = BoxX(w, l, rot), by = BoxY(w, l, rot);
            int cols = (int)((containerW + Eps) / bx);
            if (cols < 1) continue;
            var a = new[] { new LayerSection(1, cols, rot) };
            yield return MakeCand(w, l, containerW, a, [], cols, cols * bx, by, 0, false,
                                  rot ? "grid-rotated" : "grid-upright");
        }
    }

    // ── Two-band split (interlocking; PatternB = swapped bands → vertical cross) ───────────────
    // Joint search over the width split (how many upright vs rotated columns fill the 235 width) AND
    // the rows per band (depth-matching the two bands so the footprint squares off). The scorer keeps
    // the densest. Both are needed together: the best rows depend on the split and vice-versa.
    private static IEnumerable<Cand> TwoBandCandidates(double w, double l, double containerW, double depthCap)
    {
        var seen = new HashSet<(int, int, int, int)>();

        foreach (var (colsUp, colsRot) in WidthSplits(w, l, containerW))
        for (int rowsUp = 1; rowsUp <= MaxRows; rowsUp++)
        for (int rowsRot = 1; rowsRot <= MaxRows; rowsRot++)
        {
            double dU = rowsUp * l, dR = rowsRot * w;   // upright depth = rows·L; rotated depth = rows·W
            double depth = Math.Max(dU, dR);
            if (depth > depthCap + Eps) continue;
            if (!seen.Add((colsUp, rowsUp, colsRot, rowsRot))) continue;

            var up = new LayerSection(rowsUp, colsUp, false);
            var rt = new LayerSection(rowsRot, colsRot, true);
            int    boxes = colsUp * rowsUp + colsRot * rowsRot;
            double used  = colsUp * w + colsRot * l;
            yield return MakeCand(w, l, containerW, [up, rt], [rt, up],   // B = swapped order → seams cross
                                  boxes, used, depth, Math.Abs(dU - dR), true, "two-band");
        }
    }

    // Candidate width splits: greedily fill the leftover width after fixing one band's column count —
    // once leading with rotated columns, once with upright — so both "few wide rotated + many upright"
    // and the reverse are explored. Returns distinct (colsUp, colsRot) pairs, both ≥ 1 (a real split).
    private static IEnumerable<(int colsUp, int colsRot)> WidthSplits(double w, double l, double containerW)
    {
        var seen = new HashSet<(int, int)>();
        int maxRot = (int)((containerW + Eps) / l);
        for (int cr = 1; cr <= maxRot; cr++)
        {
            int cu = (int)((containerW - cr * l + Eps) / w);
            if (cu >= 1 && seen.Add((cu, cr))) yield return (cu, cr);
        }
        int maxUp = (int)((containerW + Eps) / w);
        for (int cu = 1; cu <= maxUp; cu++)
        {
            int cr = (int)((containerW - cu * w + Eps) / l);
            if (cr >= 1 && seen.Add((cu, cr))) yield return (cu, cr);
        }
    }

    // ── Pinwheel windmill (interlocking in-plane; same layer every level → PatternB empty) ─────
    private static Cand? PinwheelCandidate(double w, double l, double containerW, double depthCap)
    {
        double side = w + l;
        if (side > depthCap + Eps) return null;   // windmill is (W+L) deep — respect the depth cap
        int units = (int)((containerW + Eps) / side);
        if (units < 1) return null;
        var a = new[] { new LayerSection(0, 0, false, Pinwheel: true) };
        int boxes = 4 * units;
        return MakeCand(w, l, containerW, a, [], boxes, units * side, side, Math.Abs(l - w), true, "pinwheel");
    }

    private static InterlockResult Empty() =>
        new([], [], 0, 0, 0, 0, 0, false, "none");
}
