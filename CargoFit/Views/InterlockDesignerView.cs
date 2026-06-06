using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace CargoFit;

// Interlock Designer (Feature 1) — a stand-alone "calculator" workspace. Enter a box (W×L×H) and a
// container width (default 235, every CargoFit container), press คำนวณ, and it auto-designs the
// max-area interlock layer pattern (PatternA + the crossing PatternB) via InterlockDesigner and shows
// an accurate top view of both layers. Optionally saves the result onto a product's PatternA/B.
public class InterlockDesignerView : UserControl
{
    // ── Palette (mirrors PlanningView) ─────────────────────────────────────────
    private static readonly SolidColorBrush Surface     = new(Color.Parse("#FFFFFF"));
    private static readonly SolidColorBrush SurfaceSub  = new(Color.Parse("#F8FAFC"));
    private static readonly SolidColorBrush BorderLight = new(Color.Parse("#E2E8F0"));
    private static readonly SolidColorBrush Ink         = new(Color.Parse("#1E293B"));
    private static readonly SolidColorBrush InkMuted    = new(Color.Parse("#64748B"));
    private static readonly SolidColorBrush AccentText  = new(Color.Parse("#1D4ED8"));
    private static readonly SolidColorBrush Upright     = new(Color.Parse("#4C6EF5")); // box upright
    private static readonly SolidColorBrush Rotated     = new(Color.Parse("#FAB005")); // box rotated 90°
    private static readonly SolidColorBrush Success     = new(Color.Parse("#15803D"));
    private static readonly SolidColorBrush Danger      = new(Color.Parse("#DC2626"));

    private const double PreviewScale = 2.55;   // px per cm (235 cm ≈ 600 px)

    private readonly TextBox _wBox     = NumInput("25.8");
    private readonly TextBox _lBox     = NumInput("38.5");
    private readonly TextBox _hBox     = NumInput("15.7");
    private readonly TextBox _widthBox = NumInput("235");

    private readonly TextBlock _summary = new()
    {
        Text = "กรอกขนาดกล่องแล้วกด คำนวณ", FontSize = 14, Foreground = InkMuted,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
    };

    private readonly Canvas    _previewA = new();
    private readonly Canvas    _previewB = new();
    private readonly TextBlock _titleA   = SectionTitle("Layer A");
    private readonly TextBlock _titleB   = SectionTitle("Layer B (ขัด — สลับด้าน)");

    private readonly ComboBox  _productCombo = new() { MinWidth = 230, FontSize = 13 };
    private readonly TextBlock _saveStatus   = new() { FontSize = 12, Foreground = InkMuted, VerticalAlignment = VerticalAlignment.Center };

    private InterlockResult? _last;
    private double _w, _l, _h, _cw;

    public InterlockDesignerView()
    {
        Margin = new Thickness(20, 14, 20, 20);

        var root = new DockPanel { LastChildFill = true };

        // ── Top: inputs ────────────────────────────────────────────────────────
        var inputCard = Card(new Thickness(16, 12));
        DockPanel.SetDock(inputCard, Dock.Top);
        var inputs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
        inputs.Children.Add(Field("กว้าง W (ซม.)", _wBox));
        inputs.Children.Add(Field("ยาว L (ซม.)",   _lBox));
        inputs.Children.Add(Field("สูง H (ซม.)",   _hBox));
        inputs.Children.Add(Field("ความกว้างตู้ (ซม.)", _widthBox));

        var calcBtn = new Button
        {
            Content = "คำนวณ", FontSize = 14, FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(22, 8), VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.Parse("#2563EB")), Foreground = Brushes.White,
            CornerRadius = new CornerRadius(8)
        };
        calcBtn.Click += (_, _) => Calculate();
        inputs.Children.Add(new StackPanel { Children = { new TextBlock { Height = 18 }, calcBtn } });

        inputCard.Child = new StackPanel { Children = { inputs, _summary } };
        root.Children.Add(inputCard);

