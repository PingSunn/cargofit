using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace CargoFit;

/// <summary>
/// 2D top-down editor for the leftover (Scatter) boxes of a computed packing layout (Feature 2).
/// Primary/Condo/Mixed boxes are drawn locked; only Scatter boxes can be dragged (and rotated).
/// Dropping uses auto-gravity (the box rests on whatever is under its footprint) with strict
/// validation — inside the container, under the ceiling, fully supported (no float), no overlap.
/// Edits mutate the SAME list the 3D canvas / PDF hold, so they show up everywhere immediately.
/// </summary>
public class LeftoverEditCanvas : Control
{
    private static readonly SolidColorBrush Bg            = new(Color.Parse("#F1F5F9"));
    private static readonly SolidColorBrush ContainerFill = new(Color.Parse("#FFFFFF"));
    private static readonly SolidColorBrush LockedFill    = new(Color.Parse("#E2E8F0"));
    private static readonly SolidColorBrush Ink           = new(Color.Parse("#475569"));
    private static readonly SolidColorBrush InkFaint      = new(Color.Parse("#94A3B8"));
    private static readonly IPen ContainerPen = new Pen(new SolidColorBrush(Color.Parse("#94A3B8")), 2);
    private static readonly IPen LockedPen    = new Pen(new SolidColorBrush(Color.Parse("#CBD5E1")), 1);
    private static readonly IPen BoxPen       = new Pen(new SolidColorBrush(Color.Parse("#1E293B")), 1);
    private static readonly IPen SelectedPen  = new Pen(new SolidColorBrush(Color.Parse("#1D4ED8")), 2.5);
    private static readonly IPen GhostOkPen   = new Pen(new SolidColorBrush(Color.Parse("#16A34A")), 2.5);
    private static readonly IPen GhostBadPen  = new Pen(new SolidColorBrush(Color.Parse("#DC2626")), 2.5);
    private static readonly SolidColorBrush GhostOkFill  = new(Color.FromArgb(0x44, 0x16, 0xA3, 0x4A));
    private static readonly SolidColorBrush GhostBadFill = new(Color.FromArgb(0x44, 0xDC, 0x26, 0x26));

    private const double Pad = 28;

    private ContainerSpec?     _container;
    private List<BoxPlacement>? _boxes;
    private double _cutRatio = 1.0;

    private int _selected = -1;
    private int _dragIndex = -1;
    private double _grabOffX, _grabOffY;          // grab point relative to box origin (world cm)
    private double _dragX, _dragY;                 // raw dragged origin (world cm)
    private LeftoverEditGeometry.DropResult _drop; // live snapped landing spot + validity

    // cached world→screen transform (updated each Render)
    private double _scale = 1, _offX, _offY;

    /// <summary>Raised after a successful move/rotate so the host can refresh the 3D view.</summary>
    public event Action? EditCommitted;

    public LeftoverEditCanvas() { Cursor = new Cursor(StandardCursorType.Hand); }

    public double InteriorH => _container?.InteriorH ?? 0;
    public bool   HasSelection => _selected >= 0 && _boxes is not null && _selected < _boxes.Count;

    public void SetData(ContainerSpec container, List<BoxPlacement> boxes)
    {
        _container = container;
        _boxes     = boxes;
        _selected  = -1;
        _dragIndex = -1;
        InvalidateVisual();
    }

    public void SetCutRatio(double ratio)
    {
        _cutRatio = Math.Clamp(ratio, 0, 1);
        InvalidateVisual();
    }

    public void ClearSelection()
    {
        _selected = -1;
        InvalidateVisual();
    }

    /// <summary>Rotate the selected Scatter box 90° in place (if the rotated footprint still validates).</summary>
    public void RotateSelected()
    {
        if (_boxes is null || _container is null || !HasSelection) return;
        var b = _boxes[_selected];
        if (b.Kind != PlacementKind.Scatter) return;

        double w = b.BL, l = b.BW;   // swap footprint
        var others = Others(_selected);
        var drop = LeftoverEditGeometry.TryDrop(others, b.X, b.Y, w, l, b.BH,
                       _container.InteriorW, _container.InteriorL, _container.InteriorH);
        if (!drop.Ok) return;        // can't rotate here — leave as is

        int si = LeftoverEditGeometry.LandingStackIndex(others, drop.X, drop.Y, w, l, drop.Z, b.StackIndex);
        _boxes[_selected] = b with
        {
            BW = w, BL = l, Rotated = !b.Rotated,
            X = drop.X, Y = drop.Y, Z = drop.Z,
            LayerIndex = (int)Math.Round(drop.Z / Math.Max(b.BH, 0.01)),
            StackIndex = si
        };
        EditCommitted?.Invoke();
        InvalidateVisual();
    }

    private List<BoxPlacement> Others(int exclude)
    {
        var list = new List<BoxPlacement>(_boxes!.Count);
        for (int i = 0; i < _boxes.Count; i++)
            if (i != exclude) list.Add(_boxes[i]);
        return list;
    }

    private double CutZ => _cutRatio * InteriorH;

