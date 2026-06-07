namespace CargoFit;

// Which packing phase produced a box. Only Scatter boxes are draggable in the manual
// leftover editor (Feature 2); everything else is locked. Kind is pure metadata set by
// PackingEngine at creation — it never affects packing geometry.
public enum PlacementKind { Primary, Condo, Mixed, Scatter }

public record BoxPlacement(
    double X, double Y, double Z,
    double BW, double BL, double BH,
    int ProductIndex,
    bool Rotated = false,
    int StackIndex = 0,
    int LayerIndex = 0,
    PlacementKind Kind = PlacementKind.Primary);
