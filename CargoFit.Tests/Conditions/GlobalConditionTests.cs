using System.Collections.Generic;
using System.Linq;
using CargoFit;
using Xunit;
using Xunit.Abstractions;

namespace CargoFit.Tests.Conditions;

/// <summary>
/// Global loading condition — single check: the cargo must fill the container deep enough that the
/// free space left at the DOOR is ≤ 50 cm. (Y = depth: 0 = innermost/back wall, high = door.)
/// </summary>
public class GlobalConditionTests(ITestOutputHelper log)
{
    const double MaxFreeDoorSpaceCm = 50.0;

    // Cases come from TestHelpers.Cases() — the SAME list ProofGen renders in the HTML preview.
    // Add a case there once → it is BOTH previewed and door-checked here. (MemberData carries only the
    // label, then we look the case up — keeps the test display name readable and serializable.)
    public static IEnumerable<object[]> Cases() =>
        TestHelpers.Cases().Select(c => new object[] { c.Label });

    [Theory]
    [MemberData(nameof(Cases))]
    public void FreeDoorSpace_AtMost50cm(string label)
    {
        var c = TestHelpers.Cases().First(x => x.Label == label);

        var output = PackingEngine.Calculate(c.Container, c.Requests);
        log.WriteLine($"--- {label} ---");
        TestHelpers.DumpOutput(c.Container, c.Requests, output, log);

        double maxY     = output.Placements.Count > 0 ? output.Placements.Max(p => p.Y + p.BL) : 0;
        double freeDoor = c.Container.InteriorL - maxY;
        log.WriteLine($"  free door space = {c.Container.InteriorL:F0} - {maxY:F1} = {freeDoor:F1} cm  (limit {MaxFreeDoorSpaceCm:F0})");

        Assert.True(freeDoor <= MaxFreeDoorSpaceCm + 0.01,
            $"[{label}] free door space {freeDoor:F1}cm exceeds {MaxFreeDoorSpaceCm:F0}cm");
    }
}