        // ── Bottom: save-to-product row ──────────────────────────────────────────
        var saveCard = Card(new Thickness(16, 10));
        saveCard.Margin = new Thickness(0, 12, 0, 0);
        DockPanel.SetDock(saveCard, Dock.Bottom);
        var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        saveRow.Children.Add(new TextBlock { Text = "บันทึกลงสินค้า:", FontSize = 13, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center });
        foreach (var s in ProductSpec.All)
            _productCombo.Items.Add($"{s.Description} {s.Content} ({s.PackSize})");
        if (_productCombo.Items.Count > 0) _productCombo.SelectedIndex = 0;
        saveRow.Children.Add(_productCombo);
        var saveBtn = new Button
        {
            Content = "บันทึก PatternA/B", FontSize = 13, Padding = new Thickness(14, 6),
            Background = SurfaceSub, BorderBrush = BorderLight, BorderThickness = new Thickness(1),
            Foreground = AccentText, CornerRadius = new CornerRadius(8)
        };
        saveBtn.Click += (_, _) => SaveToProduct();
        saveRow.Children.Add(saveBtn);
        saveRow.Children.Add(_saveStatus);
        saveCard.Child = saveRow;
        root.Children.Add(saveCard);

        // ── Centre: A / B previews ───────────────────────────────────────────────
        var previews = new StackPanel { Spacing = 14, Margin = new Thickness(0, 12, 0, 0) };
        previews.Children.Add(PreviewCard(_titleA, _previewA));
        previews.Children.Add(PreviewCard(_titleB, _previewB));
        previews.Children.Add(Legend());
        root.Children.Add(new ScrollViewer { Content = previews, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        Content = root;
        Calculate();   // show a result on first load
    }

    // ── Compute + render ────────────────────────────────────────────────────────
    private void Calculate()
    {
        if (!TryParse(_wBox, out _w) || !TryParse(_lBox, out _l) || !TryParse(_hBox, out _h))
        {
            _summary.Text = "กรุณากรอกขนาดกล่องให้ครบ (ตัวเลข)";
            _summary.Foreground = Danger;
            return;
        }
        if (!TryParse(_widthBox, out _cw) || _cw <= 0) _cw = InterlockDesigner.DefaultContainerW;

        var r = InterlockDesigner.Generate(_w, _l, _h, _cw);   // ONE pattern — same for every container
        _last = r;

        if (!r.Interlocks || r.PatternA.Length == 0)
        {
            _summary.Foreground = Danger;
            _summary.Text = r.PatternA.Length == 0
                ? "กล่องใหญ่เกินกว่าจะวางในความกว้างนี้"
                : "กล่องนี้วางแบบขัดไม่ได้ (วางเป็นคอลัมน์อย่างเดียว)";
        }
        else
        {
            _summary.Foreground = Ink;
            string line = $"[{r.Template}]  {r.BoxesPerLayer} ลัง/ชั้น   ·   " +
                          $"กว้าง {r.UsedWidth:F1}/{_cw:F0} ซม. ({r.WidthUtil * 100:F0}%)   ·   " +
                          $"ลึก {r.Depth:F1} ซม.   ·   ใช้พื้นที่ {r.AreaUtil * 100:F0}%";

            // The pattern is the same for every container — report how many of it fit in each size.
            var caps = ContainerSpec.All
                .Select(c => (c.SizeLabel,
                              Total: InterlockDesigner.Generate(_w, _l, _h, c.InteriorW, c.InteriorL, c.InteriorH).TotalInContainer))
                .Where(x => x.Total > 0)
                .Select(x => $"{x.SizeLabel} {x.Total:N0}")
                .ToList();
            if (caps.Count > 0)
                line += "\n📦 ใส่ได้/ตู้ (pattern เดียวกันทุกตู้):  " + string.Join("   ·   ", caps);

            _summary.Text = line;
        }

        _titleB.IsVisible = r.PatternB.Length > 0;
        DrawLayer(_previewA, r.PatternA);
        DrawLayer(_previewB, r.PatternB.Length > 0 ? r.PatternB : System.Array.Empty<LayerSection>());
        _saveStatus.Text = "";
    }

    private void DrawLayer(Canvas canvas, LayerSection[] sections)
    {
        canvas.Children.Clear();
        var boxes = PackingEngine.LayerPlacements(sections, _w, _l, _h, _cw);
        double s = PreviewScale;
        double depth = boxes.Count > 0 ? boxes.Max(b => b.Y + b.BL) : _l;
        canvas.Width  = _cw * s;
        canvas.Height = Math.Max(depth * s, 24);

        // container-width guide
        var guide = new Border
        {
            Width = _cw * s, Height = canvas.Height,
            BorderBrush = BorderLight, BorderThickness = new Thickness(1, 0, 1, 0)
        };
        Canvas.SetLeft(guide, 0); Canvas.SetTop(guide, 0);
        canvas.Children.Add(guide);

        foreach (var b in boxes)
        {
            var rect = new Border
            {
                Width  = Math.Max(b.BW * s - 1.5, 1),
                Height = Math.Max(b.BL * s - 1.5, 1),
                Background = b.Rotated ? Rotated : Upright,
                BorderBrush = Brushes.White, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2)
            };
            Canvas.SetLeft(rect, b.X * s);
            Canvas.SetTop(rect,  b.Y * s);
            canvas.Children.Add(rect);
        }

        if (boxes.Count == 0)
            canvas.Children.Add(new TextBlock { Text = "—", Foreground = InkMuted, FontSize = 13 });
    }

