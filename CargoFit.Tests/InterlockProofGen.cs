using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CargoFit;
using Xunit;
using Xunit.Abstractions;

namespace CargoFit.Tests;

/// <summary>
/// Dev tool (not an assertion test): for every SKU in products.json it renders the HAND pattern
/// (left) next to the auto-designed pattern from InterlockDesigner (right), so the two can be compared
/// side by side. Writes interlock-proof.html at the repo root.
/// </summary>
public class InterlockProofGen(ITestOutputHelper log)
{
    const double Wc = InterlockDesigner.DefaultContainerW;

    [Fact]
    public void GenerateInterlockProof()
    {
        ProductSpec.Load();   // load the HAND patterns from products.json (repo root in dev)
        var specs = ProductSpec.All.Count > 0 ? ProductSpec.All : ProductSpec.Defaults;

        var sb = new StringBuilder();
        sb.Append("""
        <!doctype html><html lang="th"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Interlock — มือ vs Auto</title>
        <style>
          body{font-family:-apple-system,Segoe UI,Roboto,'Noto Sans Thai',sans-serif;margin:0;background:#f4f5f7;color:#1f2933;padding:24px;}
          h1{font-size:20px;margin:0 0 4px;} .sub{color:#6b7280;font-size:13px;margin:0 0 18px;}
          .scene{background:#fff;border:1px solid #e5e7eb;border-radius:12px;padding:14px 16px;margin-bottom:18px;box-shadow:0 1px 2px rgba(0,0,0,.04);}
          .scene h2{font-size:15px;margin:0 0 10px;}
          .cols{display:flex;gap:28px;flex-wrap:wrap;}
          .col{flex:1;min-width:300px;}
          .tag{font-size:12px;font-weight:700;letter-spacing:.02em;margin-bottom:2px;}
          .tag.hand{color:#6b7280;} .tag.auto{color:#1d4ed8;}
          .stat{font-size:12px;color:#4b5563;margin:0 0 8px;}
          .view b{font-size:11px;color:#9ca3af;display:block;margin:6px 0 3px;font-weight:600;}
          svg{background:#fafafa;border:1px solid #eee;border-radius:6px;}
          .badge{display:inline-block;font-size:11px;font-weight:700;padding:2px 8px;border-radius:999px;margin-left:8px;vertical-align:middle;}
          .b-up{background:#dcfce7;color:#15803d;} .b-eq{background:#e0e7ff;color:#3730a3;} .b-dn{background:#fef3c7;color:#92400e;} .b-new{background:#dbeafe;color:#1e40af;}
          .legend{display:flex;gap:16px;font-size:12px;color:#4b5563;margin:6px 0 18px;}
          .sw{width:13px;height:13px;border-radius:3px;display:inline-block;vertical-align:middle;margin-right:5px;}
        </style></head><body>
        <h1>Interlock Pattern — มือ (products.json) เทียบ Auto (Feature ใหม่)</h1>
        <p class="sub">ความกว้างตู้ 235 ซม. · ซ้าย = pattern ที่วาดมือ · ขวา = ที่โปรแกรมคำนวณเอง</p>
        <div class="legend"><span><i class="sw" style="background:#4c6ef5"></i>กล่องตั้ง</span>
        <span><i class="sw" style="background:#fab005"></i>กล่องหมุน 90°</span></div>
        """);

        foreach (var spec in specs)
        {
            var (hb, _, hd) = Measure(spec.PatternA, spec);
            double hu = hd > 0 ? hb * spec.W * spec.L / (Wc * hd) : 0;

            var r = InterlockDesigner.Generate(spec.W, spec.L, spec.H, Wc);

            string badge = hb == 0
                ? "<span class='badge b-new'>มือไม่มี → auto สร้างให้</span>"
                : r.AreaUtil >= hu - 0.005
                    ? $"<span class='badge b-up'>auto ≥ มือ ({r.AreaUtil * 100:F0}% vs {hu * 100:F0}%)</span>"
                    : $"<span class='badge b-dn'>มือดีกว่า ({hu * 100:F0}% vs {r.AreaUtil * 100:F0}%)</span>";

            sb.Append("<div class='scene'><h2>")
              .Append(Esc($"{spec.Description} {spec.Content}")).Append("  <span style='color:#9ca3af;font-weight:400'>")
              .Append(F(spec.W)).Append('×').Append(F(spec.L)).Append('×').Append(F(spec.H)).Append("</span>")
              .Append(badge).Append("</h2>");

            sb.Append("<div class='cols'>");

            // ── Hand ──
            sb.Append("<div class='col'><div class='tag hand'>มือ (products.json)</div>");
            if (hb == 0)
                sb.Append("<p class='stat'>— ไม่มี pattern —</p>");
            else
                sb.Append("<p class='stat'>").Append(hb).Append(" ลัง/ชั้น · ลึก ").Append(F(hd))
                  .Append(" ซม. · ใช้พื้นที่ ").Append(F(hu * 100)).Append("%</p>");
            sb.Append("<div class='view'><b>A</b>").Append(TopView(spec.PatternA, spec)).Append("</div>");
            if (spec.PatternB is { Length: > 0 })
                sb.Append("<div class='view'><b>B</b>").Append(TopView(spec.PatternB, spec)).Append("</div>");
            sb.Append("</div>");

            // ── Auto ──
            sb.Append("<div class='col'><div class='tag auto'>Auto (Feature ใหม่)</div>");
            sb.Append("<p class='stat'>[").Append(Esc(r.Template)).Append("] ").Append(r.BoxesPerLayer)
              .Append(" ลัง/ชั้น · ลึก ").Append(F(r.Depth)).Append(" ซม. · ใช้พื้นที่ ").Append(F(r.AreaUtil * 100)).Append("%</p>");
            sb.Append("<div class='view'><b>A</b>").Append(TopView(r.PatternA, spec)).Append("</div>");
            if (r.PatternB.Length > 0)
                sb.Append("<div class='view'><b>B (ขัด)</b>").Append(TopView(r.PatternB, spec)).Append("</div>");
            sb.Append("</div>");

            sb.Append("</div></div>");
        }

        sb.Append("</body></html>");

        var path = Path.Combine(RepoRoot(), "interlock-proof.html");
        File.WriteAllText(path, sb.ToString());
        log.WriteLine($"wrote {path}  ({specs.Count} SKUs)");
    }

