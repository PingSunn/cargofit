using CargoFit;
using Xunit;

namespace CargoFit.Tests.Engine;

/// <summary>Pure-geometry tests for the manual leftover editor (gravity / support / drop / pick).</summary>
public class LeftoverEditGeometryTests
{
    private static BoxPlacement B(double x, double y, double z, double w, double l, double h,
                                  PlacementKind kind = PlacementKind.Primary)
        => new(x, y, z, w, l, h, 0, false, 0, 0, kind);

    // ── GravityZ ─────────────────────────────────────────────────────────────--
    [Fact]
    public void GravityZ_floor_when_nothing_below()
        => Assert.Equal(0, LeftoverEditGeometry.GravityZ(new BoxPlacement[0], 0, 0, 30, 30));

    [Fact]
    public void GravityZ_rests_on_overlapping_box_top()
    {
        var others = new[] { B(0, 0, 0, 100, 100, 10) };
        Assert.Equal(10, LeftoverEditGeometry.GravityZ(others, 0, 0, 30, 30));
    }

    [Fact]
    public void GravityZ_takes_max_of_overlapping_tops()
    {
        var others = new[] { B(0, 0, 0, 50, 50, 10), B(20, 20, 0, 50, 50, 20) };
        Assert.Equal(20, LeftoverEditGeometry.GravityZ(others, 10, 10, 30, 30));
    }

    [Fact]
    public void GravityZ_ignores_non_overlapping()
    {
        var others = new[] { B(500, 500, 0, 50, 50, 30) };
        Assert.Equal(0, LeftoverEditGeometry.GravityZ(others, 0, 0, 30, 30));
    }

    // ── support (no float) ───────────────────────────────────────────────────--
    [Fact]
    public void Support_floor_is_always_supported()
        => Assert.True(LeftoverEditGeometry.IsFullySupported(new BoxPlacement[0], 0, 0, 30, 30, 0));

    [Fact]
    public void Support_full_cover_true()
    {
        var others = new[] { B(0, 0, 0, 100, 100, 10) };
        Assert.True(LeftoverEditGeometry.IsFullySupported(others, 10, 10, 30, 30, 10));
    }

    [Fact]
    public void Support_partial_overhang_false()
    {
        var others = new[] { B(0, 0, 0, 20, 100, 10) };          // covers only x[0,20]
        Assert.False(LeftoverEditGeometry.IsFullySupported(others, 0, 0, 40, 30, 10));
    }

    [Fact]
    public void Support_gap_between_two_false()
    {
        var others = new[] { B(0, 0, 0, 10, 30, 10), B(30, 0, 0, 10, 30, 10) }; // gap x[10,30]
        Assert.False(LeftoverEditGeometry.IsFullySupported(others, 0, 0, 40, 30, 10));
    }

    [Fact]
    public void Support_two_abutting_cover_full_true()
    {
        var others = new[] { B(0, 0, 0, 15, 30, 10), B(15, 0, 0, 15, 30, 10) };
        Assert.True(LeftoverEditGeometry.IsFullySupported(others, 0, 0, 30, 30, 10));
    }

    // ── TryDrop ──────────────────────────────────────────────────────────────--
    [Fact]
    public void TryDrop_valid_on_floor()
    {
        var r = LeftoverEditGeometry.TryDrop(new BoxPlacement[0], 50, 50, 30, 30, 20, 235, 586, 238);
        Assert.True(r.Ok);
        Assert.Equal(0, r.Z);
    }

    [Fact]
    public void TryDrop_clamps_into_container_so_edge_drop_is_valid()
    {
        // Snap pins an off-edge drop back inside, so a normal box dropped past the wall stays valid.
        var r = LeftoverEditGeometry.TryDrop(new BoxPlacement[0], 230, 50, 30, 30, 20, 235, 586, 238);
        Assert.True(r.Ok);
        Assert.True(r.X + 30 <= 235 + 0.01);
    }

    [Fact]
    public void TryDrop_too_wide_for_container_false()
    {
        // 300 cm can't fit a 235 cm container — the bounds guard rejects it.
        var r = LeftoverEditGeometry.TryDrop(new BoxPlacement[0], 0, 50, 300, 30, 20, 235, 586, 238);
        Assert.False(r.Ok);
    }

    [Fact]
    public void TryDrop_ceiling_exceeded_false()
    {
        var others = new[] { B(0, 0, 0, 100, 100, 230) };       // tall stack, top at 230
        var r = LeftoverEditGeometry.TryDrop(others, 10, 10, 30, 30, 20, 235, 586, 238); // 230+20 > 238
        Assert.False(r.Ok);
    }

    [Fact]
    public void TryDrop_unsupported_false()
    {
        var others = new[] { B(0, 0, 0, 20, 30, 10) };           // only half under the footprint
        var r = LeftoverEditGeometry.TryDrop(others, 0, 0, 40, 30, 10, 235, 586, 238);
        Assert.False(r.Ok);
    }

    // ── PickTopScatter ───────────────────────────────────────────────────────--
    [Fact]
    public void Pick_topmost_scatter()
    {
        var boxes = new[]
        {
            B(0, 0, 0,  100, 100, 10, PlacementKind.Primary),
            B(0, 0, 10, 50,  50,  10, PlacementKind.Scatter),
            B(0, 0, 20, 50,  50,  10, PlacementKind.Scatter),
        };
        Assert.Equal(2, LeftoverEditGeometry.PickTopScatter(boxes, 10, 10, 999));
    }

    [Fact]
    public void Pick_ignores_locked()
    {
        var boxes = new[] { B(0, 0, 0, 100, 100, 10, PlacementKind.Primary) };
        Assert.Equal(-1, LeftoverEditGeometry.PickTopScatter(boxes, 10, 10, 999));
    }

    [Fact]
    public void Pick_respects_layer_cut()
    {
        var boxes = new[] { B(0, 0, 10, 50, 50, 10, PlacementKind.Scatter) };
        Assert.Equal(-1, LeftoverEditGeometry.PickTopScatter(boxes, 10, 10, 5)); // cut below the box
    }
}
