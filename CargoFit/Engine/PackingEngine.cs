using System;
using System.Collections.Generic;
using System.Linq;

namespace CargoFit;

internal record struct ContainerDims(double W, double L, double H);
internal record struct PlaceResult(int Packed, double EndY, int FullStacks, int PartialBoxes);
internal record struct PackInfo(ProductSpec Spec, int ProductIndex, int Requested, double StartY, PlaceResult Result, bool HasPattern);

internal sealed class PackingOutput
{
    internal List<BoxPlacement>  Placements { get; }
    internal List<PackInfo>      PackInfos  { get; }
    internal Dictionary<int,int> MixedMap   { get; }
    internal Dictionary<int,int> CondoMap   { get; }
    internal Dictionary<int,int> ScatterMap { get; }

    internal PackingOutput(
        List<BoxPlacement> placements, List<PackInfo> packInfos,
        Dictionary<int,int> mixedMap, Dictionary<int,int> condoMap,
        Dictionary<int,int> scatterMap)
    {
        Placements = placements;
        PackInfos  = packInfos;
        MixedMap   = mixedMap;
        CondoMap   = condoMap;
        ScatterMap = scatterMap;
    }
}

internal static class PackingEngine
{
    internal const int    CondoStackBase = 1000;
    internal const int    MixedStackBase = 2000;

    internal static PackingOutput Calculate(
        ContainerSpec container,
        IReadOnlyList<(ProductSpec Spec, int Qty)> requests)
    {
        var dims = new ContainerDims(container.InteriorW, container.InteriorL, container.InteriorH);
        var placements = new List<BoxPlacement>();

        // ── Input summary ─────────────────────────────────────────────────────
        if (PackingLog.IsEnabled)
        {
            PackingLog.Phase("INPUT");
            PackingLog.Info($"Container : {container.Name} {container.SizeLabel}  interior={dims.W}×{dims.L}×{dims.H} cm");
            PackingLog.Info($"Requests  : {requests.Count} products");
            for (int i = 0; i < requests.Count; i++)
            {
                var (s, qty) = requests[i];
                bool hp = s.PatternA is { Length: > 0 };
                PackingLog.Info($"  [{i}] {s.Description} {s.Content} {s.PackSize}  qty={qty}  box={s.W}×{s.L}×{s.H}  maxLayers={s.MaxLayers}  hasPattern={hp}");
            }
        }

        var effectiveLayers = ComputeEffectiveLayers(dims, requests);
        if (PackingLog.IsEnabled)
            PackingLog.Info($"EffectiveLayers: {string.Join("  ", effectiveLayers.Select((v, i) => $"[{i}]={v}"))}");

        // ── Filler reservation ────────────────────────────────────────────────
        // When ≥2 patterned SKUs and one is a small "filler" — its whole qty fits as a thin condo
        // wall (qty ≤ perRow × ceilingRows) — reserve it for a PURE condo wall (loaded innermost),
        // letting the heavier "bulk" SKUs fill primary completely. The filler's own leftover then
        // rides on top of the last/shortest bulk row (cross-SKU top mix). Picks the lightest such SKU.
        int reservedIdx = SelectFillerIndex(dims, requests);
        if (PackingLog.IsEnabled)
            PackingLog.Info($"FillerReserved: {(reservedIdx >= 0 ? $"[{reservedIdx}] {requests[reservedIdx].Spec.Description} {requests[reservedIdx].Spec.Content}" : "none")}");

        // ── Primary ───────────────────────────────────────────────────────────
        PackingLog.Phase("PRIMARY PACKING");
        var packInfos = RunPrimaryPacking(dims, requests, placements, out double currentY, effectiveLayers, reservedIdx);
        if (PackingLog.IsEnabled)
            PackingLog.Info($"PrimaryDone: currentY={currentY:F1}  placed={placements.Count}");

        // ── Balancing ─────────────────────────────────────────────────────────
        PackingLog.Phase("BALANCING");
        RunBalancing(packInfos, dims, placements, ref currentY, effectiveLayers, reservedIdx);
        if (PackingLog.IsEnabled)
            PackingLog.Info($"BalancingDone: currentY={currentY:F1}  placed={placements.Count}");

        // ── Layer balancing ───────────────────────────────────────────────────
        PackingLog.Phase("LAYER BALANCING");
        RunLayerBalancing(packInfos, dims, placements);
        if (PackingLog.IsEnabled)
            PackingLog.Info($"LayerBalancingDone: placed={placements.Count}");

        // ── Partial removal ───────────────────────────────────────────────────
        PackingLog.Phase("PARTIAL REMOVAL");
        int beforeRemoval = placements.Count;
        RunPartialRemoval(packInfos, dims, placements, ref currentY);
        if (PackingLog.IsEnabled)
            PackingLog.Info($"Removed {beforeRemoval - placements.Count} boxes  currentY={currentY:F1}");

        SortStacksByHeight(packInfos, placements, dims);

        Dictionary<int,int> condoMap, scatterMap;
        if (reservedIdx >= 0)
        {
            // ── Filler path: PURE condo wall for the reserved SKU (to ceiling, no bulk mixed in),
            //    then its own leftover rides on top of the last/shortest bulk row. ──
            PackingLog.Phase("FILLER CONDO PLACEMENT");
            condoMap   = new Dictionary<int,int>();
            scatterMap = new Dictionary<int,int>();
            var reservedInfo = packInfos[reservedIdx];

            int inCondo = RunFillerCondo(reservedInfo, dims, currentY, placements);
            if (inCondo > 0) condoMap[reservedIdx] = inCondo;

            int leftover = reservedInfo.Requested - inCondo;
            int onBulk   = leftover > 0 ? PlaceFillerLeftoverOnBulk(reservedInfo, dims, placements, leftover) : 0;
            if (onBulk > 0) scatterMap[reservedIdx] = onBulk;

            packInfos[reservedIdx] = reservedInfo with { Result = reservedInfo.Result with { Packed = inCondo + onBulk } };
            if (PackingLog.IsEnabled)
                PackingLog.Info($"  filler [{reservedIdx}]: condo={inCondo}  onBulk={onBulk}  lost={reservedInfo.Requested - inCondo - onBulk}");
        }
        else
        {
            // ── Condo (ONE wall: complete single-SKU rows CBM bottom-up + remainder on top; kept only if it
            //    stands within ≤1 step of the innermost block, else every leftover scatters on its own stacks) ──
            PackingLog.Phase("CONDO PLACEMENT");
            if (PackingLog.IsEnabled)
                PackingLog.Info($"CondoStartY={currentY:F1}  available={dims.L - currentY:F1} cm");
            condoMap = RunCondoPlacement(packInfos, dims, currentY, placements);
            if (PackingLog.IsEnabled)
                foreach (var (k, v) in condoMap)
                    PackingLog.Info($"  [{k}] {requests[k].Spec.Description} {requests[k].Spec.Content}: condoPlaced={v}");

            // ── Scatter (condo overflow / discarded-condo leftovers → piled on own stacks up to the ceiling) ──
            PackingLog.Phase("SCATTER TOP PLACEMENT");
            scatterMap = RunScatteredTopPlacement(packInfos, dims, placements);
            if (PackingLog.IsEnabled)
                foreach (var (k, v) in scatterMap)
                    PackingLog.Info($"  [{k}] {requests[k].Spec.Description} {requests[k].Spec.Content}: scatterPlaced={v}");
        }

        // ── Move condo to innermost (Y=0) ─────────────────────────────────────
        // Primary stacks fill from condoDepth toward the door (high Y).
        // Condo sits at Y=0 (back wall / innermost) — loaded first.
        PackingLog.Phase("MOVE CONDO TO INNERMOST");
        MoveCandoToInnermost(placements);
        if (PackingLog.IsEnabled && placements.Count > 0)
            PackingLog.Info($"  After swap: BBoxY=[{placements.Min(p => p.Y):F1}, {placements.Max(p => p.Y + p.BL):F1}]");

        // ── Output summary ────────────────────────────────────────────────────
        if (PackingLog.IsEnabled)
        {
            PackingLog.Phase("OUTPUT SUMMARY");
            double containerCbm = dims.W * dims.L * dims.H / 1_000_000.0;
            double usedCbm      = placements.Sum(p => p.BW * p.BL * p.BH / 1_000_000.0);
            double pct          = containerCbm > 0 ? usedCbm / containerCbm * 100 : 0;
            PackingLog.Info($"Total placements: {placements.Count}  CBM: {usedCbm:F3}/{containerCbm:F3} ({pct:F1}%)");
            foreach (var info in packInfos)
            {
                int pri     = placements.Count(p => p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase);
                int condo   = condoMap.GetValueOrDefault(info.ProductIndex, 0);
                int scatter = scatterMap.GetValueOrDefault(info.ProductIndex, 0);
                PackingLog.Info($"  [{info.ProductIndex}] {info.Spec.Description} {info.Spec.Content}  packed={info.Result.Packed}  primary={pri}  condo={condo}  scatter={scatter}");
            }
            if (placements.Count > 0)
                PackingLog.Info($"  BBox: maxX={placements.Max(p => p.X + p.BW):F1}  maxY={placements.Max(p => p.Y + p.BL):F1}  maxZ={placements.Max(p => p.Z + p.BH):F1}");
        }

        return new PackingOutput(placements, packInfos, new Dictionary<int,int>(), condoMap, scatterMap);
    }

