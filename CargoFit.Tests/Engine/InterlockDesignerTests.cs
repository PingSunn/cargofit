using CargoFit;
using Xunit;
using Xunit.Abstractions;

namespace CargoFit.Tests.Engine;

// Validates the auto interlock pattern generator (Feature 1). Boxes are real SKU dimensions from
// products.json. All assert against the SAME placement logic the packing engine uses
// (PackingEngine.LayerBoxCount), so a generated pattern that passes here also packs in the engine.
public class InterlockDesignerTests(ITestOutputHelper log)
{
    const double Wc = 235.0;

    public static TheoryData<string, double, double, double> Boxes() => new()
    {
        { "Aloe 365",      21.9, 33.4, 20.5 },
        { "Mogu 320",      25.8, 38.5, 15.7 },
        { "Mogu Ice 150",  14.1, 23.5, 18.4 },
        { "Mogu 500",      28.6, 43.0, 19.6 },
        { "Gumi Jelly",    27.3, 31.1, 18.2 },
    };

    [Theory]
    [MemberData(nameof(Boxes))]
    public void Generated_FitsWidth_Interlocks_AndEngineAgrees(string name, double w, double l, double h)
    {
        var r = InterlockDesigner.Generate(w, l, h, Wc);
        log.WriteLine($"{name} {w}×{l} → [{r.Template}] {r.BoxesPerLayer}/layer  " +
                      $"width {r.UsedWidth:F1}/{Wc} ({r.WidthUtil * 100:F1}%)  depth {r.Depth:F1}  " +
                      $"areaUtil {r.AreaUtil * 100:F1}%");

        Assert.NotEmpty(r.PatternA);
        Assert.True(r.Interlocks, $"{name}: expected an interlocking pattern, got {r.Template}");

        // Never overflow the container width.
        Assert.True(r.UsedWidth <= Wc + 0.01, $"{name}: used width {r.UsedWidth:F1} > {Wc}");

        // The engine's own placement must produce exactly the reported count → the pattern is real.
        int engineCount = PackingEngine.LayerBoxCount(r.PatternA, w, l, h, Wc);
        Assert.Equal(r.BoxesPerLayer, engineCount);

        // High area utilisation of the squared-off footprint (the "ใช้พื้นที่มากสุด" goal).
        Assert.True(r.AreaUtil > 0.88, $"{name}: areaUtil {r.AreaUtil:P0} too low");
    }

    [Fact]
    public void NearSquareBox_PicksPinwheel_WithSameLayerEachLevel()
    {
        var r = InterlockDesigner.Generate(27.3, 31.1, 18.2, Wc);  // Gumi Jelly — near square
        Assert.Equal("pinwheel", r.Template);
        Assert.Single(r.PatternA);
        Assert.True(r.PatternA[0].Pinwheel);
        Assert.Empty(r.PatternB);   // windmill locks in-plane → same layer every level
    }

    [Fact]
    public void TwoBand_PatternB_IsTheCrossOfPatternA()
    {
        var r = InterlockDesigner.Generate(21.9, 33.4, 20.5, Wc);  // Aloe 365 — two-band
        Assert.Equal("two-band", r.Template);
        Assert.Equal(2, r.PatternA.Length);
        Assert.Equal(2, r.PatternB.Length);
        // PatternB swaps the band order → vertical seams cross between layers.
        Assert.Equal(r.PatternA[0], r.PatternB[1]);
        Assert.Equal(r.PatternA[1], r.PatternB[0]);
        // The two bands have different orientation (that is what makes it interlock in-plane).
        Assert.NotEqual(r.PatternA[0].Rotated, r.PatternA[1].Rotated);
    }

    [Fact]
    public void OversizedBox_ReturnsEmptyPattern()
    {
        var r = InterlockDesigner.Generate(300, 400, 100, Wc);
        Assert.Empty(r.PatternA);
        Assert.False(r.Interlocks);
        Assert.Equal("none", r.Template);
    }

    [Theory]
    [MemberData(nameof(Boxes))]
    public void OnePatternForEveryContainer_OnlyTheReportedTotalDiffers(string name, double w, double l, double h)
    {
        var bare = InterlockDesigner.Generate(w, l, h, Wc);                  // no container
        var c20  = InterlockDesigner.Generate(w, l, h, Wc, 586, 238);       // 20ft
        var c40  = InterlockDesigner.Generate(w, l, h, Wc, 1203, 238);      // 40ft

        // SAME pattern regardless of container — one product, one pattern for all containers.
        Assert.Equal(bare.BoxesPerLayer, c20.BoxesPerLayer);
        Assert.Equal(bare.Depth,         c20.Depth);
        Assert.Equal(c20.BoxesPerLayer,  c40.BoxesPerLayer);
        Assert.Equal(c20.Depth,          c40.Depth);
        Assert.Equal(c20.Template,       c40.Template);

        // only the reported capacity differs; totals are self-consistent and the longer box holds more
        Assert.True(c20.StacksDeep > 0 && c20.LayersUp > 0);
        Assert.Equal(c20.BoxesPerLayer * c20.LayersUp * c20.StacksDeep, c20.TotalInContainer);
        Assert.True(c40.TotalInContainer >= c20.TotalInContainer,
            $"{name}: 40ft {c40.TotalInContainer} < 20ft {c20.TotalInContainer}");
    }
}
