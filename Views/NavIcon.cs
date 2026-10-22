using System.Windows;

namespace UtiliTech.Views;

/// <summary>Ícone (glifo da fonte Segoe Fluent Icons) de um item do menu: v:NavIcon.Glyph="&#xE80F;".</summary>
public static class NavIcon
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.RegisterAttached("Glyph", typeof(string), typeof(NavIcon), new PropertyMetadata(""));

    public static string GetGlyph(DependencyObject d) => (string)d.GetValue(GlyphProperty);
    public static void SetGlyph(DependencyObject d, string value) => d.SetValue(GlyphProperty, value);
}
