using System.Windows;
using System.Windows.Input;
using UtiliTech.Services;

namespace UtiliTech.Views;

/// <summary>Janela "Novidades": o que mudou nas versões (depois de atualizar, ou pelo botão em Configurações).</summary>
public partial class NotesWindow : Window
{
    NotesWindow(string title, string subtitle, List<ReleaseSection> sections)
    {
        InitializeComponent();
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        SectionList.ItemsSource = sections;
    }

    /// <summary>Ao abrir o app: mostra as novidades se a versão mudou desde a última vez e grava a versão vista.</summary>
    public static void ShowIfUpdated(Window owner)
    {
        try
        {
            var all = ReleaseNotes.Parse(ReleaseNotes.Embedded());
            var (show, save) = ReleaseNotes.OnStartup(AppSettings.Current.LastSeenVersion, Updater.Current, all);
            if (AppSettings.Current.LastSeenVersion != save)
            {
                AppSettings.Current.LastSeenVersion = save;
                AppSettings.Current.Save();
            }
            if (show.Count == 0) return;
            new NotesWindow($"Novidades do UtiliTech {Updater.CurrentText}",
                show.Count == 1 ? "O que mudou nesta versão" : $"O que mudou nas últimas {show.Count} versões", show) { Owner = owner }.ShowDialog();
        }
        catch (Exception ex) { Logger.Error("Novidades da versão", ex); }
    }

    /// <summary>Botão "Ver novidades": as últimas versões, mesmo já vistas.</summary>
    public static void ShowRecent(Window owner, int count = 5)
    {
        var all = ReleaseNotes.Parse(ReleaseNotes.Embedded()).Where(s => s.Version <= Updater.Current).Take(count).ToList();
        if (all.Count == 0) { Ui.Error("As notas da versão não foram encontradas neste .exe."); return; }
        new NotesWindow("Novidades do UtiliTech", $"As últimas {all.Count} versões · você está na {Updater.CurrentText}", all) { Owner = owner }.ShowDialog();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Body_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
