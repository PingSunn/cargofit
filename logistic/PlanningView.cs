using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace logistic;

public class PlanningView : UserControl
{
    // ── Palette ─────────────────────────────────────────────────────────────
    private static readonly SolidColorBrush Surface     = new(Color.Parse("#FFFFFF"));
    private static readonly SolidColorBrush SurfaceSub  = new(Color.Parse("#F8FAFC"));
    private static readonly SolidColorBrush BorderLight = new(Color.Parse("#E2E8F0"));
    private static readonly SolidColorBrush Ink         = new(Color.Parse("#1E293B"));
    private static readonly SolidColorBrush InkMuted    = new(Color.Parse("#64748B"));
    private static readonly SolidColorBrush AccentBg    = new(Color.Parse("#EFF6FF"));
    private static readonly SolidColorBrush AccentBorder= new(Color.Parse("#93C5FD"));
    private static readonly SolidColorBrush AccentText  = new(Color.Parse("#1D4ED8"));

    // ── State ────────────────────────────────────────────────────────────────
    private readonly List<Border> _containerItems = [];
    private int _selectedContainerIndex = 0;

    private readonly List<(CheckBox Cb, ProductSpec Spec, Border Wrapper)> _products = [];
    private readonly Dictionary<ProductSpec, (Border Row, TextBox QtyBox)> _qtyMap = [];

    private StackPanel _quantityRows = null!;
    private IsometricCanvas _canvas = null!;
    private StackPanel _statsPanel = null!;
    private readonly HashSet<int> _hiddenProducts = [];
    private Slider _cutSlider = null!;
    private TextBlock _cutLabel = null!;

