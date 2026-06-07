using System.Linq;
using CargoFit;
using Xunit;

namespace CargoFit.Tests.Export;

/// <summary>
/// Smoke test: the PDF actually renders for the shared cases (which include scatter + condo + a
/// cross-product filler), exercising the loading-sequence builder incl. the 3D iso on stack cards
/// and cross-product scatter attached to its host stack.
/// </summary>
public class PdfExporterTests
{
    [Fact]
    public void Generates_valid_pdf_for_all_shared_cases()
    {
        foreach (var c in TestHelpers.Cases())
        {
            var output = PackingEngine.Calculate(c.Container, c.Requests);
            byte[] pdf = PdfExporter.Generate(c.Container, c.Requests, output);

            Assert.True(pdf.Length > 1000, $"[{c.Label}] PDF unexpectedly small ({pdf.Length} bytes)");
            Assert.True(pdf is [0x25, 0x50, 0x44, 0x46, ..], $"[{c.Label}] missing %PDF header");
        }
    }

    /// <summary>
    /// The "Mogu1000×1000 · Mogu320×120" case routes the small SKU through the filler path, leaving a
    /// few leftovers on another product's stack (cross-product scatter). This locks that the case still
    /// produces such boxes, so the PDF smoke above actually exercises the host-attachment path (R2a).
    /// </summary>
    [Fact]
    public void Filler_case_produces_cross_product_scatter()
    {
        var c = TestHelpers.Cases().First(x => x.Label.Contains("Mogu1000×1000"));
        var output = PackingEngine.Calculate(c.Container, c.Requests);

        bool hasCrossProduct = output.Placements.Any(s =>
            s.Kind == PlacementKind.Scatter &&
            output.Placements.Any(o =>
                o.Kind != PlacementKind.Scatter &&
                o.StackIndex < PackingEngine.CondoStackBase &&
                o.ProductIndex != s.ProductIndex &&
                o.Z + o.BH <= s.Z + 0.5 &&
                o.X < s.X + s.BW - 0.01 && s.X < o.X + o.BW - 0.01 &&
                o.Y < s.Y + s.BL - 0.01 && s.Y < o.Y + o.BL - 0.01));

        Assert.True(hasCrossProduct,
            "expected a filler leftover sitting on another product's stack (exercises PDF host-attachment)");
    }
}
