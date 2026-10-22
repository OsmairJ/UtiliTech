using System.Net;
using System.Text;

namespace UtiliTech.Services;

public record Atendimento(string Cliente, string OrdemServico, string Tecnico, string Observacoes);

/// <summary>Relatório HTML para entregar ao cliente (abre no navegador e imprime/salva em PDF).</summary>
public static class ReportBuilder
{
    // Mesmo desenho de Assets/utilitech-logo.svg e do LogoMark em Theme.xaml
    const string LogoSvg = """<svg viewBox="0 0 64 64" xmlns="http://www.w3.org/2000/svg"><defs><linearGradient id="ug" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#3B82F6"/><stop offset="1" stop-color="#7C3AED"/></linearGradient></defs><rect width="64" height="64" rx="15" fill="url(#ug)"/><path d="M15 0H49A15 15 0 0 1 64 15V22C44 30 20 30 0 22V15A15 15 0 0 1 15 0Z" fill="#fff" fill-opacity=".1"/><path d="M19.5 19V36A11 11 0 0 0 41.5 36V26" fill="none" stroke="#fff" stroke-width="7" stroke-linecap="round"/><circle cx="41.5" cy="18" r="5" fill="none" stroke="#fff" stroke-width="3.5"/></svg>""";

    public static string Html(Atendimento at, List<InfoSection> sections, List<DiskReport> disks, BatteryReport battery, string? memoryTest,
        SessionReport? session = null)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        var sb = new StringBuilder();
        sb.Append($$"""
            <!doctype html>
            <html lang="pt-BR"><head><meta charset="utf-8">
            <title>Relatório técnico — {{E(Environment.MachineName)}}</title>
            <style>
              body { font-family: Segoe UI, Arial, sans-serif; color: #1f2937; max-width: 900px; margin: 32px auto; padding: 0 20px; }
              h1 { margin: 0; font-size: 26px; }
              h2 { font-size: 16px; margin: 28px 0 8px; padding-bottom: 6px; border-bottom: 2px solid #3b82f6; }
              .sub { color: #6b7280; margin-top: 4px; }
              table { width: 100%; border-collapse: collapse; font-size: 13px; }
              td { padding: 5px 8px; border-bottom: 1px solid #e5e7eb; vertical-align: top; }
              td:first-child { width: 34%; color: #6b7280; }
              .badge { display: inline-block; padding: 2px 10px; border-radius: 10px; color: #fff; font-size: 12px; font-weight: 600; margin-left: 8px; }
              .disk { margin-top: 14px; }
              .disk h3 { font-size: 14px; margin: 0 0 6px; }
              .reasons { color: #374151; font-size: 13px; margin: 6px 0 0 0; }
              .obs { white-space: pre-wrap; font-size: 13px; }
              .ok { color: #16a34a; font-weight: 700; }
              .fail { color: #dc2626; font-weight: 700; }
              footer { margin-top: 40px; color: #9ca3af; font-size: 11px; text-align: center; }
              header { display: flex; align-items: center; gap: 14px; padding-bottom: 18px; border-bottom: 1px solid #e5e7eb; }
              header svg { width: 52px; height: 52px; flex: none; }
              .brand { margin-left: auto; text-align: right; font-weight: 700; font-size: 18px; }
              .brand span { color: #6d5cf0; }
              .brand small { display: block; font-weight: 400; font-size: 11px; color: #9ca3af; }
            </style></head><body>
            <header>
              {{LogoSvg}}
              <div>
                <h1>Relatório técnico</h1>
                <div class="sub">{{E(Environment.MachineName)}} · gerado em {{DateTime.Now:dd/MM/yyyy HH:mm}}</div>
              </div>
              <div class="brand">Utili<span>Tech</span><small>Ferramentas do técnico</small></div>
            </header>
            """);

        var atRows = new List<InfoItem> { new("Cliente", at.Cliente), new("Ordem de serviço", at.OrdemServico), new("Técnico", at.Tecnico) }
            .Where(i => !string.IsNullOrWhiteSpace(i.Value)).ToList();
        if (atRows.Count > 0)
        {
            sb.Append("<h2>Atendimento</h2>");
            Table(sb, atRows);
        }
        if (!string.IsNullOrWhiteSpace(at.Observacoes))
            sb.Append($"<h2>Serviço realizado / observações</h2><div class=\"obs\">{E(at.Observacoes)}</div>");

        if (session is { IsEmpty: false })
        {
            if (session.Actions.Count > 0)
            {
                sb.Append("<h2>Serviços feitos nesta visita</h2><table>");
                foreach (var a in session.Actions)
                    sb.Append($"<tr><td>{a.When:HH:mm} · {E(a.Category)}</td><td>" +
                              $"<span class=\"{(a.Success ? "ok" : "fail")}\">{(a.Success ? "✓" : "✗")}</span> {E(a.Text)}</td></tr>");
                sb.Append("</table>");
            }
            if (session.BeforeAfter.Count > 0)
            {
                sb.Append("<h2>Antes e depois</h2>");
                Table(sb, session.BeforeAfter);
            }
        }

        foreach (var s in sections)
        {
            sb.Append($"<h2>{E(s.Title)}</h2>");
            Table(sb, s.Items);
        }

        if (disks.Count > 0)
        {
            sb.Append("<h2>Saúde dos discos (S.M.A.R.T.)</h2>");
            foreach (var d in disks)
            {
                sb.Append($"<div class=\"disk\"><h3>{E(d.Name)}<span class=\"badge\" style=\"background:{d.VerdictColor}\">{E(d.Verdict)}</span></h3>");
                Table(sb, d.Items);
                sb.Append($"<div class=\"reasons\">{string.Join("<br>", d.Reasons.Select(r => "• " + E(r)))}</div></div>");
            }
        }

        if (battery.Present)
        {
            sb.Append($"<h2>Bateria<span class=\"badge\" style=\"background:{battery.VerdictColor}\">{E(battery.Verdict)}</span></h2>");
            Table(sb, battery.Items);
            sb.Append($"<div class=\"reasons\">{string.Join("<br>", battery.Reasons.Select(r => "• " + E(r)))}</div>");
        }

        if (!string.IsNullOrEmpty(memoryTest))
            sb.Append($"<h2>Testes realizados</h2><div class=\"obs\">{E(memoryTest)}</div>");

        sb.Append("<footer>Gerado pelo UtiliTech</footer></body></html>");
        return sb.ToString();
    }

    static void Table(StringBuilder sb, IEnumerable<InfoItem> rows)
    {
        sb.Append("<table>");
        foreach (var r in rows)
            sb.Append($"<tr><td>{WebUtility.HtmlEncode(r.Label)}</td><td>{WebUtility.HtmlEncode(r.Value)}</td></tr>");
        sb.Append("</table>");
    }
}