    public PlanningView()
    {
        Margin = new Thickness(20, 14, 20, 20);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(16)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(296)));

        grid.Children.Add(BuildLeftPanel());
        var right = BuildRightPanel();
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        Content = grid;

        if (ContainerSpec.All.Count > 0)
            _canvas.SetData(ContainerSpec.All[0], []);
    }

    // ── Left panel ───────────────────────────────────────────────────────────

    private Control BuildLeftPanel()
    {
        var dock = new DockPanel { LastChildFill = true };

        // Stats card — docked to bottom
        var statsCard = Card(padding: new Thickness(16, 12));
        statsCard.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(statsCard, Dock.Bottom);

        _statsPanel = new StackPanel { Spacing = 5 };
        _statsPanel.Children.Add(new TextBlock
        {
            Text = "กด Start เพื่อคำนวณการบรรจุ",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = InkMuted,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        });

        statsCard.Child = new ScrollViewer
        {
            Content = _statsPanel,
            MaxHeight = 210,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        dock.Children.Add(statsCard);

        // Canvas config bar — docked above stats
        var sliderRow = new Border
        {
            Background = Surface,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6),
            Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(sliderRow, Dock.Bottom);

        // Chip buttons row
        var chipRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Margin = new Thickness(0, 0, 0, 6)
        };

        var resetBtn = new Button
        {
            Content = "↺ รีเซ็ต",
            FontSize = 10,
            Padding = new Thickness(7, 2),
            CornerRadius = new CornerRadius(5),
            Background = SurfaceSub,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            Foreground = InkMuted,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        resetBtn.Click += (_, _) => _canvas.ResetView();
        chipRow.Children.Add(resetBtn);
        chipRow.Children.Add(MakeChip("ลวดลาย",    false, v => _canvas.SetWireframeMode(v)));
        chipRow.Children.Add(MakeChip("สีตามชั้น", false, v => _canvas.SetColorByLayer(v)));
        chipRow.Children.Add(MakeChip("สีสลับชั้น", false, v => _canvas.SetColorByStackLayer(v)));
        chipRow.Children.Add(MakeChip("แสดงขนาด",  true,  v => _canvas.SetShowDimensions(v)));

        var sliderGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,38") };

        sliderGrid.Children.Add(new TextBlock
        {
            Text = "แสดงชั้น",
            FontSize = 11,
            FontWeight = FontWeight.Medium,
            Foreground = InkMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });

        _cutSlider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = 1,
            SmallChange = 0.05,
            LargeChange = 0.1,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_cutSlider, 1);
        sliderGrid.Children.Add(_cutSlider);

        _cutLabel = new TextBlock
        {
            Text = "100%",
            FontSize = 11,
            Foreground = InkMuted,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(10, 0, 0, 0)
        };
        Grid.SetColumn(_cutLabel, 2);
        sliderGrid.Children.Add(_cutLabel);

        _cutSlider.ValueChanged += (_, _) =>
        {
            _canvas.SetCutRatio(_cutSlider.Value);
            _cutLabel.Text = $"{(int)Math.Round(_cutSlider.Value * 100)}%";
        };

        var controlStack = new StackPanel { Spacing = 0 };
        controlStack.Children.Add(chipRow);
        controlStack.Children.Add(sliderGrid);
        sliderRow.Child = controlStack;
        dock.Children.Add(sliderRow);

        // Canvas card — fills remaining space
        var canvasCard = new Border
        {
            Background = SurfaceSub,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            ClipToBounds = true
        };
        _canvas = new IsometricCanvas
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        canvasCard.Child = _canvas;
        dock.Children.Add(canvasCard);

        return dock;
    }

    // ── Right panel ──────────────────────────────────────────────────────────

    private Control BuildRightPanel()
    {
        var panel = new Border
        {
            Background = Surface,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18, 16, 18, 16)
        };

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        rootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var inner = new StackPanel { Spacing = 4 };

        // Container section
        inner.Children.Add(SectionLabel("ตู้คอนเทนเนอร์"));
        var containerStack = new StackPanel { Spacing = 5, Margin = new Thickness(0, 4, 0, 8) };
        for (int i = 0; i < ContainerSpec.All.Count; i++)
        {
            int idx = i;
            var c = ContainerSpec.All[i];
            var item = MakeContainerItem(c.Name, c.SizeLabel, i == 0);
            item.PointerPressed += (_, _) => SelectContainer(idx);
            containerStack.Children.Add(item);
            _containerItems.Add(item);
        }
        inner.Children.Add(containerStack);

        // Product section
        inner.Children.Add(SectionLabel("สินค้า"));

        var searchBox = new TextBox
        {
            Watermark = "ค้นหา...",
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 5)
        };
        inner.Children.Add(searchBox);

        var productStack = new StackPanel { Spacing = 3 };
        foreach (var spec in ProductSpec.All)
        {
            var label = $"{spec.Description} {spec.Content}";

            var contentGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
            contentGrid.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = Ink,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var packTag = new Border
            {
                Background = Surface,
                BorderBrush = BorderLight,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            packTag.Child = new TextBlock { Text = spec.PackSize, FontSize = 10, Foreground = InkMuted };
            Grid.SetColumn(packTag, 1);
            contentGrid.Children.Add(packTag);

            var cb = new CheckBox
            {
                Content = contentGrid,
                FontSize = 12,
                Foreground = Ink,
                Padding = new Thickness(6, 7),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            cb.IsCheckedChanged += (_, _) => UpdateQuantitySection();

            var wrapper = new Border
            {
                Background = SurfaceSub,
                BorderBrush = BorderLight,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(2, 1)
            };
            wrapper.Child = cb;
            productStack.Children.Add(wrapper);
            _products.Add((cb, spec, wrapper));
        }

        searchBox.TextChanged += (_, _) => FilterProducts(searchBox.Text ?? "");

        inner.Children.Add(new ScrollViewer
        {
            Content = productStack,
            Height = 178,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 8)
        });

        // Quantity section
        inner.Children.Add(SectionLabel("จำนวน"));
        _quantityRows = new StackPanel { Spacing = 5, Margin = new Thickness(0, 4, 0, 0) };
        inner.Children.Add(_quantityRows);

        var scroll = new ScrollViewer
        {
            Content = inner,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        rootGrid.Children.Add(scroll);

        // Start button
        var startBtn = new Button
        {
            Content = "Start",
            Classes = { "primary" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            FontSize = 14,
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(0, 11)
        };
        startBtn.Click += Calculate_Click;
        Grid.SetRow(startBtn, 1);
        rootGrid.Children.Add(startBtn);

        panel.Child = rootGrid;
        return panel;
    }

    // ── Component helpers ────────────────────────────────────────────────────

    private static Border Card(Thickness padding)
    {
        return new Border
        {
            Background = Surface,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = padding
        };
    }

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        Foreground = InkMuted,
        Margin = new Thickness(2, 4, 0, 0)
    };

    private static Button MakeChip(string label, bool active, Action<bool> onToggle)
    {
        bool[] on = { active };
        var btn = new Button
        {
            Content = label,
            FontSize = 10,
            Padding = new Thickness(7, 2),
            CornerRadius = new CornerRadius(5),
            Background = active ? AccentBg : SurfaceSub,
            BorderBrush = active ? AccentBorder : BorderLight,
            BorderThickness = new Thickness(1),
            Foreground = active ? AccentText : InkMuted,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        btn.Click += (_, _) =>
        {
            on[0] = !on[0];
            btn.Background  = on[0] ? AccentBg     : SurfaceSub;
            btn.BorderBrush = on[0] ? AccentBorder : BorderLight;
            btn.Foreground  = on[0] ? AccentText   : InkMuted;
            onToggle(on[0]);
        };
        return btn;
    }

    private Border BuildProductStatRow(
        ProductSpec spec, int productIndex, int packed, int requested,
        int fullStacks, int mixedPlaced = 0, int condoPlaced = 0)
    {
        int remaining = requested - packed;
        var color = IsometricCanvas.GetProductColor(productIndex);
        var colorBrush = new SolidColorBrush(color);

        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("14,*,Auto,Auto") };

        // Color dot
        grid.Children.Add(new Border
        {
            Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
            Background = colorBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });

        // Product name
        var nameText = new TextBlock
        {
            Text = $"{spec.Description} {spec.Content}",
            FontSize = 12, FontWeight = FontWeight.Medium,
            Foreground = Ink, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(nameText, 1);
        grid.Children.Add(nameText);

        // Stats badges
        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 0) };

        badges.Children.Add(new TextBlock
        {
            Text = $"{packed}/{requested} ลัง",
            FontSize = 11, Foreground = packed >= requested ? Ink : InkMuted,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (fullStacks > 0)
            badges.Children.Add(new TextBlock
            {
                Text = $"{fullStacks} ตั้ง", FontSize = 11, Foreground = InkMuted,
                VerticalAlignment = VerticalAlignment.Center
            });

        if (mixedPlaced > 0)
            badges.Children.Add(new TextBlock
            {
                Text = $"ผสม {mixedPlaced}", FontSize = 11, Foreground = InkMuted,
                VerticalAlignment = VerticalAlignment.Center
            });

        if (condoPlaced > 0)
            badges.Children.Add(new TextBlock
            {
                Text = $"คอนโด {condoPlaced}", FontSize = 11, Foreground = InkMuted,
                VerticalAlignment = VerticalAlignment.Center
            });

        if (remaining > 0)
            badges.Children.Add(new TextBlock
            {
                Text = $"เหลือ {remaining}",
                FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#EF4444")),
                VerticalAlignment = VerticalAlignment.Center
            });

        Grid.SetColumn(badges, 2);
        grid.Children.Add(badges);

        // Visibility toggle
        bool[] visible = [true];
        int capturedIndex = productIndex;
        var toggleBtn = new Button
        {
            Content = "แสดง", FontSize = 10,
            Padding = new Thickness(6, 2), CornerRadius = new CornerRadius(4),
            Background = AccentBg, BorderBrush = AccentBorder,
            BorderThickness = new Thickness(1), Foreground = AccentText,
            Cursor = new Cursor(StandardCursorType.Hand),
            Margin = new Thickness(4, 0, 0, 0)
        };
        toggleBtn.Click += (_, _) =>
        {
            visible[0] = !visible[0];
            if (visible[0])
            {
                _hiddenProducts.Remove(capturedIndex);
                toggleBtn.Background = AccentBg; toggleBtn.BorderBrush = AccentBorder;
                toggleBtn.Foreground = AccentText; toggleBtn.Content = "แสดง";
            }
            else
            {
                _hiddenProducts.Add(capturedIndex);
                toggleBtn.Background = SurfaceSub; toggleBtn.BorderBrush = BorderLight;
                toggleBtn.Foreground = InkMuted; toggleBtn.Content = "ซ่อน";
            }
            _canvas.SetHiddenProducts(new HashSet<int>(_hiddenProducts));
        };
        Grid.SetColumn(toggleBtn, 3);
        grid.Children.Add(toggleBtn);

        var outer = new StackPanel { Spacing = 2 };
        outer.Children.Add(grid);

        if (packed > 0)
        {
            int boxesPerStack;
            if (fullStacks > 0)
                boxesPerStack = packed / fullStacks;
            else
                boxesPerStack = packed;
            if (boxesPerStack > 0)
                outer.Children.Add(new TextBlock
                {
                    Text = $"CBM/Stack: {spec.Cbm * boxesPerStack:F4} m³",
                    FontSize = 10,
                    Foreground = InkMuted,
                    Margin = new Thickness(20, 0, 0, 0)
                });
        }

        return new Border { Child = outer, Padding = new Thickness(0, 2) };
    }

    private static Border MakeContainerItem(string name, string size, bool selected)
    {
        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };

        grid.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 13,
            FontWeight = FontWeight.Medium,
            Foreground = selected ? AccentText : Ink,
            VerticalAlignment = VerticalAlignment.Center
        });

        var tag = new Border
        {
            Background = selected ? AccentBg : SurfaceSub,
            BorderBrush = selected ? AccentBorder : BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2)
        };
        tag.Child = new TextBlock
        {
            Text = size,
            FontSize = 10,
            Foreground = selected ? AccentText : InkMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tag, 1);
        grid.Children.Add(tag);

        return new Border
        {
            Background = selected ? AccentBg : Surface,
            BorderBrush = selected ? AccentBorder : BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(11, 8),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = grid
        };
    }

    private void SelectContainer(int idx)
    {
        _selectedContainerIndex = idx;
        for (int i = 0; i < _containerItems.Count; i++)
            ApplyContainerSelection(_containerItems[i], i == idx);
        if (idx >= 0 && idx < ContainerSpec.All.Count)
            _canvas.SetData(ContainerSpec.All[idx], []);
    }

    private static void ApplyContainerSelection(Border item, bool selected)
    {
        item.Background  = selected ? AccentBg     : Surface;
        item.BorderBrush = selected ? AccentBorder : BorderLight;

        if (item.Child is not Grid g) return;
        if (g.Children[0] is TextBlock name)
            name.Foreground = selected ? AccentText : Ink;
        if (g.Children.Count > 1 && g.Children[1] is Border tag)
            ApplyTagSelection(tag, selected);
    }

    private static void ApplyTagSelection(Border tag, bool selected)
    {
        tag.Background  = selected ? AccentBg     : SurfaceSub;
        tag.BorderBrush = selected ? AccentBorder : BorderLight;
        if (tag.Child is TextBlock label)
            label.Foreground = selected ? AccentText : InkMuted;
    }

    private void FilterProducts(string query)
    {
        var q = query.Trim();
        foreach (var (_, spec, wrapper) in _products)
        {
            wrapper.IsVisible = q.Length == 0 ||
                $"{spec.Description} {spec.Content}".Contains(q, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void UpdateQuantitySection()
    {
        var toRemove = new List<ProductSpec>();
        foreach (var (spec, (row, _)) in _qtyMap)
        {
            var entry = _products.Find(p => p.Spec == spec);
            if (entry.Cb is null || entry.Cb.IsChecked != true)
            {
                _quantityRows.Children.Remove(row);
                toRemove.Add(spec);
            }
        }
        foreach (var spec in toRemove) _qtyMap.Remove(spec);

        foreach (var (cb, spec, _) in _products)
        {
            if (cb.IsChecked != true || _qtyMap.ContainsKey(spec)) continue;
            var row = BuildQtyRow(spec, out TextBox qtyBox);
            _quantityRows.Children.Add(row);
            _qtyMap[spec] = (row, qtyBox);
        }
    }

    private static Border BuildQtyRow(ProductSpec spec, out TextBox qtyBox)
    {
        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,8,62,8,Auto") };

        grid.Children.Add(new TextBlock
        {
            Text = $"{spec.Description} {spec.Content}",
            FontSize = 12,
            Foreground = Ink,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var qty = new TextBox
        {
            Text = "",
            Width = 62,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(qty, 2);
        grid.Children.Add(qty);

        var unit = new TextBlock
        {
            Text = "ลัง",
            FontSize = 12,
            Foreground = InkMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(unit, 4);
        grid.Children.Add(unit);

        qty.TextChanged += (_, _) =>
        {
            var clean = new string(qty.Text?.Where(char.IsDigit).ToArray() ?? []);
            if (qty.Text != clean) { qty.Text = clean; qty.CaretIndex = clean.Length; }
        };

        var border = new Border
        {
            Background = SurfaceSub,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 7)
        };
        border.Child = grid;
        qtyBox = qty;
        return border;
    }

    // ── Calculation ──────────────────────────────────────────────────────────

    private void Calculate_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_selectedContainerIndex < 0 || _selectedContainerIndex >= ContainerSpec.All.Count) return;
        var container = ContainerSpec.All[_selectedContainerIndex];
        var dims = new ContainerDims(container.InteriorW, container.InteriorL, container.InteriorH);

        var placements = new List<BoxPlacement>();
        _statsPanel.Children.Clear();
        _hiddenProducts.Clear();

        // Sort CBM ascending: smallest → door side, largest → back wall (adjacent to condo)
        var sortedSpecs = _products
            .Where(x => x.Cb.IsChecked == true && _qtyMap.ContainsKey(x.Spec))
            .OrderBy(x => x.Spec.Cbm)
            .Select(x => x.Spec)
            .ToList();

        if (sortedSpecs.Count == 0)
        {
            _statsPanel.Children.Add(new TextBlock
            {
                Text = "ยังไม่ได้เลือกสินค้า",
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = InkMuted, FontSize = 13
            });
            _cutSlider.Value = 1.0;
            _cutLabel.Text = "100%";
            _canvas.SetData(container, placements);
            return;
        }

        var packInfos      = RunPrimaryPacking(container, sortedSpecs, placements, out double currentY);
        RunBalancing(packInfos, dims, placements);
        var partialRemoved = RunPartialRemoval(packInfos, dims, placements, ref currentY);
        var (mixedMap, condoAreaStart) = RunMixedPlacement(packInfos, dims, partialRemoved, placements, ref currentY);
        var condoMap       = RunCondoPlacement(packInfos, dims, partialRemoved, mixedMap, condoAreaStart, placements);

        // Slide the entire pack to the back wall — preserves internal order, no flip
        if (placements.Count > 0)
        {
            double maxUsedY = placements.Max(p => p.Y + p.BL);
            double shift = dims.L - Clearance - maxUsedY;
            if (shift > 0.01)
                for (int i = 0; i < placements.Count; i++)
                {
                    var p = placements[i];
                    placements[i] = p with { Y = p.Y + shift };
                }
        }

        BuildPackStats(packInfos, container, placements, mixedMap, condoMap);

        _cutSlider.Value = 1.0;
        _cutLabel.Text = "100%";
        _canvas.SetData(container, placements);
    }

    private List<PackInfo> RunPrimaryPacking(
        ContainerSpec container, List<ProductSpec> specs,
        List<BoxPlacement> placements, out double currentY)
    {
        var packInfos = new List<PackInfo>();
        currentY = Clearance;

        for (int i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            if (!_qtyMap.TryGetValue(spec, out var entry)) continue;
            int requested = int.TryParse(entry.QtyBox.Text, out int parsed) && parsed > 0 ? parsed : 1;
            bool hasPattern = spec.PatternA is { Length: > 0 };
            double productStartY = currentY;

            PlaceResult r;
            if (hasPattern)
            {
                r = PlaceProduct(container, spec, requested, currentY, i, placements);
                currentY = r.EndY;
            }
            else
            {
                r = new PlaceResult(0, currentY, 0, 0);
            }
            packInfos.Add(new PackInfo(spec, i, requested, productStartY, r, hasPattern));
        }

        return packInfos;
    }

    private static void RunBalancing(
        List<PackInfo> packInfos, ContainerDims dims, List<BoxPlacement> placements)
    {
        foreach (var info in packInfos)
        {
            if (!info.HasPattern || info.Result.Packed <= 0) continue;
            double stackDepth = LayerDepth(info.Spec.PatternA!, info.Spec.W, info.Spec.L);
            int layerLimit = CalcLayerLimit(info.Spec, dims);
            BalanceAllStacks(placements, info.ProductIndex, info.Spec, dims,
                             info.StartY, stackDepth, layerLimit);
        }
    }

    private static Dictionary<int, int> RunPartialRemoval(
        List<PackInfo> packInfos, ContainerDims dims,
        List<BoxPlacement> placements, ref double currentY)
    {
        var removed = new Dictionary<int, int>();
        if (dims.L - Clearance - currentY >= 50.0) return removed;

        foreach (var info in packInfos)
        {
            if (!info.HasPattern) continue;
            var stacks = placements
                .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase)
                .GroupBy(p => p.StackIndex)
                .OrderBy(g => g.Key)
                .Select(g => (g.Key, Layers: g.Max(p => p.LayerIndex) + 1))
                .ToList();
            if (stacks.Count < 2 || stacks[^1].Layers >= stacks[^2].Layers - 1) continue;

            int maxSI = stacks[^1].Key;
            int n = placements.RemoveAll(p => p.ProductIndex == info.ProductIndex && p.StackIndex == maxSI);
            if (n > 0) removed[info.ProductIndex] = n;
        }

        currentY = placements.Count > 0 ? placements.Max(p => p.Y + p.BL) + 0.1 : Clearance;
        return removed;
    }

    private static (Dictionary<int, int> mixedMap, double condoAreaStart) RunMixedPlacement(
        List<PackInfo> packInfos, ContainerDims dims,
        Dictionary<int, int> partialRemoved,
        List<BoxPlacement> placements, ref double currentY)
    {
        var withRem = packInfos
            .Where(info => info.HasPattern)
            .Select(info => (info, Rem: info.Requested - (info.Result.Packed - partialRemoved.GetValueOrDefault(info.ProductIndex, 0))))
            .Where(x => x.Rem > 0)
            .OrderByDescending(x => x.info.Spec.Cbm)
            .ToList();

        double condoAreaStart = Math.Max(currentY, dims.L - Clearance - withRem.Sum(x => x.info.Spec.L));
        var mixedMap = new Dictionary<int, int>();

        if (condoAreaStart - currentY >= 50.0 && withRem.Count > 0)
        {
            int totalRem = withRem.Sum(x => x.Rem);
            double xOffset = 0;

            foreach (var (info, rem) in withRem)
            {
                double xSlice = (double)rem / totalRem * dims.W;
                int placed = PlaceMixedSlice(dims, info.Spec, rem, currentY,
                                             xOffset, xSlice, condoAreaStart,
                                             info.ProductIndex, MixedStackBase + info.ProductIndex * 10, placements);
                if (placed > 0) mixedMap[info.ProductIndex] = placed;
                xOffset += xSlice;
            }

            var mixedBoxes = placements.Where(p => p.StackIndex >= MixedStackBase).ToList();
            if (mixedBoxes.Count > 0) currentY = mixedBoxes.Max(p => p.Y + p.BL) + 0.1;
        }

        return (mixedMap, condoAreaStart);
    }

    private static Dictionary<int, int> RunCondoPlacement(
        List<PackInfo> packInfos, ContainerDims dims,
        Dictionary<int, int> partialRemoved, Dictionary<int, int> mixedMap,
        double condoAreaStart, List<BoxPlacement> placements)
    {
        double condoY = condoAreaStart;
        var condoMap = new Dictionary<int, int>();

        foreach (var info in packInfos)
        {
            if (!info.HasPattern) continue;
            int primaryPacked = info.Result.Packed - partialRemoved.GetValueOrDefault(info.ProductIndex, 0);
            int rem = info.Requested - primaryPacked - mixedMap.GetValueOrDefault(info.ProductIndex, 0);
            if (rem <= 0) continue;

            int placed = PlaceCondoStack(dims, info.Spec, rem, ref condoY,
                                         info.ProductIndex, CondoStackBase + info.ProductIndex, placements);
            if (placed > 0) condoMap[info.ProductIndex] = placed;
        }

        return condoMap;
    }

    private void BuildPackStats(
        List<PackInfo> packInfos, ContainerSpec container,
        List<BoxPlacement> placements,
        Dictionary<int, int> mixedMap, Dictionary<int, int> condoMap)
    {
        foreach (var info in packInfos)
        {
            if (!info.HasPattern)
            {
                _statsPanel.Children.Add(new TextBlock
                {
                    Text = $"{info.Spec.Description} {info.Spec.Content}: ยังไม่ได้กำหนด pattern",
                    FontSize = 12, Foreground = InkMuted
                });
                continue;
            }

            int totalPacked = placements.Count(p => p.ProductIndex == info.ProductIndex);
            int fullStacks  = placements
                .Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex < CondoStackBase)
                .Select(p => p.StackIndex).Distinct().Count();

            _statsPanel.Children.Add(BuildProductStatRow(
                info.Spec, info.ProductIndex, totalPacked, info.Requested,
                fullStacks, mixedMap.GetValueOrDefault(info.ProductIndex, 0),
                condoMap.GetValueOrDefault(info.ProductIndex, 0)));
        }

        double containerCbm = (double)container.InteriorW * container.InteriorL * container.InteriorH / 1_000_000;
        double usedCbm = placements.Sum(p => p.BW * p.BL * p.BH / 1_000_000);

        if (containerCbm > 0)
        {
            _statsPanel.Children.Add(new Border { Height = 1, Background = BorderLight, Margin = new Thickness(0, 3, 0, 1) });
            _statsPanel.Children.Add(new TextBlock
            {
                Text = $"รวม {usedCbm:F3} / {containerCbm:F3} m³  ({usedCbm / containerCbm * 100:F1}%)",
                FontSize = 12, Foreground = InkMuted
            });
        }
    }

    private record struct ContainerDims(double W, double L, double H);
    private record struct PlaceResult(int Packed, double EndY, int FullStacks, int PartialBoxes);
    private record struct PackInfo(ProductSpec Spec, int ProductIndex, int Requested, double StartY, PlaceResult Result, bool HasPattern);

    private const double Clearance      = 5.0;   // cm gap from each container wall
    private const int    CondoStackBase = 1000;   // StackIndex ≥ 1000 = condo placement
    private const int    MixedStackBase = 2000;   // StackIndex ≥ 2000 = mixed zone placement

    // Depth (Y) occupied by one layer of the given pattern
    private static double LayerDepth(LayerSection[] sections, double W, double L)
    {
        double max = 0;
        foreach (var s in sections)
        {
            double depth = 0;
            foreach (var sub in s.GetSubRows())
                depth += sub.Rows * (sub.Rotated ? W : L);
            max = Math.Max(max, depth);
        }
        return max;
    }

    private static PlaceResult PlaceProduct(
        ContainerSpec container, ProductSpec spec, int requested,
        double startY, int productIndex, List<BoxPlacement> placements)
    {
        if (spec.PatternA is not { Length: > 0 }) return new(0, startY, 0, 0);

        var dims      = new ContainerDims(container.InteriorW, container.InteriorL, container.InteriorH);
        double stackDepth = LayerDepth(spec.PatternA, spec.W, spec.L);
        if (stackDepth <= 0) return new(0, startY, 0, 0);

        int maxLayers  = spec.MaxLayers > 0 ? spec.MaxLayers : int.MaxValue;
        int maxHeight  = (int)Math.Floor(dims.H / spec.H);
        int layerLimit = Math.Min(maxLayers, maxHeight);

        int packed      = 0;
        int stackIndex  = 0;
        int fullStacks  = 0;
        int partialBoxes = 0;

        while (packed < requested)
        {
            double stackY = startY + stackIndex * stackDepth;
            if (stackY >= dims.L - Clearance) break;

            bool flipStart   = (stackIndex % 2 == 1) && spec.PatternB is { Length: > 0 };
            int  beforeStack = packed;
            int  layersPlaced = 0;

            for (int layer = 0; layer < layerLimit && packed < requested; layer++)
            {
                double z     = layer * spec.H;
                bool useA    = flipStart ? (layer % 2 == 1) : (layer % 2 == 0);
                var sections = useA ? spec.PatternA : (spec.PatternB ?? spec.PatternA);

                int n = PlaceLayerAt(sections, spec, dims, stackY, z, requested - packed, productIndex, placements, stackIndex, layer);
                if (n < 0) break;
                packed += n;
                layersPlaced++;
            }

            if (layerLimit > 0 && layersPlaced == layerLimit)
                fullStacks++;
            else if (layersPlaced > 0)
                partialBoxes = packed - beforeStack;

            stackIndex++;
        }

        // Remove partial last layer from the last stack: if the final placed layer has
        // fewer boxes than the preceding layer, the requested count ran out mid-layer.
        // Those boxes go back to remaining so Phases 4/5 can place them cleanly.
        if (stackIndex > 0)
        {
            int lastSI = stackIndex - 1;
            var lastGroups = placements
                .Where(p => p.ProductIndex == productIndex && p.StackIndex == lastSI)
                .GroupBy(p => p.LayerIndex)
                .OrderBy(g => g.Key)
                .ToList();
            if (lastGroups.Count >= 2)
            {
                int lastCount = lastGroups[^1].Count();
                int prevCount = lastGroups[^2].Count();
                if (lastCount < prevCount)
                {
                    int layerKey = lastGroups[^1].Key;
                    int removed = placements.RemoveAll(p =>
                        p.ProductIndex == productIndex &&
                        p.StackIndex == lastSI &&
                        p.LayerIndex == layerKey);
                    packed -= removed;
                }
            }
        }

        return new(packed, startY + stackIndex * stackDepth, fullStacks, partialBoxes);
    }

    private static int PlaceLayerAt(
        LayerSection[] sections, ProductSpec spec, ContainerDims dims,
        double stackY, double z, int limit, int productIndex, List<BoxPlacement> placements, int stackIndex, int layerIndex)
    {
        if (z + spec.H > dims.H + 0.01) return -1;

        static double SectionWidth(LayerSection s, double w, double l) =>
            s.GetSubRows().Max(sub => sub.Cols * (sub.Rotated ? l : w));

        double tierW = 0;
        foreach (var s in sections)
            tierW += SectionWidth(s, spec.W, spec.L);
        if (tierW <= 0) return -1;

        int numTiers = Math.Max(1, (int)Math.Floor(dims.W / tierW));
        int packed   = 0;

        for (int tier = 0; tier < numTiers && packed < limit; tier++)
        {
            double sectionX = tier * tierW;
            foreach (var section in sections)
            {
                double subY = stackY;
                foreach (var sub in section.GetSubRows())
                {
                    double bw = sub.Rotated ? spec.L : spec.W;
                    double bl = sub.Rotated ? spec.W : spec.L;

                    for (int c = 0; c < sub.Cols && packed < limit; c++)
                    {
                        for (int r = 0; r < sub.Rows && packed < limit; r++)
                        {
                            double px = sectionX + c * bw;
                            double py = subY + r * bl;
                            if (px + bw > dims.W + 0.01 || py + bl > dims.L - Clearance + 0.01) continue;
                            placements.Add(new BoxPlacement(px, py, z, bw, bl, spec.H, productIndex, sub.Rotated, stackIndex, layerIndex));
                            packed++;
                        }
                    }
                    subY += sub.Rows * bl;
                }
                sectionX += SectionWidth(section, spec.W, spec.L);
            }
        }

        return packed > 0 ? packed : -1;
    }

    // ── Effective layer limit for a product in a container ────────────────────
    private static int CalcLayerLimit(ProductSpec spec, ContainerDims dims) =>
        Math.Min(spec.MaxLayers > 0 ? spec.MaxLayers : int.MaxValue,
                 (int)Math.Floor(dims.H / spec.H));

    // ── Balance ALL stacks so no two differ by more than 1 layer ─────────────
    private static void BalanceAllStacks(
        List<BoxPlacement> placements, int productIndex,
        ProductSpec spec, ContainerDims dims,
        double startY, double stackDepth, int layerLimit)
    {
        if (spec.PatternA is not { Length: > 0 }) return;

        var stackIndices = placements
            .Where(p => p.ProductIndex == productIndex && p.StackIndex < CondoStackBase)
            .Select(p => p.StackIndex)
            .Distinct()
            .OrderBy(si => si)
            .ToList();

        if (stackIndices.Count < 2) return;

        var layerCounts = stackIndices.ToDictionary(si => si, si =>
            placements
                .Where(p => p.ProductIndex == productIndex && p.StackIndex == si)
                .Max(p => p.LayerIndex) + 1);

        if (layerCounts.Values.Max() - layerCounts.Values.Min() <= 1) return;

        // Redistribute evenly: each stack gets baseL or baseL+1 layers.
        // Last 'extra' stacks (innermost) get the extra layer.
        int totalLayers = layerCounts.Values.Sum();
        int numStacks   = stackIndices.Count;
        int baseL       = Math.Min(totalLayers / numStacks, layerLimit);
        int tallL       = Math.Min(baseL + 1, layerLimit);
        int extra       = totalLayers % numStacks;

        for (int j = 0; j < stackIndices.Count; j++)
        {
            int si     = stackIndices[j];
            int target = j < (numStacks - extra) ? baseL : tallL;
            int current = layerCounts[si];

            if (current > target)
            {
                placements.RemoveAll(p =>
                    p.ProductIndex == productIndex &&
                    p.StackIndex == si &&
                    p.LayerIndex >= target);
            }
            else if (current < target)
            {
                double stackY  = startY + si * stackDepth;
                bool   flipIt  = (si % 2 == 1) && spec.PatternB is { Length: > 0 };

                for (int layer = current; layer < target; layer++)
                {
                    double z     = layer * spec.H;
                    if (z + spec.H > dims.H + 0.01) break;
                    bool useA    = flipIt ? (layer % 2 == 1) : (layer % 2 == 0);
                    var sections = useA ? spec.PatternA! : (spec.PatternB ?? spec.PatternA)!;
                    PlaceLayerAt(sections, spec, dims, stackY, z, int.MaxValue,
                                 productIndex, placements, si, layer);
                }
            }
        }
    }

    // ── Mixed placement: each product gets a proportional X slice ─────────────
    // Boxes placed left-to-right in the slice, advancing Y as needed.
    private static int PlaceMixedSlice(
        ContainerDims dims, ProductSpec spec, int remaining,
        double startY, double xOffset, double xWidth,
        double maxY,
        int productIndex, int stackIndexBase, List<BoxPlacement> placements)
    {
        int colsInSlice = (int)Math.Floor(xWidth / spec.W);
        if (colsInSlice <= 0) return 0;

        int maxLayers = CalcLayerLimit(spec, dims);
        if (maxLayers <= 0) return 0;

        int placed      = 0;
        int stackOffset = 0;

        while (placed < remaining)
        {
            double stackY = startY + stackOffset * spec.L;
            if (stackY + spec.L > maxY + 0.01) break;

            for (int layer = 0; layer < maxLayers && placed < remaining; layer++)
            {
                double z = layer * spec.H;
                if (z + spec.H > dims.H + 0.01) break;

                for (int col = 0; col < colsInSlice && placed < remaining; col++)
                {
                    double px = xOffset + col * spec.W;
                    if (px + spec.W > dims.W + 0.01) break;
                    placements.Add(new BoxPlacement(
                        px, stackY, z, spec.W, spec.L, spec.H,
                        productIndex, false, stackIndexBase + stackOffset, layer));
                    placed++;
                }
            }
            stackOffset++;
        }

        return placed;
    }

    // ── Condo placement: 1-row-wide in Y, full width in X, stacked to layerLimit ─
    // No rotation alternation (all boxes same orientation, ไม่ interlock).
    private static int PlaceCondoStack(
        ContainerDims dims, ProductSpec spec, int remaining,
        ref double condoY, int productIndex, int condoStackIndex,
        List<BoxPlacement> placements)
    {
        if (remaining <= 0) return 0;

        int condoCount = spec.CondoCount > 0
            ? spec.CondoCount
            : (int)Math.Floor(dims.W / spec.W);
        int maxLayers = CalcLayerLimit(spec, dims);
        if (condoCount <= 0 || maxLayers <= 0 || condoY + spec.L > dims.L - Clearance + 0.01) return 0;

        int cols = Math.Min(condoCount, (int)Math.Floor(dims.W / spec.W));
        int placed = 0;
        for (int layer = 0; layer < maxLayers && placed < remaining; layer++)
        {
            for (int col = 0; col < cols && placed < remaining; col++)
            {
                placements.Add(new BoxPlacement(
                    col * spec.W, condoY, layer * spec.H, spec.W, spec.L, spec.H,
                    productIndex, false, condoStackIndex, layer));
                placed++;
            }
        }

        if (placed > 0) condoY += spec.L;
        return placed;
    }
}
