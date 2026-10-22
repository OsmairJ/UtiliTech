using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace UtiliTech.Services;

/// <summary>
/// Exporta o relatório como planilha: .xlsx (uma aba por assunto, abre no Excel/LibreOffice/Google Planilhas)
/// ou .csv (separado por ponto e vírgula, padrão do Excel em português). Sem bibliotecas externas.
/// </summary>
public static class SpreadsheetExporter
{
    enum RowStyle { Normal = 0, Header = 1, Bold = 2 }

    record Row(string[] Cells, RowStyle Style = RowStyle.Normal);
    record Sheet(string Name, List<Row> Rows);

    public static void Save(string path, Atendimento at, List<InfoSection> sections, List<DiskReport> disks,
        BatteryReport battery, string? memoryTest)
    {
        var sheets = BuildSheets(at, sections, disks, battery, memoryTest);
        if (Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase)) WriteCsv(path, sheets);
        else WriteXlsx(path, sheets);
    }

    static List<Sheet> BuildSheets(Atendimento at, List<InfoSection> sections, List<DiskReport> disks,
        BatteryReport battery, string? memoryTest)
    {
        var sheets = new List<Sheet>();

        var resumo = new List<Row>
        {
            new(new[] { "Relatório técnico", "" }, RowStyle.Header),
            new(new[] { "Computador", Environment.MachineName }),
            new(new[] { "Gerado em", DateTime.Now.ToString("dd/MM/yyyy HH:mm") }),
            new(new[] { "Cliente", at.Cliente }),
            new(new[] { "Ordem de serviço", at.OrdemServico }),
            new(new[] { "Técnico", at.Tecnico }),
            new(new[] { "Serviço realizado / observações", at.Observacoes }),
        };
        foreach (var d in disks) resumo.Add(new(new[] { $"Disco {d.Name}", d.Verdict }));
        if (battery.Present) resumo.Add(new(new[] { "Bateria", battery.Verdict + (battery.HealthPercent is { } h ? $" ({h:0}% de saúde)" : "") }));
        if (!string.IsNullOrEmpty(memoryTest))
            foreach (var line in memoryTest.Split('\n'))
            {
                int sep = line.IndexOf(": ", StringComparison.Ordinal);
                resumo.Add(sep > 0 ? new(new[] { "Teste · " + line[..sep], line[(sep + 2)..] }) : new(new[] { "Testes realizados", line }));
            }
        sheets.Add(new("Resumo", resumo));

        var sistema = new List<Row> { new(new[] { "Seção", "Item", "Valor" }, RowStyle.Header) };
        foreach (var s in sections)
            foreach (var i in s.Items)
                sistema.Add(new(new[] { s.Title, i.Label, i.Value }));
        sheets.Add(new("Sistema", sistema));

        if (disks.Count > 0)
        {
            // Uma linha por disco; colunas = união dos itens, na ordem em que aparecem
            var labels = disks.SelectMany(d => d.Items.Select(i => i.Label)).Distinct().ToList();
            var rows = new List<Row> { new(new[] { "Disco", "Veredito" }.Concat(labels).Append("Motivos").ToArray(), RowStyle.Header) };
            foreach (var d in disks)
            {
                var values = labels.Select(l => d.Items.FirstOrDefault(i => i.Label == l)?.Value ?? "");
                rows.Add(new(new[] { d.Name, d.Verdict }.Concat(values).Append(string.Join("; ", d.Reasons)).ToArray()));
            }
            sheets.Add(new("Discos", rows));
        }

        if (battery.Present)
        {
            var rows = new List<Row> { new(new[] { "Item", "Valor" }, RowStyle.Header), new(new[] { "Veredito", battery.Verdict }) };
            rows.AddRange(battery.Items.Select(i => new Row(new[] { i.Label, i.Value })));
            rows.Add(new(new[] { "Observações", string.Join("; ", battery.Reasons) }));
            sheets.Add(new("Bateria", rows));
        }

        return sheets;
    }

    // ---------- CSV ----------

    static void WriteCsv(string path, List<Sheet> sheets)
    {
        var sb = new StringBuilder();
        foreach (var sheet in sheets)
        {
            sb.AppendLine(Csv(sheet.Name.ToUpperInvariant()));
            foreach (var row in sheet.Rows) sb.AppendLine(string.Join(";", row.Cells.Select(Csv)));
            sb.AppendLine();
        }
        // BOM para o Excel reconhecer os acentos
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    static string Csv(string v) =>
        v.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;

    // ---------- XLSX (Open XML mínimo: workbook, estilos e uma planilha por aba) ----------

    const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    const string PkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    static void WriteXlsx(string path, List<Sheet> sheets)
    {
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        var types = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>""");
        for (int i = 1; i <= sheets.Count; i++)
            types.Append($"""<Override PartName="/xl/worksheets/sheet{i}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>""");
        types.Append("</Types>");
        Entry(zip, "[Content_Types].xml", types.ToString());

        Entry(zip, "_rels/.rels",
            $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="{PkgRelNs}"><Relationship Id="rId1" Type="{RelNs}/officeDocument" Target="xl/workbook.xml"/></Relationships>""");

        var wb = new StringBuilder($"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="{MainNs}" xmlns:r="{RelNs}"><sheets>""");
        var wbRels = new StringBuilder($"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="{PkgRelNs}">""");
        for (int i = 1; i <= sheets.Count; i++)
        {
            wb.Append($"""<sheet name="{Xml(sheets[i - 1].Name)}" sheetId="{i}" r:id="rId{i}"/>""");
            wbRels.Append($"""<Relationship Id="rId{i}" Type="{RelNs}/worksheet" Target="worksheets/sheet{i}.xml"/>""");
        }
        wb.Append("</sheets></workbook>");
        wbRels.Append($"""<Relationship Id="rId{sheets.Count + 1}" Type="{RelNs}/styles" Target="styles.xml"/></Relationships>""");
        Entry(zip, "xl/workbook.xml", wb.ToString());
        Entry(zip, "xl/_rels/workbook.xml.rels", wbRels.ToString());

        // Estilo 0 = normal, 1 = cabeçalho (negrito com fundo azul claro), 2 = negrito
        Entry(zip, "xl/styles.xml",
            $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="{MainNs}"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFD9E2F3"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>""");

        for (int i = 1; i <= sheets.Count; i++)
            Entry(zip, $"xl/worksheets/sheet{i}.xml", SheetXml(sheets[i - 1]));
    }

    static string SheetXml(Sheet sheet)
    {
        var sb = new StringBuilder($"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="{MainNs}">""");

        // Congela a linha de cabeçalho
        sb.Append("""<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>""");

        int cols = sheet.Rows.Max(r => r.Cells.Length);
        sb.Append("<cols>");
        for (int c = 0; c < cols; c++)
        {
            int len = sheet.Rows.Where(r => c < r.Cells.Length).Select(r => r.Cells[c].Length).DefaultIfEmpty(10).Max();
            sb.Append($"""<col min="{c + 1}" max="{c + 1}" width="{Math.Clamp(len + 2, 10, 80)}" customWidth="1"/>""");
        }
        sb.Append("</cols><sheetData>");

        for (int r = 0; r < sheet.Rows.Count; r++)
        {
            var row = sheet.Rows[r];
            sb.Append($"""<row r="{r + 1}">""");
            for (int c = 0; c < row.Cells.Length; c++)
            {
                string style = row.Style == RowStyle.Normal ? "" : $" s=\"{(int)row.Style}\"";
                sb.Append($"""<c r="{ColumnName(c)}{r + 1}" t="inlineStr"{style}><is><t xml:space="preserve">{Xml(row.Cells[c])}</t></is></c>""");
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    static string ColumnName(int index)
    {
        var name = "";
        for (int n = index + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
        return name;
    }

    // Escapa XML e remove caracteres de controle que o Excel não aceita
    static string Xml(string s) =>
        SecurityElement.Escape(new string(s.Where(ch => ch >= 0x20 || ch is '\t' or '\n' or '\r').ToArray())) ?? "";

    static void Entry(ZipArchive zip, string name, string content)
    {
        using var w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        w.Write(content);
    }
}