    // ── pointer ──────────────────────────────────────────────────────────────--
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_boxes is null || _container is null) return;
        var p = e.GetPosition(this);
        double wx = (p.X - _offX) / _scale, wy = (p.Y - _offY) / _scale;

        int idx = LeftoverEditGeometry.PickTopScatter(_boxes, wx, wy, CutZ);
        _selected = idx;
        if (idx >= 0)
        {
            var b = _boxes[idx];
            _dragIndex = idx;
            _grabOffX  = wx - b.X;
            _grabOffY  = wy - b.Y;
            _dragX     = b.X;
            _dragY     = b.Y;
            _drop      = new LeftoverEditGeometry.DropResult(true, b.X, b.Y, b.Z);
            e.Pointer.Capture(this);
        }
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_dragIndex < 0 || _boxes is null || _container is null) return;
        var p = e.GetPosition(this);
        _dragX = (p.X - _offX) / _scale - _grabOffX;
        _dragY = (p.Y - _offY) / _scale - _grabOffY;

        var b = _boxes[_dragIndex];
        _drop = LeftoverEditGeometry.TryDrop(Others(_dragIndex), _dragX, _dragY, b.BW, b.BL, b.BH,
                    _container.InteriorW, _container.InteriorL, _container.InteriorH);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragIndex < 0 || _boxes is null || _container is null) { _dragIndex = -1; return; }
        var b = _boxes[_dragIndex];
        var others = Others(_dragIndex);
        var drop = LeftoverEditGeometry.TryDrop(others, _dragX, _dragY, b.BW, b.BL, b.BH,
                       _container.InteriorW, _container.InteriorL, _container.InteriorH);
        if (drop.Ok)
        {
            // Re-file the box under the stack it now rests on, so the PDF lists it with that ต๊ง.
            int si = LeftoverEditGeometry.LandingStackIndex(others, drop.X, drop.Y, b.BW, b.BL, drop.Z, b.StackIndex);
            _boxes[_dragIndex] = b with
            {
                X = drop.X, Y = drop.Y, Z = drop.Z,
                LayerIndex = (int)Math.Round(drop.Z / Math.Max(b.BH, 0.01)),
                StackIndex = si
            };
            EditCommitted?.Invoke();
        }
        _dragIndex = -1;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    // ── render ─────────────────────────────────────────────────────────────────
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        context.FillRectangle(Bg, new Rect(0, 0, bounds.Width, bounds.Height));
        if (_container is null || _boxes is null || bounds.Width < 20 || bounds.Height < 20) return;

        double cw = _container.InteriorW, cl = _container.InteriorL;
        _scale = Math.Min((bounds.Width - 2 * Pad) / cw, (bounds.Height - 2 * Pad) / cl);
        if (_scale <= 0) return;
        _offX = (bounds.Width  - cw * _scale) / 2;
        _offY = (bounds.Height - cl * _scale) / 2;

        // container floor + outline
        context.DrawRectangle(ContainerFill, ContainerPen, ScreenRect(0, 0, cw, cl));

        DrawLabel(context, "◄ ในสุด (หลังตู้)", new Point(_offX, _offY - 18), InkFaint, 11);
        DrawLabel(context, "ประตู ►", new Point(_offX, _offY + cl * _scale + 4), InkFaint, 11);

        double cutZ = CutZ;
        // paint bottom-up so the topmost (scatter) boxes land on top; skip the box being dragged
        foreach (var (b, i) in _boxes.Select((b, i) => (b, i))
                                     .Where(t => t.b.Z < cutZ - 0.01 && t.i != _dragIndex)
                                     .OrderBy(t => t.b.Z))
        {
            var rect = ScreenRect(b.X, b.Y, b.BW, b.BL);
            if (b.Kind == PlacementKind.Scatter)
            {
                var c = IsometricCanvas.GetProductColor(b.ProductIndex);
                var fill = new SolidColorBrush(Color.FromArgb(0xD8, c.R, c.G, c.B));
                context.DrawRectangle(fill, i == _selected ? SelectedPen : BoxPen, rect, 2, 2);
            }
            else
            {
                context.DrawRectangle(LockedFill, LockedPen, rect, 1, 1);
            }
        }

        // drag ghost at the live snapped landing spot
        if (_dragIndex >= 0)
        {
            var b = _boxes[_dragIndex];
            var ghost = ScreenRect(_drop.X, _drop.Y, b.BW, b.BL);
            context.DrawRectangle(_drop.Ok ? GhostOkFill : GhostBadFill,
                                  _drop.Ok ? GhostOkPen  : GhostBadPen, ghost, 2, 2);
        }

        DrawLabel(context,
            _dragIndex >= 0
                ? (_drop.Ok ? "วางได้" : "วางไม่ได้ (ทับ/ลอย/พ้นตู้)")
                : "ลากกล่องสีเพื่อย้าย · กล่องเทาคือล็อก",
            new Point(_offX, bounds.Height - 20),
            _dragIndex >= 0 && !_drop.Ok ? new SolidColorBrush(Color.Parse("#DC2626")) : Ink, 12);
    }

    private Rect ScreenRect(double wx, double wy, double w, double l)
        => new(_offX + wx * _scale, _offY + wy * _scale, w * _scale, l * _scale);

    private static void DrawLabel(DrawingContext ctx, string text, Point at, IBrush brush, double size)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                   Typeface.Default, size, brush);
        ctx.DrawText(ft, at);
    }
}
