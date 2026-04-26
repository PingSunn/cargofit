using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace logistic;

/// <summary>
/// Represents a shipping container type.
/// Nominal = real physical size shown to the user.
/// Interior = usable space for calculation (nominal − 5 cm per side).
/// </summary>
public record ContainerSpec(
    string Name,
    string SizeLabel,
    int NominalW,
    int NominalL,
    int NominalH)
{
    [JsonIgnore] public int InteriorW => NominalW - 5;
    [JsonIgnore] public int InteriorL => NominalL - 5;
    [JsonIgnore] public int InteriorH => NominalH - 5;

    public static readonly List<ContainerSpec> All =
    [
        new("ตู้สั้น",     "20 ft",    244, 600,  259),
        new("ตู้ยาว",     "40 ft",    244, 1209, 260),
        new("ตู้ไฮคิวบ์", "40 ft HC", 244, 1203, 290),
    ];

    private static readonly string FilePath = AppPaths.ContainersFile;

    public static void Load()
    {
        if (!File.Exists(FilePath)) return;
        try
        {
            var json = File.ReadAllText(FilePath);
            var specs = JsonSerializer.Deserialize<ContainerSpec[]>(json, JsonOptions.WriteIndented);
            if (specs is null || specs.Length == 0) return;
            All.Clear();
            All.AddRange(specs);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ContainerSpec.Load] {ex}"); /* keep defaults */ }
    }

    public static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(All, JsonOptions.WriteIndented));
    }
}
