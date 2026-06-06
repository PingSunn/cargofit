using System.Collections.Generic;
using System.Linq;
using CargoFit;
using Xunit.Abstractions;

namespace CargoFit.Tests;

internal static class TestHelpers
{
    // ── Containers ───────────────────────────────────────────────────────────

    internal static ContainerSpec Container20ft()  => new("ตู้สั้น",     "20 ft",    244, 600,  259, Gap: 10);
    internal static ContainerSpec Container40ft()  => new("ตู้ยาว",     "40 ft",    244, 1209, 260, Gap: 10);
    internal static ContainerSpec Container40HC()  => new("ตู้ไฮคิวบ์", "40 ft HC", 244, 1203, 290, Gap: 10);

    // ── Shared product specs (used across multiple test files) ───────────────

    internal static ProductSpec Aloe365ML() => new("Aloe", "365 ML", "Pack 24", 9.79,
        21.9, 33.4, 20.5,
        PatternA: [new LayerSection(2, 6, false), new LayerSection(3, 3, true)],
        PatternB: [new LayerSection(3, 3, true),  new LayerSection(2, 6, false)],
        MaxLayers: 10, CondoCount: 10);

    internal static ProductSpec Mogu1000ML() => new("Mogu", "1000 ML", "Pack 12", 13.47,
        26.2, 34.8, 26.7,
        PatternA: [new LayerSection(4, 2, true), new LayerSection(3, 6, false)],
        PatternB: [new LayerSection(3, 6, false), new LayerSection(4, 2, true)],
        MaxLayers: 8, CondoCount: 9);

    internal static ProductSpec Mogu320ML() => new("Mogu", "320 ML", "Pack 24", 8.7,
        25.8, 38.5, 15.7,
        PatternA:
        [
            new LayerSection(4, 1, true),
            new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
            new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
            new LayerSection(4, 1, true),
        ],
        PatternB:
        [
            new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
            new LayerSection(4, 1, true),
            new LayerSection(4, 1, true),
            new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
        ],
        MaxLayers: 13, CondoCount: 9);

    internal static ProductSpec Mogu220ML() => new("Mogu", "220 ML", "Pack 24", 6.2,
        23.1, 35, 13.8,
        PatternA: [new LayerSection(3, 2, true),  new LayerSection(2, 7, false)],
        PatternB: [new LayerSection(2, 7, false), new LayerSection(3, 2, true)],
        MaxLayers: 14, CondoCount: 10);

    internal static ProductSpec CoolerBag320ML() => new("Cooler Bag", "320 ML", "Pack 24", 9.5,
        27.4, 40.9, 17.1,
        PatternA: [new LayerSection(3, 3, true),  new LayerSection(2, 4, false)],
        PatternB: [new LayerSection(2, 4, false), new LayerSection(3, 3, true)],
        MaxLayers: 10, CondoCount: 8);

    internal static ProductSpec Gumi320ML() => new("Gumi", "320 ML", "Pack 24", 8.7,
        25.8, 38.5, 16.3,
        PatternA:
        [
            new LayerSection(4, 1, true),
            new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
            new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
            new LayerSection(4, 1, true),
        ],
        PatternB:
        [
            new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
            new LayerSection(4, 1, true),
            new LayerSection(4, 1, true),
            new LayerSection(0, 0, false, [new SectionSubRow(1, 3, false), new SectionSubRow(1, 2, true), new SectionSubRow(1, 3, false)]),
        ],
        MaxLayers: 13, CondoCount: 9);

    // Pinwheel pack: 4 boxes (27.3×31.1) interlock into a 58.4×58.4 square (3.8 cm centre gap);
    // 4 squares fill the 234 cm width almost exactly (233.6, 0.4 waste) vs a plain grid (~16 waste).
    internal static ProductSpec GumiJelly150G() => new("Gumi Jelly", "150 G", "Pack 36", 5.58,
        27.3, 31.1, 18.2,
        PatternA: [new LayerSection(0, 0, false, Pinwheel: true)],
        MaxLayers: 12, CondoCount: 0);

    // ── Shared test cases (SINGLE source of truth) ───────────────────────────
    // Every case here is BOTH (1) rendered in the ProofGen HTML preview and (2) asserted by
    // GlobalConditionTests (free door space ≤ 50 cm). Add a case ONCE → it shows up in both.