    private static (int boxes, double usedW, double depth) Measure(LayerSection[]? sections, ProductSpec spec)
    {
        if (sections is not { Length: > 0 }) return (0, 0, 0);
        var bx = PackingEngine.LayerPlacements(sections, spec.W, spec.L, spec.H, Wc);
        if (bx.Count == 0) return (0, 0, 0);
        return (bx.Count, bx.Max(b => b.X + b.BW), bx.Max(b => b.Y + b.BL));
    }

    private static string TopView(LayerSection[]? sections, ProductSpec spec)
    {
        var boxes = sections is { Length: > 0 }
            ? PackingEngine.LayerPlacements(sections, spec.W, spec.L, spec.H, Wc)
            : new System.Collections.Generic.List<BoxPlacement>();
        const double s = 0.9; double pad = 6;
        double depth = boxes.Count > 0 ? boxes.Max(b => b.Y + b.BL) : spec.L;
        double w = Wc * s + pad * 2, h = depth * s + pad * 2;

        var sb = new StringBuilder();
        sb.Append("<svg viewBox='0 0 ").Append(F(w)).Append(' ').Append(F(h))
          .Append("' width='").Append(F(w)).Append("' height='").Append(F(h)).Append("'>");
        sb.Append(Rect(pad, pad, Wc * s, depth * s, "none", "#ced4da", 1));
        foreach (var b in boxes)
            sb.Append(Rect(pad + b.X * s, pad + b.Y * s, b.BW * s, b.BL * s,
                           b.Rotated ? "#fab005" : "#4c6ef5", "#fff", 0.8));
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string Rect(double x, double y, double w, double h, string fill, string stroke, double sw) =>
        $"<rect x='{F(x)}' y='{F(y)}' width='{F(w)}' height='{F(h)}' fill='{fill}' fill-opacity='0.85' stroke='{stroke}' stroke-width='{F(sw)}'/>";

    private static string F(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}