    // ── Save to a product's PatternA/B ────────────────────────────────────────────
    private void SaveToProduct()
    {
        if (_last is null || _last.PatternA.Length == 0)
        {
            _saveStatus.Text = "ยังไม่มี pattern ให้บันทึก"; _saveStatus.Foreground = Danger; return;
        }
        int idx = _productCombo.SelectedIndex;
        if (idx < 0 || idx >= ProductSpec.All.Count)
        {
            _saveStatus.Text = "เลือกสินค้าก่อน"; _saveStatus.Foreground = Danger; return;
        }
        var spec = ProductSpec.All[idx];
        ProductSpec.All[idx] = spec with { PatternA = _last.PatternA, PatternB = _last.PatternB };
        ProductSpec.Save();
        _saveStatus.Text = $"✓ บันทึกลง {spec.Description} {spec.Content} แล้ว"; _saveStatus.Foreground = Success;
    }

    // ── UI helpers ────────────────────────────────────────────────────────────────
    private static bool TryParse(TextBox box, out double value) =>
        double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0;

    private static TextBox NumInput(string initial) => new()
    {
        Text = initial, Width = 96, FontSize = 14,
        Padding = new Thickness(8, 6), CornerRadius = new CornerRadius(6)
    };

    private static Control Field(string label, Control input) => new StackPanel
    {
        Spacing = 3,
        Children =
        {
            new TextBlock { Text = label, FontSize = 11, Foreground = InkMuted },
            input
        }
    };

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Ink,
        Margin = new Thickness(0, 0, 0, 6)
    };

    private static Border PreviewCard(TextBlock title, Canvas canvas)
    {
        var card = Card(new Thickness(16, 12));
        card.Child = new StackPanel
        {
            Children =
            {
                title,
                new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }
            }
        };
        return card;
    }

    private static Control Legend()
    {
        StackPanel Swatch(IBrush b, string t) => new()
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Border { Width = 14, Height = 14, Background = b, CornerRadius = new CornerRadius(3) },
                new TextBlock { Text = t, FontSize = 12, Foreground = InkMuted, VerticalAlignment = VerticalAlignment.Center }
            }
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 18, Margin = new Thickness(2, 0, 0, 4),
            Children = { Swatch(Upright, "กล่องตั้ง"), Swatch(Rotated, "กล่องหมุน 90°") }
        };
    }

    private static Border Card(Thickness padding) => new()
    {
        Background = Surface, BorderBrush = BorderLight, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10), Padding = padding
    };
}