    private static List<PackInfo> RunPrimaryPacking(
        ContainerDims dims, IReadOnlyList<(ProductSpec Spec, int Qty)> requests,
        List<BoxPlacement> placements, out double currentY, int[] effectiveLayers, int reservedIdx = -1)
    {
        var packInfos = new List<PackInfo>();
        currentY = 0;

        for (int i = 0; i < requests.Count; i++)
        {
            var (spec, requested) = requests[i];
            bool hasPattern = spec.PatternA is { Length: > 0 };
            double productStartY = currentY;

            if (PackingLog.IsEnabled)
                PackingLog.Info($"[{i}] {spec.Description} {spec.Content}  startY={productStartY:F1}  effectiveLayers={effectiveLayers[i]}  hasPattern={hasPattern}");

            PlaceResult r;
            if (i == reservedIdx)
            {
                r = new PlaceResult(0, currentY, 0, 0);   // reserved filler → packed in the condo phase
            }
            else if (hasPattern)
            {
                // When a filler is reserved, bulk SKUs must fill primary completely (partial last wall)
                // so nothing spills into the filler's condo.
                r = PlaceProduct(dims, spec, requested, currentY, i, placements,
                                 overrideMaxLayers: effectiveLayers[i], fillCompletely: reservedIdx >= 0);
                currentY = r.EndY;
            }
            else
            {
                r = new PlaceResult(0, currentY, 0, 0);
            }

            if (PackingLog.IsEnabled)
            {
                if (hasPattern)
                    PackingLog.Info($"  → placed={r.Packed}  endY={r.EndY:F1}  fullStacks={r.FullStacks}  partial={r.PartialBoxes}");
                else
                    PackingLog.Info($"  → no pattern, skipped");
            }

            packInfos.Add(new PackInfo(spec, i, requested, productStartY, r, hasPattern));
        }

        return packInfos;
    }

    private static void RunBalancing(
        List<PackInfo> packInfos, ContainerDims dims,
        List<BoxPlacement> placements, ref double currentY, int[] effectiveLayers, int reservedIdx = -1)
    {
        // ── Step A: Fill free Y with more primary stacks ──────────────────────
        // Leave 50 cm for condo/mixed; keep adding stacks while space remains.
        var fillDims = dims with { L = dims.L - 50.0 };

        if (PackingLog.IsEnabled)
            PackingLog.Info($"StepA (fill Y): currentY={currentY:F1}  fillLimit={fillDims.L:F1}");

        bool anyAdded = true;
        while (anyAdded && fillDims.L - currentY > 0)
        {
            anyAdded = false;

            var withRem = packInfos
                .Where(info => info.HasPattern && info.ProductIndex != reservedIdx)
                .Select(info => {
                    int primary = placements.Count(p =>
                        p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase);
                    return (info, rem: info.Requested - primary);
                })
                .Where(x => x.rem > 0)
                .OrderByDescending(x => x.info.Spec.Cbm)
                .ToList();

            foreach (var (info, rem) in withRem)
            {
                if (fillDims.L - currentY <= 0) break;

                // Skip if the next stack position has reduced capacity (truncated pattern at container edge).
                int fullBpl = CountLayerCapacity(info.Spec.PatternA!, info.Spec, fillDims, info.StartY);
                if (CountLayerCapacity(info.Spec.PatternA!, info.Spec, fillDims, currentY) < fullBpl) continue;

                int nextSI = placements
                    .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase)
                    .Select(p => p.StackIndex)
                    .DefaultIfEmpty(-1)
                    .Max() + 1;

                var r = PlaceProduct(fillDims, info.Spec, rem, currentY,
                                     info.ProductIndex, placements, nextSI,
                                     overrideMaxLayers: effectiveLayers[info.ProductIndex],
                                     fillCompletely: reservedIdx >= 0);
                if (r.Packed > 0)
                {
                    if (PackingLog.IsEnabled)
                        PackingLog.Info($"  [{info.ProductIndex}] added stack{nextSI}  placed={r.Packed}  newEndY={r.EndY:F1}  rem→{rem - r.Packed}");
                    currentY = Math.Max(currentY, r.EndY);
                    anyAdded = true;
                }
            }
        }

