using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace logistic;

// Visual editor for a layer pattern composed of sections placed left-to-right.
// Each section has its own rows, cols, and box orientation (normal W×L or rotated L×W).
public class LayerPatternEditor : UserControl
{
    private static readonly SolidColorBrush BgNormal    = new(Color.Parse("#EFF6FF"));
    private static readonly SolidColorBrush BgRotated   = new(Color.Parse("#FFF7ED"));
    private static readonly SolidColorBrush BoxNormal   = new(Color.Parse("#3B82F6"));
    private static readonly SolidColorBrush BoxRotated  = new(Color.Parse("#F97316"));
    private static readonly SolidColorBrush CardBorder  = new(Color.Parse("#E2E8F0"));
    private static readonly SolidColorBrush LabelMuted  = new(Color.Parse("#64748B"));
    private static readonly SolidColorBrush LabelStrong = new(Color.Parse("#1E293B"));

    private readonly List<SectionRow> _rows = [];
    private double _boxW;
    private double _boxL;

    private StackPanel _sectionsPanel = null!;
    private TextBlock _summaryLabel   = null!;

    public event Action<LayerSection[]>? PatternChanged;

    public double BoxW { get => _boxW; set { _boxW = value; RefreshAllPreviews(); } }
    public double BoxL { get => _boxL; set { _boxL = value; RefreshAllPreviews(); } }

    public LayerSection[] Pattern
    {
        get => _rows.Select(r => r.ToSection()).ToArray();
        set
        {
            _rows.Clear();
            _sectionsPanel?.Children.Clear();
            foreach (var s in value)
                AddSection(s);
            UpdateSummary();
        }
    }

    public LayerPatternEditor(double boxW = 25.0, double boxL = 38.0)
    {
        _boxW = boxW;
        _boxL = boxL;
        Build();
    }