    internal sealed record LoadCase(string Label, ContainerSpec Container, (ProductSpec Spec, int Qty)[] Requests);

    /// <summary>
    /// The one list of loads to preview + door-check. Each case must be a full-ish load (door ≤ 50 cm)
    /// or GlobalConditionTests fails. Add a case once → it shows in ProofGen's HTML AND gets door-checked.
    /// </summary>
    internal static IReadOnlyList<LoadCase> Cases() => new[]
    {
        // NOTE: user said "Mogu 329ML" but no 329 exists — treated as 320 ML Pack 24 (the only match).
        new LoadCase("20ft · Mogu220×1076 · Mogu320×940",
            Container20ft(), [(Mogu220ML(), 1076), (Mogu320ML(), 940)]),

        new LoadCase("40ft · Gumi320×936 · CoolerBag320×2000",
            Container40ft(), [(Gumi320ML(), 936), (CoolerBag320ML(), 2000)]),

        // "1000ML ฐาน 26" = Mogu 1000 Pack 12 (4×2+3×6 = 26/layer). 320ML assumed Mogu 320 Pack 24.
        new LoadCase("20ft · Mogu1000×1000 · Mogu320×120",
            Container20ft(), [(Mogu1000ML(), 1000), (Mogu320ML(), 120)]),

        // Pinwheel demo — Gumi Jelly fills the 20ft container (16/layer × 12 × 10 slices = 1920).
        new LoadCase("20ft · Gumi Jelly 150G ×1920 (pinwheel)",
            Container20ft(), [(GumiJelly150G(), 1920)]),
    };

    // ── Dump helper ──────────────────────────────────────────────────────────

    internal static void DumpOutput(
        ContainerSpec container,
        IReadOnlyList<(ProductSpec Spec, int Qty)> requests,
        PackingOutput output,
        ITestOutputHelper log)
    {
        double iw = container.InteriorW;
        double il = container.InteriorL;
        double ih = container.InteriorH;

        double containerCbm = iw * il * ih / 1_000_000.0;
        double usedCbm      = output.Placements.Sum(p => p.BW * p.BL * p.BH / 1_000_000.0);
        double pct          = containerCbm > 0 ? usedCbm / containerCbm * 100.0 : 0;

        log.WriteLine($"=== {container.Name} {container.SizeLabel}  ({iw}×{il}×{ih} cm interior) ===");
        log.WriteLine($"    placements: {output.Placements.Count}  CBM: {usedCbm:F3}/{containerCbm:F3} ({pct:F1}%)");
        log.WriteLine("");

        log.WriteLine("  #  Product                            Req  Packed  Primary  Condo  Scatter  Pattern");
        log.WriteLine("  " + new string('-', 81));

        foreach (var info in output.PackInfos)
        {
            var s       = info.Spec;
            int pri     = output.Placements.Count(p =>
                              p.ProductIndex == info.ProductIndex &&
                              p.StackIndex   <  PackingEngine.CondoStackBase);
            int condo   = output.CondoMap.GetValueOrDefault(info.ProductIndex, 0);
            int scatter = output.ScatterMap.GetValueOrDefault(info.ProductIndex, 0);
            string name = $"{s.Description} {s.Content} {s.PackSize}";

            log.WriteLine($"  {info.ProductIndex,2}  {name,-34} {info.Requested,4}" +
                          $"  {info.Result.Packed,6}  {pri,7}  {condo,5}  {scatter,7}  {(info.HasPattern ? "yes" : "no")}");
        }

        log.WriteLine("");

        if (output.Placements.Count > 0)
        {
            double maxX = output.Placements.Max(p => p.X + p.BW);
            double maxY = output.Placements.Max(p => p.Y + p.BL);
            double maxZ = output.Placements.Max(p => p.Z + p.BH);
            log.WriteLine($"  Bounding box: X=[0,{maxX:F1}]  Y=[0,{maxY:F1}]  Z=[0,{maxZ:F1}]");
            log.WriteLine($"  Container   : X=[0,{iw}]  Y=[0,{il}]  Z=[0,{ih}]");
        }
    }
}
