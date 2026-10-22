using System.Windows;
using System.Windows.Media;

namespace UtiliTech.Views;

/// <summary>Gráfico de linha minimalista dos últimos N valores (escala fixa 0–100: % ou °C).</summary>
public class Sparkline : FrameworkElement
{
    readonly Queue<double> _values = new();

    public int Capacity { get; set; } = 60;
    public double Maximum { get; set; } = 100;
    public Brush Stroke { get; set; } = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));

    public void Push(double? value)
    {
        _values.Enqueue(Math.Clamp(value ?? 0, 0, Maximum));
        while (_values.Count > Capacity) _values.Dequeue();
        InvalidateVisual();
    }

    public void Clear()
    {
        _values.Clear();
        InvalidateVisual();
    }

    /// <summary>Linha tracejada vermelha num valor (ex.: limite de temperatura do teste de estresse).</summary>
    public double? LimitLine { get; set; }

    public void SetColor(string hex)
    {
        Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Linhas-guia em 50% e na base
        var guide = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0x8B, 0x93, 0xA1)), 1) { DashStyle = DashStyles.Dot };
        dc.DrawLine(guide, new Point(0, h / 2), new Point(w, h / 2));
        dc.DrawLine(guide, new Point(0, h - 0.5), new Point(w, h - 0.5));
        if (LimitLine is { } limit)
        {
            double y = h - Math.Clamp(limit, 0, Maximum) / Maximum * (h - 2) - 1;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0xEF, 0x44, 0x44)), 1) { DashStyle = DashStyles.Dash }, new Point(0, y), new Point(w, y));
        }
        if (_values.Count < 2) return;

        var pts = _values.Select((v, i) => new Point(
            w - (_values.Count - 1 - i) * (w / (Capacity - 1)),
            h - v / Maximum * (h - 2) - 1)).ToList();

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(pts[0], false, false);
            ctx.PolyLineTo(pts.Skip(1).ToList(), true, true);
        }
        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(pts[0].X, h), true, true);
            ctx.PolyLineTo(pts.Append(new Point(pts[^1].X, h)).ToList(), false, true);
        }

        var color = (Stroke as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
        var fill = new LinearGradientBrush(Color.FromArgb(0x55, color.R, color.G, color.B), Color.FromArgb(0x00, color.R, color.G, color.B), 90);
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round }, line);
    }
}
