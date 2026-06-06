using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CargoFit;
using Xunit;
using Xunit.Abstractions;

namespace CargoFit.Tests;

/// <summary>
/// Dev tool (not a real assertion test): renders REAL PackingEngine output to an HTML
/// elevation/plan view so the square-off + condo + scatter behaviour can be eyeballed.
/// Writes square-off-proof.html at the repo root.
/// </summary>
public class ProofGen(ITestOutputHelper log)
{
    // Always runs — regenerates square-off-proof.html on every `dotnet test` (no assertions; it's a
    // preview generator, not a pass/fail test).
    [Fact]
    public void GenerateProof()
    {
        var sb = new StringBuilder();
        sb.Append("""
        <!doctype html><html lang="th"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Square-off — real engine output</title>
        <style>
          body{font-family:-apple-system,Segoe UI,Roboto,'Noto Sans Thai',sans-serif;margin:0;background:#f4f5f7;color:#1f2933;padding:24px;}
          h1{font-size:20px;margin:0 0 16px;}
          .scene{background:#fff;border:1px solid #e5e7eb;border-radius:12px;padding:16px 18px;margin-bottom:22px;box-shadow:0 1px 2px rgba(0,0,0,.04);}
          .scene h2{font-size:16px;margin:0 0 2px;}
          .stat{font-size:13px;color:#6b7280;margin:0 0 12px;}
          table{border-collapse:collapse;font-size:12px;margin:8px 0 14px;}
          th,td{padding:3px 10px;text-align:right;border-bottom:1px solid #eee;}
          th:nth-child(2),td:nth-child(2){text-align:left;}
          .view{font-size:12px;color:#868e96;margin:10px 0 2px;font-weight:600;}
          svg{background:#fafafa;border:1px solid #eee;border-radius:6px;max-width:100%;height:auto;}
          .legend{display:flex;gap:16px;flex-wrap:wrap;font-size:12px;color:#4b5563;margin-top:6px;}
          .legend span{display:inline-flex;align-items:center;gap:6px;}
          .sw{width:13px;height:13px;border-radius:3px;display:inline-block;}
        </style></head><body>
        <h1>Square-off — ผลจริงจาก PackingEngine (condo ผนังเดียว + gate ≤1 step · เศษล้น/condo สั้น → scatter ขึ้น stack)</h1>
        """);

        // Every case from the SHARED list (TestHelpers.Cases()) — the SAME cases GlobalConditionTests
        // asserts (door ≤ 50 cm). Add a case there once → it appears here AND gets door-checked.
        foreach (var c in TestHelpers.Cases())
            Scene(sb, c.Label, c.Container, c.Requests);

        sb.Append("</body></html>");

        var path = Path.Combine(RepoRoot(), "square-off-proof.html");
        File.WriteAllText(path, sb.ToString());
        log.WriteLine($"wrote {path}");
    }

    private static readonly string[] Fills  = { "#4c6ef5", "#12b886", "#fab005", "#7048e8", "#f06595" };

    private void Scene(StringBuilder sb, string title, ContainerSpec container,
        params (ProductSpec Spec, int Qty)[] requests)
    {
        var output = PackingEngine.Calculate(container, requests);
        TestHelpers.DumpOutput(container, requests, output, log);

        // Height profile in Y order (innermost→door): zone heights + condo.
        log.WriteLine("  profile (Y order, innermost→door):");
        var zones = output.Placements
            .GroupBy(p => (p.ProductIndex, Condo: p.StackIndex >= PackingEngine.CondoStackBase))
            .Select(g => new
            {
                g.Key.ProductIndex,
                g.Key.Condo,
                MinY = g.Min(p => p.Y),
                MaxY = g.Max(p => p.Y + p.BL),
                TopZ = g.Max(p => p.Z + p.BH)
            })
            .OrderBy(z => z.MinY);
        foreach (var z in zones)
        {
            var s = output.PackInfos.First(i => i.ProductIndex == z.ProductIndex).Spec;
            log.WriteLine($"    Y[{z.MinY,6:F0}..{z.MaxY,6:F0}]  h={z.TopZ,6:F1}cm  {(z.Condo ? "CONDO " : "block ")}{s.Description} {s.Content}");
        }

        double L = container.InteriorL, H = container.InteriorH, W = container.InteriorW;
        double cbm = container.InteriorW * L * H / 1_000_000.0;
        double used = output.Placements.Sum(p => p.BW * p.BL * p.BH / 1_000_000.0);

        sb.Append("<div class='scene'><h2>").Append(Esc(title)).Append("</h2>");
        sb.Append("<p class='stat'>").Append(F(used)).Append('/').Append(F(cbm))
          .Append(" CBM (").Append(F(used / cbm * 100)).Append("%) · ")
          .Append(output.Placements.Count).Append(" boxes</p>");

        // per-product table
        sb.Append("<table><tr><th>#</th><th>Product</th><th>Req</th><th>Primary</th><th>Condo</th><th>Scatter</th><th>maxLayers(จริง)</th></tr>");
        foreach (var info in output.PackInfos)
        {
            int pri = output.Placements.Count(p => p.ProductIndex == info.ProductIndex && p.StackIndex < PackingEngine.CondoStackBase);
            int condo = output.CondoMap.GetValueOrDefault(info.ProductIndex, 0);
            int scat = output.ScatterMap.GetValueOrDefault(info.ProductIndex, 0);
            int maxLay = output.Placements.Where(p => p.ProductIndex == info.ProductIndex && p.StackIndex < PackingEngine.CondoStackBase)
                .Select(p => p.LayerIndex).DefaultIfEmpty(-1).Max() + 1;
            sb.Append("<tr><td>").Append(info.ProductIndex).Append("</td><td>")
              .Append(Esc($"{info.Spec.Description} {info.Spec.Content}")).Append("</td><td>")
              .Append(info.Requested).Append("</td><td>").Append(pri).Append("</td><td>")
              .Append(condo).Append("</td><td>").Append(scat).Append("</td><td>")
              .Append(maxLay).Append('/').Append(info.Spec.MaxLayers).Append("</td></tr>");
        }
        sb.Append("</table>");

        sb.Append("<div class='view'>มุมข้าง (Y ลึก: ในสุดซ้าย→ประตูขวา · Z สูง) — เส้นแดง = condo</div>");
        sb.Append(SideView(output, L, H));
        sb.Append("<div class='view'>มุมบน (X กว้าง · Y ลึก)</div>");
        sb.Append(TopView(output, L, W));

        sb.Append("<div class='view'>★ คอนโด — มุมประตู (X กว้าง · Z สูง · ซูม) — แถวล่าง=หนัก/CBM สูง</div>");
        sb.Append(CondoDoorView(output, W, H));

        sb.Append("<div class='legend'>");
        foreach (var info in output.PackInfos)
            sb.Append("<span><i class='sw' style='background:").Append(Fills[info.ProductIndex % Fills.Length])
              .Append("'></i>").Append(Esc($"{info.Spec.Description} {info.Spec.Content}")).Append("</span>");
        sb.Append("<span><i class='sw' style='background:#fff;border:2px solid #e03131'></i>condo</span></div>");
        sb.Append("</div>");
    }

