using System.Windows;
using System.Windows.Input;
using UtiliTech.Services;

namespace UtiliTech.Views;

/// <summary>O que pedir ao técnico antes de uma ação que mexe no PC.</summary>
public class ConfirmRequest
{
    public string Title { get; init; } = "";
    /// <summary>O que vai acontecer, um item por linha.</summary>
    public IReadOnlyList<string> Effects { get; init; } = Array.Empty<string>();
    /// <summary>Aviso em destaque (ex.: "Não dá para desfazer"); null = sem aviso.</summary>
    public string? Warning { get; init; }
    public string ConfirmText { get; init; } = "Confirmar";
    /// <summary>Botão vermelho para ações sem volta.</summary>
    public bool Danger { get; init; }
    /// <summary>Oferece criar ponto de restauração antes (descrição do ponto); null = não oferece.</summary>
    public string? RestorePoint { get; init; }
    public int RestorePointType { get; init; } = Services.RestorePoint.ModifySettings;
}

/// <summary>Confirmação no visual do app: lista o que vai acontecer, avisa se não tem volta e oferece o ponto de restauração.</summary>
public partial class ConfirmWindow : Window
{
    bool _confirmed;

    ConfirmWindow(ConfirmRequest r)
    {
        InitializeComponent();
        TitleText.Text = r.Title;
        IconText.Text = r.Danger ? "" : ""; // aviso / informação
        IconText.Foreground = Ui.Hex(r.Danger ? Palette.Warn : Palette.Accent);
        EffectList.ItemsSource = r.Effects;
        OkBtn.Content = r.ConfirmText;
        if (r.Danger) OkBtn.Style = (Style)FindResource("BtnDanger");
        if (r.Warning != null)
        {
            WarningText.Text = r.Warning;
            WarningText.Foreground = Ui.Hex(r.Danger ? Palette.Bad : Palette.Warn);
            WarningBox.BorderBrush = WarningText.Foreground;
            WarningBox.Visibility = Visibility.Visible;
        }
        if (r.RestorePoint != null)
        {
            RestoreBox.IsChecked = AppSettings.Current.CreateRestorePoints;
            RestoreBox.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Mostra a confirmação. Devolve (confirmou, criar ponto de restauração).</summary>
    public static (bool Ok, bool RestorePoint) Ask(ConfirmRequest r)
    {
        var w = new ConfirmWindow(r) { Owner = Application.Current.MainWindow };
        w.ShowDialog();
        return (w._confirmed, w._confirmed && r.RestorePoint != null && w.RestoreBox.IsChecked == true);
    }

    /// <summary>
    /// Confirma e, se marcado, cria o ponto de restauração antes. Devolve false se o técnico desistir.
    /// Se a Proteção do Sistema estiver desligada ou o ponto falhar, pergunta se liga/continua.
    /// </summary>
    public static async Task<bool> ConfirmAsync(ConfirmRequest r, Action<string>? log = null)
    {
        var (ok, wantPoint) = Ask(r);
        if (!ok) return false;
        if (!wantPoint) return true;
        return await RestorePointAsync(r.RestorePoint!, r.RestorePointType, log);
    }

    static async Task<bool> RestorePointAsync(string description, int type, Action<string>? log)
    {
        log?.Invoke("Criando ponto de restauração...");
        var result = await Task.Run(() => RestorePoint.Create(description, type));
        if (result.Status == RestorePointStatus.Disabled)
        {
            var (enable, _) = Ask(new ConfirmRequest
            {
                Title = "A Proteção do Sistema está desligada",
                Effects = new[]
                {
                    "Liga a Proteção do Sistema na unidade do Windows (o Windows reserva uma parte do disco para os pontos)",
                    "Cria o ponto de restauração e segue com a ação",
                },
                Warning = "Sem ela não dá para criar o ponto. Cancelar continua a ação sem ponto de restauração.",
                ConfirmText = "Ligar e criar",
            });
            if (enable)
            {
                log?.Invoke("Ligando a Proteção do Sistema...");
                if (await Task.Run(RestorePoint.Enable))
                    result = await Task.Run(() => RestorePoint.Create(description, type));
                else
                    result = new(RestorePointStatus.Failed, "Não foi possível ligar a Proteção do Sistema");
            }
            else
            {
                log?.Invoke("Seguindo sem ponto de restauração (Proteção do Sistema desligada)");
                return true;
            }
        }

        log?.Invoke(result.Message);
        if (result.Status is RestorePointStatus.Created or RestorePointStatus.Recent) return true;

        var (goOn, _) = Ask(new ConfirmRequest
        {
            Title = "Ponto de restauração não foi criado",
            Effects = new[] { result.Message },
            Warning = "Continuar mesmo assim, sem como voltar pelo ponto de restauração?",
            ConfirmText = "Continuar sem ponto",
            Danger = true,
        });
        return goOn;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        _confirmed = true;
        Close();
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    void Body_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
