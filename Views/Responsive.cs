using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using UtiliTech.Services;

namespace UtiliTech.Views;

/// <summary>Telas que implementam isto respondem ao F5 (atualizar a tela aberta).</summary>
public interface IRefreshable
{
    Task RefreshAsync();
}

/// <summary>
/// Layout para telas pequenas: grade de cartões que diminui as colunas quando falta largura
/// (Responsive.MinItemWidth numa UniformGrid) e grade de colunas que empilha os cartões abaixo de uma largura
/// (Responsive.StackBelow num Grid com cartões nas colunas 0, 2, 4... e espaçadores entre elas).
/// </summary>
public static class Responsive
{
    // ---------- UniformGrid ----------

    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.RegisterAttached(
        "MinItemWidth", typeof(double), typeof(Responsive), new PropertyMetadata(0.0, OnMinItemWidth));

    public static void SetMinItemWidth(DependencyObject d, double v) => d.SetValue(MinItemWidthProperty, v);
    public static double GetMinItemWidth(DependencyObject d) => (double)d.GetValue(MinItemWidthProperty);

    static readonly DependencyProperty MaxColumnsProperty =
        DependencyProperty.RegisterAttached("MaxColumns", typeof(int), typeof(Responsive), new PropertyMetadata(0));

    static void OnMinItemWidth(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UniformGrid g) return;
        g.SizeChanged -= UniformSizeChanged;
        g.SizeChanged += UniformSizeChanged;
    }

    static void UniformSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var g = (UniformGrid)sender;
        int max = (int)g.GetValue(MaxColumnsProperty);
        if (max == 0) { max = Math.Max(1, g.Columns); g.SetValue(MaxColumnsProperty, max); }
        int cols = Shortcuts.ColumnsFor(e.NewSize.Width, GetMinItemWidth(g), max);
        if (g.Columns != cols) g.Columns = cols;
    }

    // ---------- Grid com cartões lado a lado ----------

    public static readonly DependencyProperty StackBelowProperty = DependencyProperty.RegisterAttached(
        "StackBelow", typeof(double), typeof(Responsive), new PropertyMetadata(0.0, OnStackBelow));

    public static void SetStackBelow(DependencyObject d, double v) => d.SetValue(StackBelowProperty, v);
    public static double GetStackBelow(DependencyObject d) => (double)d.GetValue(StackBelowProperty);

    /// <summary>Layout original guardado para voltar quando a janela crescer.</summary>
    sealed class Original
    {
        public GridLength[] Columns = Array.Empty<GridLength>();
        public Dictionary<UIElement, (int Column, Thickness Margin)> Children = new();
        public bool Stacked;
    }

    static readonly DependencyProperty OriginalProperty =
        DependencyProperty.RegisterAttached("Original", typeof(Original), typeof(Responsive));

    static void OnStackBelow(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Grid g) return;
        g.SizeChanged -= GridSizeChanged;
        g.SizeChanged += GridSizeChanged;
    }

    static void GridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var g = (Grid)sender;
        bool narrow = e.NewSize.Width < GetStackBelow(g);
        if (g.GetValue(OriginalProperty) is not Original o)
        {
            if (!narrow) return;
            o = new Original
            {
                Columns = g.ColumnDefinitions.Select(c => c.Width).ToArray(),
                Children = g.Children.OfType<FrameworkElement>().ToDictionary(c => (UIElement)c, c => (Grid.GetColumn(c), c.Margin)),
            };
            g.SetValue(OriginalProperty, o);
        }
        if (narrow == o.Stacked) return;
        o.Stacked = narrow;

        if (narrow)
        {
            // Uma coluna só; cada cartão vira uma linha, na ordem das colunas
            for (int i = 0; i < g.ColumnDefinitions.Count; i++) g.ColumnDefinitions[i].Width = i == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            var ordered = o.Children.OrderBy(c => c.Value.Column).Select(c => c.Key).ToList();
            g.RowDefinitions.Clear();
            for (int i = 0; i < ordered.Count; i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(ordered[i], i);
                Grid.SetColumn(ordered[i], 0);
                if (ordered[i] is FrameworkElement fe && i < ordered.Count - 1)
                    fe.Margin = new Thickness(o.Children[fe].Margin.Left, o.Children[fe].Margin.Top, o.Children[fe].Margin.Right, Math.Max(12, o.Children[fe].Margin.Bottom));
            }
        }
        else
        {
            for (int i = 0; i < g.ColumnDefinitions.Count && i < o.Columns.Length; i++) g.ColumnDefinitions[i].Width = o.Columns[i];
            g.RowDefinitions.Clear();
            foreach (var (child, (col, margin)) in o.Children)
            {
                Grid.SetRow(child, 0);
                Grid.SetColumn(child, col);
                if (child is FrameworkElement fe) fe.Margin = margin;
            }
        }
    }
}