    private static string SideView(PackingOutput o, double L, double H)
    {
        const double s = 0.62; double pad = 26;
        double w = L * s + pad * 2, h = H * s + pad * 2;
        var sb = new StringBuilder();
        sb.Append("<svg viewBox='0 0 ").Append(F(w)).Append(' ').Append(F(h)).Append("' width='").Append(F(w)).Append("'>");
        sb.Append(Rect(pad, pad, L * s, H * s, "none", "#ced4da", 1));
        foreach (var p in o.Placements.OrderBy(p => p.Z))
        {
            double x = pad + p.Y * s, y = pad + (H - p.Z - p.BH) * s;
            bool condo = p.StackIndex >= PackingEngine.CondoStackBase;
            sb.Append(Rect(x, y, p.BL * s, p.BH * s, Fills[p.ProductIndex % Fills.Length],
                condo ? "#e03131" : "rgba(0,0,0,.18)", condo ? 1.3 : 0.4));
        }
        sb.Append("<text x='").Append(F(pad)).Append("' y='").Append(F(h - 6)).Append("' font-size='11' fill='#868e96'>◀ ในสุด</text>");
        sb.Append("<text x='").Append(F(w - pad)).Append("' y='").Append(F(h - 6)).Append("' font-size='11' fill='#868e96' text-anchor='end'>ประตู ▶</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string TopView(PackingOutput o, double L, double W)
    {
        const double s = 0.62; double pad = 26;
        double w = L * s + pad * 2, h = W * s + pad * 2;
        var sb = new StringBuilder();
        sb.Append("<svg viewBox='0 0 ").Append(F(w)).Append(' ').Append(F(h)).Append("' width='").Append(F(w)).Append("'>");
        sb.Append(Rect(pad, pad, L * s, W * s, "none", "#ced4da", 1));
        foreach (var p in o.Placements.OrderBy(p => p.StackIndex >= PackingEngine.CondoStackBase ? 0 : 1))
        {
            double x = pad + p.Y * s, y = pad + p.X * s;
            bool condo = p.StackIndex >= PackingEngine.CondoStackBase;
            sb.Append(Rect(x, y, p.BL * s, p.BW * s, Fills[p.ProductIndex % Fills.Length],
                condo ? "#e03131" : "rgba(0,0,0,.18)", condo ? 1.3 : 0.4));
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    // Condo only, door view (X width × Z height), zoomed — the angle in the user's sketch.
    private static string CondoDoorView(PackingOutput o, double W, double H)
    {
        var condo = o.Placements.Where(p => p.StackIndex >= PackingEngine.CondoStackBase).ToList();
        if (condo.Count == 0) return "<p class='stat'>(เคสนี้ไม่มี condo)</p>";

        double maxZ = condo.Max(p => p.Z + p.BH);
        const double s = 1.5; double pad = 26;
        double w = W * s + pad * 2, h = maxZ * s + pad * 2;
        var sb = new StringBuilder();
        sb.Append("<svg viewBox='0 0 ").Append(F(w)).Append(' ').Append(F(h)).Append("' width='").Append(F(w)).Append("'>");
        sb.Append(Rect(pad, pad, W * s, maxZ * s, "none", "#ced4da", 1));
        foreach (var p in condo.OrderBy(p => p.Z).ThenBy(p => p.X))
        {
            double x = pad + p.X * s, y = pad + (maxZ - p.Z - p.BH) * s;
            sb.Append(Rect(x, y, p.BW * s, p.BH * s, Fills[p.ProductIndex % Fills.Length], "rgba(0,0,0,.4)", 0.7));
        }
        sb.Append("<text x='").Append(F(pad)).Append("' y='").Append(F(h - 6)).Append("' font-size='11' fill='#868e96'>พื้นตู้ (z=0)</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string Rect(double x, double y, double w, double h, string fill, string stroke, double sw) =>
        $"<rect x='{F(x)}' y='{F(y)}' width='{F(w)}' height='{F(h)}' fill='{fill}' fill-opacity='0.82' stroke='{stroke}' stroke-width='{F(sw)}'/>";

    private static string F(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}