    private void Build()
    {
        var root = new StackPanel { Spacing = 8 };

        _sectionsPanel = new StackPanel { Spacing = 6 };
        root.Children.Add(_sectionsPanel);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        var addBtn = new Button
        {
            Content = "+ เพิ่ม Section",
            FontSize = 12,
            Padding = new Thickness(10, 5),
            Classes = { "outline" }
        };
        addBtn.Click += (_, _) =>
        {
            AddSection(new LayerSection(2, 4, false));
            UpdateSummary();
            Emit();
        };
        footer.Children.Add(addBtn);

        _summaryLabel = new TextBlock
        {
            FontSize = 12,
            Foreground = LabelMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        footer.Children.Add(_summaryLabel);

        root.Children.Add(footer);
        Content = root;
        UpdateSummary();
    }

    private void AddSection(LayerSection s)
    {
        var row = new SectionRow(s, _boxW, _boxL, OnSectionChanged, OnSectionRemoved);
        _rows.Add(row);
        _sectionsPanel.Children.Add(row.Card);
    }

    private void OnSectionChanged()
    {
        RefreshAllPreviews();
        UpdateSummary();
        Emit();
    }

    private void OnSectionRemoved(SectionRow row)
    {
        _sectionsPanel.Children.Remove(row.Card);
        _rows.Remove(row);
        UpdateSummary();
        Emit();
    }

    private void RefreshAllPreviews()
    {
        foreach (var r in _rows)
            r.UpdatePreview(_boxW, _boxL);
    }

    private void UpdateSummary()
    {
        int total = _rows.Sum(r => r.Rows * r.Cols);
        _summaryLabel.Text = total > 0 ? $"รวม {total} ลัง/ชั้น" : "";
    }

    private void Emit() => PatternChanged?.Invoke(Pattern);

    // ── Inner class: one section card ────────────────────────────────────────

    private sealed class SectionRow
    {
        public Border Card { get; }
        public int Rows { get; private set; }
        public int Cols { get; private set; }
        private bool _rotated;

        private readonly Action _onChange;
        private readonly Action<SectionRow> _onRemove;
        private Canvas _preview = null!;

        public LayerSection ToSection() => new(Rows, Cols, _rotated);

        public SectionRow(LayerSection s, double boxW, double boxL, Action onChange, Action<SectionRow> onRemove)
        {
            Rows = s.Rows;
            Cols = s.Cols;
            _rotated = s.Rotated;
            _onChange = onChange;
            _onRemove = onRemove;
            Card = BuildCard(boxW, boxL);
        }

        private Border BuildCard(double boxW, double boxL)
        {
            var inner = new StackPanel { Spacing = 8 };

            // ── Controls row ──────────────────────────────────────────────────
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            controls.Children.Add(LabelText("แถว"));
            controls.Children.Add(SpinBox(Rows, v => { Rows = v; _onChange(); }));

            controls.Children.Add(LabelText("คอลัมน์"));
            controls.Children.Add(SpinBox(Cols, v => { Cols = v; _onChange(); }));

            // Orientation toggle button
            var orientBtn = new Button
            {
                FontSize = 11,
                Padding = new Thickness(8, 4),
                Classes = { "outline" }
            };
            void RefreshOrientBtn() =>
                orientBtn.Content = _rotated ? "↺ Rotated (L×W)" : "→ Normal (W×L)";
            RefreshOrientBtn();
            orientBtn.Click += (_, _) =>
            {
                _rotated = !_rotated;
                RefreshOrientBtn();
                _onChange();
            };
            controls.Children.Add(orientBtn);

            // Delete button
            var delBtn = new Button
            {
                Content = "✕",
                FontSize = 11,
                Padding = new Thickness(6, 4),
                Classes = { "danger" },
                Margin = new Thickness(4, 0, 0, 0)
            };
            delBtn.Click += (_, _) => _onRemove(this);
            controls.Children.Add(delBtn);

            inner.Children.Add(controls);

            // ── Preview canvas ────────────────────────────────────────────────
            _preview = new Canvas { Height = 60 };
            DrawPreview(boxW, boxL);
            inner.Children.Add(_preview);

            var card = new Border
            {
                Background = _rotated ? BgRotated : BgNormal,
                BorderBrush = CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10)
            };
            card.Child = inner;
            return card;
        }

        public void UpdatePreview(double boxW, double boxL)
        {
            _preview.Children.Clear();
            DrawPreview(boxW, boxL);
            Card.Background = _rotated ? BgRotated : BgNormal;
        }

        private void DrawPreview(double boxW, double boxL)
        {
            double bw = _rotated ? boxL : boxW;
            double bl = _rotated ? boxW : boxL;

            // Scale so the whole pattern fits within 200×50 px
            double patW = Cols * bw;
            double patH = Rows * bl;
            double scale = Math.Min(200.0 / Math.Max(patW, 1), 50.0 / Math.Max(patH, 1));

            double cellW = bw * scale;
            double cellH = bl * scale;

            var fill = _rotated ? BoxRotated : BoxNormal;

            for (int r = 0; r < Rows; r++)
            {
                for (int c = 0; c < Cols; c++)
                {
                    var rect = new Border
                    {
                        Width  = Math.Max(cellW - 2, 1),
                        Height = Math.Max(cellH - 2, 1),
                        Background = fill,
                        BorderBrush = Brushes.White,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(2)
                    };
                    Canvas.SetLeft(rect, c * cellW + 1);
                    Canvas.SetTop(rect,  r * cellH + 1);
                    _preview.Children.Add(rect);
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static TextBlock LabelText(string t) => new()
        {
            Text = t,
            FontSize = 12,
            Foreground = LabelStrong,
            VerticalAlignment = VerticalAlignment.Center
        };

        private static Border SpinBox(int initial, Action<int> onChanged)
        {
            int value = initial;
            var label = new TextBlock
            {
                Text = value.ToString(),
                Width = 24,
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            void Update(int delta)
            {
                value = Math.Max(1, value + delta);
                label.Text = value.ToString();
                onChanged(value);
            }

            var minus = new Button { Content = "−", FontSize = 11, Padding = new Thickness(5, 2) };
            var plus  = new Button { Content = "+", FontSize = 11, Padding = new Thickness(5, 2) };
            minus.Click += (_, _) => Update(-1);
            plus.Click  += (_, _) => Update(+1);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            row.Children.Add(minus);
            row.Children.Add(label);
            row.Children.Add(plus);

            return new Border { Child = row };
        }
    }
}
