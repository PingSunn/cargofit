using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CargoFit;

/// <summary>
/// Represents a shipping container type.
/// Nominal  = real physical size shown to the user (the 3D shell).
/// Interior = usable space used for packing calculation (measured, stored explicitly —
///            real containers don't shrink uniformly so a single gap can't express it).
/// MaxWeightTons / MaxVolumeCbm = rated limits; 0 = ignore (no limit).
/// </summary>
public record ContainerSpec(
    string Name,
    string SizeLabel,
    int NominalW,
    int NominalL,
    int NominalH,
    int InteriorW,
    int InteriorL,
    int InteriorH,
    double MaxWeightTons = 0,
    double MaxVolumeCbm = 0)
{
    public static readonly List<ContainerSpec> All =
    [
        new("ตู้สั้น",     "20 ft",    244, 600,  259, 235, 586,  238, MaxWeightTons: 0, MaxVolumeCbm: 33),
        new("ตู้ยาว",     "40 ft",    244, 1209, 260, 235, 1203, 238, MaxWeightTons: 0,  MaxVolumeCbm: 67.5),
        new("ตู้ไฮคิวบ์", "40 ft HC", 244, 1203, 290, 235, 1203, 270, MaxWeightTons: 0,  MaxVolumeCbm: 76.2),
    ];

    private static readonly string FilePath = AppPaths.ContainersFile;

    public static void Load()
    {
        if (!File.Exists(FilePath))
            SeedFromResource();

        if (!File.Exists(FilePath)) return;
        try
        {
            var json = File.ReadAllText(FilePath);
            var specs = JsonSerializer.Deserialize<ContainerSpec[]>(json, JsonOptions.WriteIndented);
            if (specs is null || specs.Length == 0) return;
            All.Clear();
            // Migration: pre-interior files (old Gap-only schema) deserialize interior to 0 — fall back to nominal−10.
            foreach (var s in specs)
                All.Add(s.InteriorW > 0 && s.InteriorL > 0 && s.InteriorH > 0
                    ? s
                    : s with { InteriorW = s.NominalW - 10, InteriorL = s.NominalL - 10, InteriorH = s.NominalH - 10 });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ContainerSpec.Load] {ex}"); /* keep defaults */ }
    }

    /// <summary>
    /// Extracts the bundled containers.json from the assembly to DataDir on first run.
    /// </summary>
    private static void SeedFromResource()
    {
        try
        {
            var asm = typeof(ContainerSpec).Assembly;
            using var stream = asm.GetManifestResourceStream("CargoFit.containers.json");
            if (stream is null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            using var fs = File.Create(FilePath);
            stream.CopyTo(fs);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ContainerSpec.Seed] {ex}"); }
    }

    public static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(All, JsonOptions.WriteIndented));
    }
}
