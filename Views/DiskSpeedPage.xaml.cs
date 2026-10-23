using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

/// <summary>Teste de velocidade do disco (sequencial e 4K), com o veredito do tipo de disco.</summary>
public partial class DiskSpeedPage : UserControl
{
    CancellationTokenSource? _run;

    public DiskSpeedPage()
    {
        InitializeComponent();
        SizeBox.ItemsSource = DiskSpeed.Sizes;
        SizeBox.SelectedIndex = 1;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible || _run != null) return;
            var drives = DiskSpeed.Drives();
            string? chosen = (DriveBox.SelectedItem as SpeedDrive)?.Root;
            DriveBox.ItemsSource = drives;
            DriveBox.SelectedItem = drives.FirstOrDefault(d => d.Root == chosen)
                ?? drives.FirstOrDefault(d => d.Root.Equals(System.IO.Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase))
                ?? drives.FirstOrDefault();
        };
    }

    async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (DriveBox.SelectedItem is not SpeedDrive drive) { Ui.Error("Escolha a unidade."); return; }
        long size = (SizeBox.SelectedItem as DiskSpeed.TestSize ?? DiskSpeed.Sizes[1]).Bytes;
        if (!DiskSpeed.HasRoom(drive.Free, size))
        {
            Ui.Error($"Pouco espaço em {drive.Letter}: o teste de {Fmt.Bytes(size)} precisa de {Fmt.Bytes(size + (1L << 30))} livres. Escolha um tamanho menor.");
            return;
        }

        _run = new CancellationTokenSource();
        StartBtn.IsEnabled = DriveBox.IsEnabled = SizeBox.IsEnabled = false;
        StopBtn.Visibility = Visibility.Visible;
        VerdictText.Visibility = AdviceText.Visibility = Visibility.Collapsed;
        foreach (var t in new[] { SeqReadText, SeqWriteText, RandReadText, RandWriteText }) t.Text = "…";
        try
        {
            var progress = new Progress<string>(m => ProgressText.Text = m);
            var r = await DiskSpeed.RunAsync(drive, size, progress, _run.Token);
            SeqReadText.Text = $"{r.SeqRead:N0}";
            SeqWriteText.Text = $"{r.SeqWrite:N0}";
            RandReadText.Text = $"{r.RandRead:N1}";
            RandWriteText.Text = $"{r.RandWrite:N1}";
            RandReadIops.Text = $"MB/s · {r.RandReadIops:N0} IOPS";
            RandWriteIops.Text = $"MB/s · {r.RandWriteIops:N0} IOPS";

            var (label, color, advice) = DiskSpeed.Classify(r);
            VerdictText.Text = $"{drive.Letter} {label}";
            VerdictText.Foreground = Ui.Hex(color);
            VerdictText.Visibility = Visibility.Visible;
            AdviceText.Text = advice;
            AdviceText.Visibility = advice.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            ProgressText.Text = $"Teste de {Fmt.Bytes(size)} concluído às {DateTime.Now:HH:mm}; arquivo temporário apagado.";

            AppState.LastDiskSpeed = $"{DateTime.Now:dd/MM/yyyy HH:mm} — {r.Summary} ({label})";
            SessionLog.Add("Teste", $"velocidade do disco {r.Summary}");
        }
        catch (OperationCanceledException) { ProgressText.Text = "Teste interrompido; arquivo temporário apagado."; }
        catch (Exception ex)
        {
            Logger.Error("Velocidade do disco", ex);
            ProgressText.Text = "O teste falhou: " + ex.Message;
            foreach (var t in new[] { SeqReadText, SeqWriteText, RandReadText, RandWriteText }) t.Text = "—";
        }
        finally
        {
            _run.Dispose();
            _run = null;
            StartBtn.IsEnabled = DriveBox.IsEnabled = SizeBox.IsEnabled = true;
            StopBtn.Visibility = Visibility.Collapsed;
        }
    }

    void Stop_Click(object sender, RoutedEventArgs e) => _run?.Cancel();
}
