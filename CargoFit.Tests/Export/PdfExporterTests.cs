using CargoFit;
using Xunit;

namespace CargoFit.Tests.Export;

/// <summary>
/// Smoke test: the PDF actually renders for the shared cases (which include scatter + condo),
/// exercising the loading-sequence builder — including the new separate "กระจาย" (scatter) units.
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
}