        // ── Step B: Global height balance (average target, ±1 layer) ──────────
        if (PackingLog.IsEnabled)
            PackingLog.Info($"StepB (height balance): targets={string.Join("  ", effectiveLayers.Select((v, i) => $"[{i}]={v}"))}");

        var active = packInfos
            .Where(info => info.HasPattern && info.ProductIndex != reservedIdx &&
                   placements.Any(p => p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase))
            .ToList();

        if (active.Count > 0)
        {
            foreach (var info in active)
            {
                int targetLayers = effectiveLayers[info.ProductIndex];
                if (targetLayers <= 0) continue;

                var stackIndices = placements
                    .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase)
                    .Select(p => p.StackIndex).Distinct().OrderBy(si => si).ToList();

                int primaryPacked = placements.Count(p =>
                    p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase);

                foreach (int si in stackIndices)
                {
                    int current = placements
                        .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex == si)
                        .Select(p => p.LayerIndex).DefaultIfEmpty(-1).Max() + 1;

                    if (current > targetLayers)
                    {
                        int before = placements.Count(p =>
                            p.ProductIndex == info.ProductIndex && p.StackIndex == si);
                        placements.RemoveAll(p =>
                            p.ProductIndex == info.ProductIndex &&
                            p.StackIndex   == si &&
                            p.LayerIndex   >= targetLayers);
                        int after = placements.Count(p =>
                            p.ProductIndex == info.ProductIndex && p.StackIndex == si);
                        int trimmed = before - after;
                        primaryPacked -= trimmed;
                        if (PackingLog.IsEnabled)
                            PackingLog.Info($"  [{info.ProductIndex}] stack{si}: {current}→{targetLayers} layers (trimmed {trimmed})");
                    }
                    else if (current < targetLayers)
                    {
                        double stackY = placements
                            .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex == si)
                            .Min(p => p.Y);
                        bool flipIt = (si % 2 == 1) && info.Spec.PatternB is { Length: > 0 };

                        for (int layer = current; layer < targetLayers; layer++)
                        {
                            double z = layer * info.Spec.H;
                            if (z + info.Spec.H > dims.H + 0.01) break;

                            bool useA    = flipIt ? (layer % 2 == 1) : (layer % 2 == 0);
                            var sections = useA ? info.Spec.PatternA! : (info.Spec.PatternB ?? info.Spec.PatternA)!;

                            int capacity = CountLayerCapacity(sections, info.Spec, dims, stackY);
                            if (capacity <= 0) break;
                            if (info.Requested - primaryPacked < capacity) break;

                            PlaceLayerAt(sections, info.Spec, dims, stackY, z, capacity,
                                         info.ProductIndex, placements, si, layer);
                            primaryPacked += capacity;
                        }
                    }
                }
            }
        }

        // ── Step C: Sync PackInfo.Result.Packed ───────────────────────────────
        if (PackingLog.IsEnabled)
            PackingLog.Info("StepC (sync counts):");
        for (int i = 0; i < packInfos.Count; i++)
        {
            var info = packInfos[i];
            if (!info.HasPattern) continue;
            int actual = placements.Count(p =>
                p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase);
            if (PackingLog.IsEnabled)
                PackingLog.Info($"  [{info.ProductIndex}] {info.Spec.Description} {info.Spec.Content}: packed={info.Result.Packed}→{actual}  rem={info.Requested - actual}");
            packInfos[i] = info with { Result = info.Result with { Packed = actual } };
        }
    }

    private static void RunPartialRemoval(
        List<PackInfo> packInfos, ContainerDims dims,
        List<BoxPlacement> placements, ref double currentY)
    {
        if (dims.L - currentY >= 50.0) return;

        foreach (var info in packInfos)
        {
            if (!info.HasPattern) continue;
            var stacks = placements
                .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase)
                .GroupBy(p => p.StackIndex)
                .OrderBy(g => g.Key)
                .Select(g => (g.Key, Layers: g.Max(p => p.LayerIndex) + 1))
                .ToList();
            if (stacks.Count < 2 || stacks[^1].Layers >= stacks[^2].Layers - 1) continue;

            int maxSI    = stacks[^1].Key;
            int removing = placements.Count(p => p.ProductIndex == info.ProductIndex && p.StackIndex == maxSI);
            if (PackingLog.IsEnabled)
                PackingLog.Info($"  [{info.ProductIndex}] {info.Spec.Description}: stack{maxSI} layers={stacks[^1].Layers} < prev={stacks[^2].Layers-1}  removing {removing} boxes");
            placements.RemoveAll(p => p.ProductIndex == info.ProductIndex && p.StackIndex == maxSI);
        }

        currentY = placements.Count > 0 ? placements.Max(p => p.Y + p.BL) + 0.1 : 0;

        for (int i = 0; i < packInfos.Count; i++)
        {
            var info = packInfos[i];
            if (!info.HasPattern) continue;
            int actual = placements.Count(p =>
                p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase);
            packInfos[i] = info with { Result = info.Result with { Packed = actual } };
        }
    }

    private static Dictionary<int,int> RunCondoPlacement(
        List<PackInfo> packInfos, ContainerDims dims,
        double condoAreaStart, List<BoxPlacement> placements)
    {
        var condoMap = new Dictionary<int,int>();

        // Leftover queue: products with unplaced boxes, highest box-CBM first (heaviest/most-volume
        // forms the BASE). rem[] tracks how many of each remain.
        var leftover = packInfos
            .Where(info => info.HasPattern && info.Requested - info.Result.Packed > 0)
            .Select(info => (info, rem: info.Requested - info.Result.Packed))
            .OrderByDescending(x => x.info.Spec.Cbm)
            .ThenByDescending(x => x.info.Spec.H)
            .ThenByDescending(x => x.rem)
            .ToList();
        if (leftover.Count == 0) return condoMap;

        var queue = leftover.Select(x => x.info).ToList();
        var rem   = leftover.Select(x => x.rem).ToArray();

        // Height cap = top of the innermost primary block (lowest-Y zone = highest total-CBM product
        // after SortStacksByHeight). The condo must stay within ONE step (≤1 layer) of it so the
        // back-wall profile stays squared off. Overflow that can't fit under this cap is left in rem
        // and handled by the scatter phase (piled on top of the product's own stacks).
        var innermost = placements
            .Where(p => p.StackIndex < CondoStackBase)
            .GroupBy(p => p.ProductIndex)
            .Select(g => new { MinY = g.Min(p => p.Y), TopZ = g.Max(p => p.Z + p.BH), BoxH = g.Max(p => p.BH) })
            .OrderBy(s => s.MinY)
            .FirstOrDefault();
        if (innermost is null) return condoMap;
        double targetCondoZ = innermost.TopZ;
        if (targetCondoZ <= 0) return condoMap;

        double colDepth = queue.Max(info => info.Spec.L); // wall depth in Y (one box deep)
        if (condoAreaStart + colDepth > dims.L + 0.01) return condoMap; // no room for a wall → scatter all

        // There is only ONE condo — a single wall (one Y-slice, full container width) against the back
        // wall, built bottom-up into a temp list and committed only if it stands tall enough (see gate
        // below). Two phases:
        //   1. COMPLETE full-width single-SKU rows, bottom-up in CBM order — heaviest product on the
        //      floor. Each row is one product at a uniform height, so its top is flat and supports the
        //      next row (no floating, the failure of the old mixed-height rows).
        //   2. The leftover REMAINDER of every product (boxes that can't complete a full row) is then
        //      combined into the row(s) on top, again in CBM order left→right.
        // Both phases stop at the ≤1-step cap (targetCondoZ); anything still unplaced spills to scatter.
        var condoBoxes = new List<BoxPlacement>();
        double condoY  = condoAreaStart;
        double z   = 0;
        int    row = 0;

        // ── Phase 1: complete single-SKU rows, bottom-up by CBM ──
        for (int q = 0; q < queue.Count; q++)
        {
            var spec = queue[q].Spec;
            if (spec.W > dims.W + 0.01) continue;            // box wider than container → remainder/scatter
            int perRow = (int)((dims.W + 0.01) / spec.W);    // boxes that fit across the width
            if (perRow < 1) continue;

            while (rem[q] >= perRow && z + spec.H <= targetCondoZ + 0.01)
            {
                for (int c = 0; c < perRow; c++)
                    condoBoxes.Add(new BoxPlacement(
                        c * spec.W, condoY, z, spec.W, spec.L, spec.H,
                        queue[q].ProductIndex, false, CondoStackBase + queue[q].ProductIndex, row));
                condoMap[queue[q].ProductIndex] = condoMap.GetValueOrDefault(queue[q].ProductIndex) + perRow;
                rem[q] -= perRow;
                z += spec.H;
                row++;
            }
        }

        // ── Phase 2: combine the remainders into the top row(s), CBM order left→right ──
        double x = 0, rowMaxH = 0;
        for (int q = 0; q < queue.Count; q++)
        {
            var spec = queue[q].Spec;
            if (spec.W > dims.W + 0.01) continue;
            while (rem[q] > 0)
            {
                if (x + spec.W > dims.W + 0.01) { z += rowMaxH; x = 0; rowMaxH = 0; row++; } // wrap row
                if (z + spec.H > targetCondoZ + 0.01) break;                                 // cap → overflow→scatter
                condoBoxes.Add(new BoxPlacement(
                    x, condoY, z, spec.W, spec.L, spec.H,
                    queue[q].ProductIndex, false, CondoStackBase + queue[q].ProductIndex, row));
                condoMap[queue[q].ProductIndex] = condoMap.GetValueOrDefault(queue[q].ProductIndex) + 1;
                rem[q]--;
                x += spec.W;
                rowMaxH = Math.Max(rowMaxH, spec.H);
            }
        }

        // ── Gate: build the condo only if it stands CLOSE to the innermost block ──
        // The condo is worth a back wall only when it rises near the innermost block (within one box
        // layer of that block's top). If the leftover is too little to raise it that high, a short stub
        // against the back wall is pointless — discard it entirely and let EACH leftover pile on top of
        // its OWN product's stacks (scatter). User rule (2026-06): "ถ้า condo ทำให้สูงใกล้เคียง block แรก
        // ไม่ได้ ให้เอาเศษแต่ละอันไปวางบน stack ของสินค้านั้น ๆ" + "condo จะมีแค่อันเดียว".
        if (condoBoxes.Count == 0) return condoMap;
        double condoTopZ = condoBoxes.Max(p => p.Z + p.BH);
        if (targetCondoZ - condoTopZ > innermost.BoxH + 0.01)
        {
            condoMap.Clear();
            return condoMap; // not tall enough → every leftover scatters onto its own stacks
        }

        placements.AddRange(condoBoxes);
        return condoMap;
    }

    private static Dictionary<int,int> RunScatteredTopPlacement(
        List<PackInfo> packInfos, ContainerDims dims, List<BoxPlacement> placements)
    {
        var scatterMap = new Dictionary<int,int>();

        foreach (var info in packInfos)
        {
            if (!info.HasPattern) continue;

            int placedSoFar = placements.Count(p => p.ProductIndex == info.ProductIndex);
            int rem         = info.Requested - placedSoFar;
            if (rem <= 0) continue;

            // Scatter tops up the product's OWN stacks shortest-first (keeps them within ±1 layer).
            // Condo overflow (boxes that didn't fit under the ≤1-step cap) AND the leftovers of a
            // discarded condo (a wall too short to stand near the innermost block) are piled here on top
            // of the stacks, filling the empty space up to the CEILING. MaxLayers is intentionally NOT
            // enforced — the user's rule is "if the condo can't stand near block-1, put each leftover on
            // top of its own stack" — so nothing drops while the container still has headroom.
            int ceilLayers  = (int)Math.Floor(dims.H / info.Spec.H);
            int maxLayerCap = ceilLayers;
            if (maxLayerCap <= 0) continue;

            var stacks = placements
                .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase)
                .GroupBy(p => p.StackIndex)
                .Select(g => (
                    SI:       g.Key,
                    StackY:   g.Min(p => p.Y),
                    TopLayer: g.Max(p => p.LayerIndex) + 1))
                .Where(s => s.TopLayer < maxLayerCap && s.TopLayer * info.Spec.H + info.Spec.H <= dims.H + 0.01)
                .Select(s => new ScatterStack(s.SI, s.StackY, s.TopLayer, s.TopLayer * info.Spec.H))
                .ToList();

            while (rem > 0 && stacks.Count > 0)
            {
                int pick = 0;
                for (int i = 1; i < stacks.Count; i++)
                {
                    double dz = stacks[i].TopZ - stacks[pick].TopZ;
                    if (dz < -0.001 || (dz < 0.001 && stacks[i].SI < stacks[pick].SI))
                        pick = i;
                }
                var s = stacks[pick];

                bool flipIt  = (s.SI % 2 == 1) && info.Spec.PatternB is { Length: > 0 };
                bool useA    = flipIt ? (s.TopLayer % 2 == 1) : (s.TopLayer % 2 == 0);
                var sections = useA ? info.Spec.PatternA! : (info.Spec.PatternB ?? info.Spec.PatternA)!;

                int cap = CountLayerCapacity(sections, info.Spec, dims, s.StackY);
                if (cap <= 0) { stacks.RemoveAt(pick); continue; }

                int take = Math.Min(cap, rem);
                int n = PlaceLayerAt(sections, info.Spec, dims, s.StackY, s.TopZ, take,
                                     info.ProductIndex, placements, s.SI, s.TopLayer);
                if (n <= 0) { stacks.RemoveAt(pick); continue; }

                rem -= n;
                scatterMap[info.ProductIndex] = scatterMap.GetValueOrDefault(info.ProductIndex) + n;

                int    nextLayer = s.TopLayer + 1;
                double nextZ     = s.TopZ + info.Spec.H;
                if (nextLayer >= maxLayerCap || nextZ + info.Spec.H > dims.H + 0.01)
                    stacks.RemoveAt(pick);
                else
                    stacks[pick] = new ScatterStack(s.SI, s.StackY, nextLayer, nextZ);
            }
        }

        return scatterMap;
    }

    // Builds a PURE single-SKU condo wall for the reserved filler — one Y-slice (1 box deep) against
    // what becomes the back wall, full container width, stacked in COMPLETE rows up to the ceiling.
    // No other SKU is mixed in and there is no innermost-block height cap (unlike RunCondoPlacement),
    // so the filler reaches its natural height (e.g. Mogu320 → 9/row × 13 rows = 117). The partial
    // remainder is left for PlaceFillerLeftoverOnBulk. Returns the number of boxes placed.
    private static int RunFillerCondo(
        PackInfo info, ContainerDims dims, double condoAreaStart, List<BoxPlacement> placements)
    {
        var spec = info.Spec;
        if (spec.W > dims.W + 0.01 || spec.H <= 0) return 0;

        int perRow = (int)((dims.W + 0.01) / spec.W);
        if (perRow < 1) return 0;
        if (condoAreaStart + spec.L > dims.L + 0.01) return 0;   // no depth left for a wall

        int    rem = info.Requested;
        int    placed = 0, row = 0;
        double z = 0;
        int    si = CondoStackBase + info.ProductIndex;

        while (rem >= perRow && z + spec.H <= dims.H + 0.01)
        {
            for (int c = 0; c < perRow; c++)
                placements.Add(new BoxPlacement(
                    c * spec.W, condoAreaStart, z, spec.W, spec.L, spec.H,
                    info.ProductIndex, false, si, row));
            placed += perRow;
            rem    -= perRow;
            z      += spec.H;
            row++;
        }
        return placed;
    }

    // Places the filler's leftover (boxes too few to complete a condo row) on TOP of the SHORTEST
    // bulk stack — the last/door-side row of the staircase — as a partial layer (cross-SKU top mix:
    // "ผสมในแถว 6"). Attached to that bulk stack's StackIndex so it shifts with it in the final move.
    // Returns boxes placed.
    private static int PlaceFillerLeftoverOnBulk(
        PackInfo info, ContainerDims dims, List<BoxPlacement> placements, int count)
    {
        var spec = info.Spec;
        if (count <= 0 || spec.W > dims.W + 0.01) return 0;

        // Shortest bulk stack (lowest top Z); tie → closest to the door (max Y).
        var bulk = placements
            .Where(p => p.StackIndex < CondoStackBase && p.ProductIndex != info.ProductIndex)
            .GroupBy(p => p.StackIndex)
            .Select(g => new
            {
                SI       = g.Key,
                StackY   = g.Min(p => p.Y),
                TopZ     = g.Max(p => p.Z + p.BH),
                TopLayer = g.Max(p => p.LayerIndex) + 1
            })
            .OrderBy(s => s.TopZ).ThenByDescending(s => s.StackY)
            .FirstOrDefault();
        if (bulk is null) return 0;

        int perRow = (int)((dims.W + 0.01) / spec.W);
        if (perRow < 1) return 0;

        int    placed = 0, layer = bulk.TopLayer;
        double z = bulk.TopZ;
        int    rem = count;
        while (rem > 0 && z + spec.H <= dims.H + 0.01)
        {
            int n = Math.Min(perRow, rem);
            for (int c = 0; c < n; c++)
                placements.Add(new BoxPlacement(
                    c * spec.W, bulk.StackY, z, spec.W, spec.L, spec.H,
                    info.ProductIndex, false, bulk.SI, layer));
            placed += n;
            rem    -= n;
            z      += spec.H;
            layer++;
        }
        return placed;
    }

    /// <summary>
    /// Moves all condo boxes to Y=0 (innermost/back wall) and shifts primary stacks
    /// (including scatter) upward by the condo depth so they fill from condoDepth toward
    /// the door. No-op when there are no condo boxes.
    /// </summary>
    private static void MoveCandoToInnermost(List<BoxPlacement> placements)
    {
        if (placements.Count == 0) return;

        // Identify condo boxes (StackIndex >= CondoStackBase).
        double condoMin = double.MaxValue;
        double condoMax = double.MinValue;
        bool   hasCondo = false;

        foreach (var p in placements)
        {
            if (p.StackIndex < CondoStackBase) continue;
            hasCondo = true;
            if (p.Y           < condoMin) condoMin = p.Y;
            if (p.Y + p.BL    > condoMax) condoMax = p.Y + p.BL;
        }

        if (!hasCondo) return;

        double condoDepth  = condoMax - condoMin;
        double condoShift  = -condoMin;          // translate condo so its start = Y=0
        double primaryShift = condoDepth;        // shift primary/scatter up by condo depth

        for (int i = 0; i < placements.Count; i++)
        {
            var p = placements[i];
            placements[i] = p.StackIndex >= CondoStackBase
                ? p with { Y = p.Y + condoShift }
                : p with { Y = p.Y + primaryShift };
        }
    }

    private record struct ScatterStack(int SI, double StackY, int TopLayer, double TopZ);

    // A pinwheel pattern = exactly one pinwheel section (the 4-box windmill square unit).
    // Detected here so the two pattern funnels (LayerDepth + PlaceLayerAt) can branch; every other
    // consumer (PlaceProduct, balancing, scatter) routes through those and needs no change.
    private static bool IsPinwheel(LayerSection[] sections) =>
        sections.Length == 1 && sections[0].Pinwheel;

    private static double LayerDepth(LayerSection[] sections, double W, double L)
    {
        if (IsPinwheel(sections)) return W + L;   // one stack = one (W+L)-deep square slice

        double max = 0;
        foreach (var s in sections)
        {
            double depth = 0;
            foreach (var sub in s.GetSubRows())
                depth += sub.Rows * (sub.Rotated ? W : L);
            max = Math.Max(max, depth);
        }
        return max;
    }

    private static int CountLayerCapacity(
        LayerSection[] sections, ProductSpec spec, ContainerDims dims, double stackY)
    {
        var scratch = new List<BoxPlacement>();
        int n = PlaceLayerAt(sections, spec, dims, stackY, 0.0, int.MaxValue, -1, scratch, -1, -1);
        return n < 0 ? 0 : n;
    }

    // Exposed for InterlockDesigner + tests: how many boxes one layer of `sections` actually places
    // for a box of the given dimensions across a container of interior width `containerW` (depth and
    // height left effectively unbounded). Routes through the same PlaceLayerAt that real packing uses,
    // so a candidate pattern is scored/verified by the exact placement logic.
    internal static int LayerBoxCount(LayerSection[] sections, double w, double l, double h, double containerW)
    {
        if (sections is not { Length: > 0 } || w <= 0 || l <= 0 || containerW <= 0) return 0;
        var spec = new ProductSpec("", "", "", 0, w, l, h);
        var dims = new ContainerDims(containerW, 1e9, 1e9);
        return CountLayerCapacity(sections, spec, dims, 0.0);
    }

    // Exposed for the Interlock Designer preview: the actual box placements of ONE layer of `sections`
    // (at z=0, stackY=0) for a box of the given dimensions across `containerW`. Each placement carries
    // X (width), Y (depth), BW/BL and the Rotated flag — enough to draw an accurate top view.
    internal static List<BoxPlacement> LayerPlacements(LayerSection[] sections, double w, double l, double h, double containerW)
    {
        var list = new List<BoxPlacement>();
        if (sections is not { Length: > 0 } || w <= 0 || l <= 0 || containerW <= 0) return list;
        var spec = new ProductSpec("", "", "", 0, w, l, h);
        var dims = new ContainerDims(containerW, 1e9, 1e9);
        PlaceLayerAt(sections, spec, dims, 0.0, 0.0, int.MaxValue, 0, list, 0, 0);
        return list;
    }

    private static PlaceResult PlaceProduct(
        ContainerDims dims, ProductSpec spec, int requested,
        double startY, int productIndex, List<BoxPlacement> placements,
        int initialStackIndex = 0, int overrideMaxLayers = 0, bool fillCompletely = false)
    {
        if (spec.PatternA is not { Length: > 0 }) return new(0, startY, 0, 0);

        double stackDepth = LayerDepth(spec.PatternA, spec.W, spec.L);
        if (stackDepth <= 0) return new(0, startY, 0, 0);

        int maxLayers   = overrideMaxLayers > 0
            ? overrideMaxLayers
            : (spec.MaxLayers > 0 ? spec.MaxLayers : int.MaxValue);
        int maxHeight   = (int)Math.Floor(dims.H / spec.H);
        int layerLimit  = Math.Min(maxLayers, maxHeight);

        int packed       = 0;
        int stackIndex   = initialStackIndex;
        int fullStacks   = 0;
        int partialBoxes = 0;

        int fullBpl = CountLayerCapacity(spec.PatternA, spec, dims, startY);

        // Cap primary at floor(requested / fullStack) FULL stacks for every patterned product.
        // A partial last stack would consume a full stack-depth slot while only holding a
        // fraction of boxes — the remainder is better handled by condo (compact, at the innermost
        // wall) than by opening another primary slot. Without this cap the first-placed product
        // greedily occupies one extra stack-depth that the next product could have used, causing
        // it to run short. (Applies to all products now — no longer gated by CondoCount.)
        int maxStacks = int.MaxValue;
        if (fullBpl > 0 && layerLimit > 0)
        {
            int fullStackBoxes = fullBpl * layerLimit;
            // Normally floor → a partial last stack is left for condo. When fillCompletely (a "bulk"
            // SKU that must fill primary so the filler condo stays pure), use ceiling → build the
            // partial last wall here instead.
            maxStacks = fillCompletely
                ? Math.Max(1, (int)Math.Ceiling((double)requested / fullStackBoxes))
                : Math.Max(1, requested / fullStackBoxes);
        }

        while (packed < requested)
        {
            if (stackIndex - initialStackIndex >= maxStacks) break;
            double stackY = startY + (stackIndex - initialStackIndex) * stackDepth;
            if (stackY >= dims.L) break;
            if (CountLayerCapacity(spec.PatternA, spec, dims, stackY) < fullBpl) break;

            bool flipStart    = (stackIndex % 2 == 1) && spec.PatternB is { Length: > 0 };
            int  beforeStack  = packed;
            int  layersPlaced = 0;

            for (int layer = 0; layer < layerLimit && packed < requested; layer++)
            {
                double z     = layer * spec.H;
                bool   useA  = flipStart ? (layer % 2 == 1) : (layer % 2 == 0);
                var sections = useA ? spec.PatternA : (spec.PatternB ?? spec.PatternA);

                int capacity = CountLayerCapacity(sections!, spec, dims, stackY);
                if (capacity <= 0) break;
                if (requested - packed < capacity)
                {
                    if (!fillCompletely) break;       // normal: don't start a partial layer
                    capacity = requested - packed;    // fillCompletely: place a partial top layer
                    if (capacity <= 0) break;
                }

                int n = PlaceLayerAt(sections, spec, dims, stackY, z, capacity, productIndex, placements, stackIndex, layer);
                if (n < 0) break;
                packed += n;
                layersPlaced++;
            }

            if (layersPlaced == 0) break; // can't start any stack from here

            if (layerLimit > 0 && layersPlaced == layerLimit)
                fullStacks++;
            else if (layersPlaced > 0)
                partialBoxes = packed - beforeStack;

            stackIndex++;
        }

        return new(packed, startY + (stackIndex - initialStackIndex) * stackDepth, fullStacks, partialBoxes);
    }

    private static int PlaceLayerAt(
        LayerSection[] sections, ProductSpec spec, ContainerDims dims,
        double stackY, double z, int limit, int productIndex,
        List<BoxPlacement> placements, int stackIndex, int layerIndex)
    {
        if (z + spec.H > dims.H + 0.01) return -1;

        if (IsPinwheel(sections))
            return PlacePinwheelLayer(spec, dims, stackY, z, limit, productIndex, placements, stackIndex, layerIndex);

        static double SectionWidth(LayerSection s, double w, double l) =>
            s.GetSubRows().Max(sub => sub.Cols * (sub.Rotated ? l : w));

        double tierW = 0;
        foreach (var s in sections)
            tierW += SectionWidth(s, spec.W, spec.L);
        if (tierW <= 0) return -1;

        int numTiers = Math.Max(1, (int)Math.Floor(dims.W / tierW));
        int packed   = 0;

        for (int tier = 0; tier < numTiers && packed < limit; tier++)
        {
            double sectionX = tier * tierW;
            foreach (var section in sections)
            {
                double subY = stackY;
                foreach (var sub in section.GetSubRows())
                {
                    double bw = sub.Rotated ? spec.L : spec.W;
                    double bl = sub.Rotated ? spec.W : spec.L;

                    for (int c = 0; c < sub.Cols && packed < limit; c++)
                    {
                        for (int r = 0; r < sub.Rows && packed < limit; r++)
                        {
                            double px = sectionX + c * bw;
                            double py = subY + r * bl;
                            if (px + bw > dims.W + 0.01 || py + bl > dims.L + 0.01) continue;
                            placements.Add(new BoxPlacement(px, py, z, bw, bl, spec.H, productIndex, sub.Rotated, stackIndex, layerIndex));
                            packed++;
                        }
                    }
                    subY += sub.Rows * bl;
                }
                sectionX += SectionWidth(section, spec.W, spec.L);
            }
        }

        return packed > 0 ? packed : -1;
    }

    // Places one layer of pinwheel "windmill" units across the container width. Four boxes per
    // unit — each rotated 90° from the next — form a (W+L)×(W+L) square with a small |L−W| centre
    // gap. Units tile along X; one full layer is floor(W_container / (W+L)) units. `limit` is
    // honoured so condo/scatter callers can place a partial count. Returns boxes placed, or -1.
    private static int PlacePinwheelLayer(
        ProductSpec spec, ContainerDims dims, double stackY, double z, int limit,
        int productIndex, List<BoxPlacement> placements, int stackIndex, int layerIndex)
    {
        double w = spec.W, l = spec.L, side = w + l;
        int unitsAcross = (int)((dims.W + 0.01) / side);
        if (unitsAcross < 1) return -1;

        // (dx, dy, rotated) of each box relative to the unit's near-left corner. Rotated boxes lie
        // with their long side (L) along X; non-rotated keep W along X. The four offsets interlock
        // into the square (centre gap = |L−W|, here unfilled).
        var motif = new (double dx, double dy, bool rot)[]
        {
            (0, 0, true),   // bottom: L wide × W deep
            (l, 0, false),  // right : W wide × L deep
            (w, l, true),   // top   : L wide × W deep
            (0, w, false),  // left  : W wide × L deep
        };

        int placed = 0;
        for (int u = 0; u < unitsAcross && placed < limit; u++)
        {
            double ox = u * side;
            foreach (var (dx, dy, rot) in motif)
            {
                if (placed >= limit) break;
                double bw = rot ? l : w;
                double bl = rot ? w : l;
                double px = ox + dx, py = stackY + dy;
                if (px + bw > dims.W + 0.01 || py + bl > dims.L + 0.01) continue;
                placements.Add(new BoxPlacement(px, py, z, bw, bl, spec.H, productIndex, rot, stackIndex, layerIndex));
                placed++;
            }
        }
        return placed > 0 ? placed : -1;
    }

    // Picks the filler SKU to reserve for a pure condo wall (see Calculate). A SKU qualifies when its
    // ENTIRE quantity fits as a thin condo wall — perRow (= boxes across the width) × ceilingRows
    // (= layers that fit under the container height). Among qualifiers, the lightest (min total CBM)
    // wins. Returns -1 when there are fewer than 2 patterned SKUs or none qualifies.
    private static int SelectFillerIndex(
        ContainerDims dims, IReadOnlyList<(ProductSpec Spec, int Qty)> requests)
    {
        int patterned = requests.Count(r => r.Spec.PatternA is { Length: > 0 });
        if (patterned < 2) return -1;

        int    best     = -1;
        double bestCbm  = double.MaxValue;
        for (int i = 0; i < requests.Count; i++)
        {
            var (spec, qty) = requests[i];
            if (spec.PatternA is not { Length: > 0 } || spec.W <= 0 || spec.H <= 0) continue;

            int perRow   = (int)((dims.W + 0.01) / spec.W);
            int ceilRows = (int)Math.Floor(dims.H / spec.H);
            if (perRow < 1 || ceilRows < 1) continue;
            if (qty > perRow * ceilRows) continue;            // too big to be a thin wall → not a filler

            double totalCbm = qty * spec.Cbm;
            if (totalCbm < bestCbm) { bestCbm = totalCbm; best = i; }
        }
        return best;
    }

    private static int[] ComputeEffectiveLayers(
        ContainerDims dims, IReadOnlyList<(ProductSpec Spec, int Qty)> requests)
    {
        int n   = requests.Count;
        var eff  = new int[n];
        var data = new (int bpl, double sd, int stacks)[n];

        for (int i = 0; i < n; i++)
        {
            var (spec, qty) = requests[i];
            int maxL = spec.MaxLayers > 0 ? spec.MaxLayers : (int)Math.Floor(dims.H / spec.H);
            eff[i] = maxL;
            if (spec.PatternA is not { Length: > 0 }) continue;

            double sd  = LayerDepth(spec.PatternA, spec.W, spec.L);
            int    bpl = CountLayerCapacity(spec.PatternA, spec, dims, 0);
            if (sd <= 0 || bpl <= 0) continue;

            int stacks = (int)Math.Ceiling((double)qty / (bpl * maxL));
            data[i] = (bpl, sd, stacks);
        }

        double totalY  = data.Sum(d => d.stacks * d.sd);
        double targetY = dims.L - 50.0;
        if (totalY <= 0 || totalY >= targetY) return eff;
        int activeCount = data.Count(d => d.bpl > 0 && d.sd > 0 && d.stacks > 0);
        if (activeCount >= 1)
        {
            // Full layers placeable per product (floor: only complete layers count).
            var totalLayers = new int[n];
            for (int i = 0; i < n; i++)
                if (data[i].bpl > 0)
                    totalLayers[i] = requests[i].Qty / data[i].bpl;

            // Snap points = integer multiples of each product's physical height.
            // Iterating ascending finds the smallest T_h where requiredY ≤ targetY,
            // maximising Y fill while keeping all stack heights near the same physical target.
            var snapSet = new SortedSet<double>();
            for (int i = 0; i < n; i++)
            {
                if (data[i].bpl <= 0 || data[i].sd <= 0 || totalLayers[i] <= 0) continue;
                double h = requests[i].Spec.H;
                for (int k = 1; k <= eff[i]; k++)
                    snapSet.Add(Math.Round(k * h, 4));
            }

            foreach (double Th in snapSet)
            {
                double requiredY = 0;
                var trialEff = new int[n];
                for (int i = 0; i < n; i++)
                {
                    trialEff[i] = eff[i];
                    if (data[i].bpl <= 0 || data[i].sd <= 0 || totalLayers[i] <= 0) continue;
                    int ei = Math.Max(1, Math.Min(eff[i],
                        (int)Math.Floor(Th / requests[i].Spec.H)));
                    trialEff[i] = ei;
                    requiredY  += (int)Math.Ceiling((double)totalLayers[i] / ei) * data[i].sd;
                }
                if (requiredY <= targetY)
                {
                    Array.Copy(trialEff, eff, n);
                    return eff;
                }
            }

            return eff;
        }

        // No active pattern products — nothing to scale.
        return eff;
    }

    private static void RunLayerBalancing(
        List<PackInfo> packInfos, ContainerDims dims,
        List<BoxPlacement> placements)
    {
        foreach (var info in packInfos)
        {
            if (!info.HasPattern) continue;

            bool changed = true;
            while (changed)
            {
                changed = false;

                var stacks = placements
                    .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase)
                    .GroupBy(p => p.StackIndex)
                    .Select(g => (SI: g.Key,
                                  Height: g.Max(p => p.LayerIndex) + 1,
                                  StackY: g.Min(p => p.Y)))
                    .OrderBy(x => x.SI)
                    .ToList();

                if (stacks.Count < 2) break;

                int maxH = stacks.Max(x => x.Height);
                int minH = stacks.Min(x => x.Height);
                if (maxH - minH <= 1) break;

                var tall = stacks.Last(x => x.Height == maxH);
                var low  = stacks.First(x => x.Height == minH);

                int removeLayer = tall.Height - 1;
                int removedCount = placements.Count(p =>
                    p.ProductIndex == info.ProductIndex &&
                    p.StackIndex   == tall.SI &&
                    p.LayerIndex   == removeLayer);
                if (removedCount == 0) break;

                int nextLayer = low.Height;
                double z = nextLayer * info.Spec.H;
                if (z + info.Spec.H > dims.H + 0.01) break;

                bool flipIt  = (low.SI % 2 == 1) && info.Spec.PatternB is { Length: > 0 };
                bool useA    = flipIt ? (nextLayer % 2 == 1) : (nextLayer % 2 == 0);
                var sections = useA ? info.Spec.PatternA! : (info.Spec.PatternB ?? info.Spec.PatternA)!;

                int capacity = CountLayerCapacity(sections, info.Spec, dims, low.StackY);
                if (capacity != removedCount) break;

                if (PackingLog.IsEnabled)
                    PackingLog.Info($"  [{info.ProductIndex}] {info.Spec.Description}: stack{tall.SI}(h={tall.Height}) layer{removeLayer}→stack{low.SI}(h={low.Height}) layer{nextLayer}  moved={capacity}");

                placements.RemoveAll(p =>
                    p.ProductIndex == info.ProductIndex &&
                    p.StackIndex   == tall.SI &&
                    p.LayerIndex   == removeLayer);

                PlaceLayerAt(sections, info.Spec, dims, low.StackY, z, capacity,
                             info.ProductIndex, placements, low.SI, nextLayer);
                changed = true;
            }
        }
    }


    private static void SortStacksByHeight(
        List<PackInfo> packInfos, List<BoxPlacement> placements, ContainerDims dims)
    {
        // Zone ordering: product with highest total CBM (qty × box CBM) goes innermost (back wall).
        // This matches real loading practice — heaviest/most-volume product loaded first.
        // Within each zone: stacks sorted by physical height DESC so the global profile
        // forms a staircase with each adjacent pair differing by ≈ 1 box layer.
        var zones = packInfos
            .Where(info => info.HasPattern)
            .Select(info =>
            {
                var boxes = placements
                    .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase)
                    .ToList();
                return (ProductIndex: info.ProductIndex, Boxes: boxes, TotalCbm: info.Spec.Cbm * info.Requested);
            })
            .Where(x => x.Boxes.Count > 0)
            .Select(x =>
            {
                var stacks = x.Boxes
                    .GroupBy(p => p.StackIndex)
                    .Select(g => (
                        SI:    g.Key,
                        PhysH: g.Max(p => p.Z + p.BH),
                        MinY:  g.Min(p => p.Y),
                        Depth: g.Max(p => p.Y + p.BL) - g.Min(p => p.Y)
                    ))
                    .OrderByDescending(s => s.PhysH)
                    .ToList();
                return (
                    ProductIndex: x.ProductIndex,
                    ZoneMinY:     x.Boxes.Min(p => p.Y),
                    ZoneDepth:    x.Boxes.Max(p => p.Y + p.BL) - x.Boxes.Min(p => p.Y),
                    TotalCbm:     x.TotalCbm,
                    Stacks:       stacks
                );
            })
            .OrderByDescending(z => z.TotalCbm)
            .ToList();

        if (zones.Count == 0) return;

        // Start from the earliest Y of all patterned zones, pack zones contiguously in CBM order.
        double curY = zones.Min(z => z.ZoneMinY);
        var stackYMap = new Dictionary<(int ProductIndex, int StackIndex), (double OrigMinY, double NewMinY)>();
        foreach (var zone in zones)
        {
            foreach (var stack in zone.Stacks)
            {
                stackYMap[(zone.ProductIndex, stack.SI)] = (stack.MinY, curY);
                curY += stack.Depth;
            }
        }

        for (int j = 0; j < placements.Count; j++)
        {
            var p = placements[j];
            if (p.StackIndex >= CondoStackBase) continue;
            if (!stackYMap.TryGetValue((p.ProductIndex, p.StackIndex), out var yInfo)) continue;
            if (Math.Abs(yInfo.NewMinY - yInfo.OrigMinY) < 0.001) continue;
            placements[j] = p with { Y = yInfo.NewMinY + (p.Y - yInfo.OrigMinY) };
        }
    }
}
