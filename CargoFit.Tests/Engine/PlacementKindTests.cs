using System.Collections.Generic;
using System.Linq;
using CargoFit;
using Xunit;
using Xunit.Abstractions;

namespace CargoFit.Tests.Engine;

/// <summary>
/// Verifies <see cref="PlacementKind"/> tagging (added for the manual leftover editor, Feature 2).
/// Kind is the ONLY reliable way to tell scatter boxes apart from primary ones, because scatter
/// boxes reuse the primary stack's StackIndex (they only sit at a higher layer). These tests pin:
///   • per-product Scatter/Condo counts == the engine's ScatterMap/CondoMap, and
///   • the StackIndex relationships the editor's lock logic relies on.
/// </summary>
public class PlacementKindTests(ITestOutputHelper log)
{
    public static IEnumerable<object[]> Cases() =>
        TestHelpers.Cases().Select(c => new object[] { c.Label });

    [Theory]
    [MemberData(nameof(Cases))]
    public void Kind_matches_engine_maps_and_stackindex_invariants(string label)
    {
        var c = TestHelpers.Cases().First(x => x.Label == label);
        var output = PackingEngine.Calculate(c.Container, c.Requests);

        foreach (var info in output.PackInfos)
        {
            int p = info.ProductIndex;
            int scatter = output.Placements.Count(b => b.ProductIndex == p && b.Kind == PlacementKind.Scatter);
            int condo   = output.Placements.Count(b => b.ProductIndex == p && b.Kind == PlacementKind.Condo);

            Assert.Equal(output.ScatterMap.GetValueOrDefault(p, 0), scatter);
            Assert.Equal(output.CondoMap.GetValueOrDefault(p, 0),   condo);
        }

        // Scatter reuses the primary stack index (sits on top of its own stack); condo lives in the 1000+ range.
        Assert.All(output.Placements.Where(b => b.Kind == PlacementKind.Scatter),
            b => Assert.True(b.StackIndex < PackingEngine.CondoStackBase));
        Assert.All(output.Placements.Where(b => b.Kind == PlacementKind.Condo),
            b => Assert.True(b.StackIndex >= PackingEngine.CondoStackBase));

        // Every box is classified; total maps add up to the placement count.
        int byKind = output.Placements.Count(b => b.Kind == PlacementKind.Primary)
                   + output.Placements.Count(b => b.Kind == PlacementKind.Condo)
                   + output.Placements.Count(b => b.Kind == PlacementKind.Mixed)
                   + output.Placements.Count(b => b.Kind == PlacementKind.Scatter);
        Assert.Equal(output.Placements.Count, byKind);
    }

    /// <summary>The shared cases must actually exercise both Scatter and Condo, or the tagging is untested.</summary>
    [Fact]
    public void Shared_cases_exercise_both_scatter_and_condo()
    {
        int totalScatter = 0, totalCondo = 0;
        foreach (var c in TestHelpers.Cases())
        {
            var output = PackingEngine.Calculate(c.Container, c.Requests);
            totalScatter += output.Placements.Count(b => b.Kind == PlacementKind.Scatter);
            totalCondo   += output.Placements.Count(b => b.Kind == PlacementKind.Condo);
        }
        log.WriteLine($"across all cases: scatter={totalScatter}  condo={totalCondo}");
        Assert.True(totalScatter > 0, "no Scatter boxes in any shared case — scatter tagging is untested");
        Assert.True(totalCondo   > 0, "no Condo boxes in any shared case — condo tagging is untested");
    }

    /// <summary>
    /// Scatter boxes sit ON TOP of the stack they share (ProductIndex, StackIndex) with, at that stack's
    /// Y — so the PDF can fold them into that stack's card without inflating its Y span. (Cross-product
    /// filler leftovers have no same-product primary at their StackIndex and are skipped.)
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Scatter_sits_within_its_stacks_Y_band(string label)
    {
        var c = TestHelpers.Cases().First(x => x.Label == label);
        var output = PackingEngine.Calculate(c.Container, c.Requests);

        foreach (var b in output.Placements.Where(p => p.Kind == PlacementKind.Scatter))
        {
            var stackPrimary = output.Placements
                .Where(p => p.ProductIndex == b.ProductIndex
                         && p.StackIndex == b.StackIndex
                         && p.Kind != PlacementKind.Scatter)
                .ToList();
            if (stackPrimary.Count == 0) continue;   // filler on another product's stack → its own unit

            double yMin = stackPrimary.Min(p => p.Y);
            double yMax = stackPrimary.Max(p => p.Y + p.BL);
            Assert.True(b.Y >= yMin - 1 && b.Y + b.BL <= yMax + 1,
                $"[{label}] scatter box Y[{b.Y:F0},{b.Y + b.BL:F0}] escapes its stack band Y[{yMin:F0},{yMax:F0}]");
        }
    }
}
