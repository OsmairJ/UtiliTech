using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace UtiliTech.Views;

/// <summary>
/// Gráfico de linha no tempo com duas séries (principal contínua, secundária tracejada), linhas-guia
/// pontilhadas com o valor à esquerda e "-5 min … agora" embaixo. Cores seguem o tema.
/// </summary>
public class LineChart : FrameworkElement
{
    IReadOnlyList<(DateTime Time, double? A, double? B)> _points = Array.Empty<(DateTime, double?, double?)>();
    TimeSpan _span = TimeSpan.FromMinutes(5);
    double _min, _max = 100;
    string _unit = "";

    public void Plot(IReadOnlyList<(DateTime Time, double? A, double? B)> points, TimeSpan span, double min, double max, string unit)
    {
        _points = points;
        _span = span;
        _min = min;
        _max = Math.Max(max, min + 1);
        _unit = unit;
        InvalidateVisual();
    }

    Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 60 || h < 40) return;
        var text = Res("TextBrush", Brushes.White);
        var muted = Res("MutedBrush", Brushes.Gray);
        var line = Res("LineBrush", Brushes.DimGray);
        var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText Label(string s) => new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, 11, muted, dpi);

        const double left = 46, bottom = 20, top = 6;
        double plotW = w - left, plotH = h - bottom - top;
        double Y(double v) => top + plotH - (Math.Clamp(v, _min, _max) - _min) / (_max - _min) * plotH;
        var now = DateTime.Now;
        double X(DateTime t) => left + plotW * (1 - (now - t).TotalSeconds / _span.TotalSeconds);

        // Linhas-guia: máximo, meio e mínimo
        var guide = new Pen(line, 1) { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) };
        foreach (var v in new[] { _max, (_min + _max) / 2, _min })
        {
            double y = Y(v);
            dc.DrawLine(guide, new Point(left, y), new Point(w, y));
            var lbl = Label($"{v:0}{_unit}");
            dc.DrawText(lbl, new Point(0, y - lbl.Height / 2));
        }

        // Tempo embaixo
        var start = Label($"-{_span.TotalMinutes:0} min");
        var mid = Label($"-{_span.TotalMinutes / 2:0.#} min");
        var end = Label("agora");
        dc.DrawText(start, new Point(left, h - start.Height));
        dc.DrawText(mid, new Point(left + plotW / 2 - mid.Width / 2, h - mid.Height));
        dc.DrawText(end, new Point(w - end.Width, h - end.Height));

        dc.PushClip(new RectangleGeometry(new Rect(left, 0, plotW, h)));
        DrawSeries(dc, _points.Where(p => p.B.HasValue).Select(p => new Point(X(p.Time), Y(p.B!.Value))).ToList(),
            new Pen(muted, 1.4) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) });
        DrawSeries(dc, _points.Where(p => p.A.HasValue).Select(p => new Point(X(p.Time), Y(p.A!.Value))).ToList(),
            new Pen(text, 1.8) { LineJoin = PenLineJoin.Round });
        dc.Pop();
    }

    static void DrawSeries(DrawingContext dc, List<Point> pts, Pen pen)
    {
        if (pts.Count < 2) return;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(pts[0], false, false);
            ctx.PolyLineTo(pts.Skip(1).ToList(), true, true);
        }
        dc.DrawGeometry(null, pen, g);
    }
}
