using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

public partial class MemoryPage : UserControl
{
    bool _loaded;
    CancellationTokenSource? _cts;

    public MemoryPage()
    {
        InitializeComponent();
        UpdatePercentText();
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            RamItems.ItemsSource = await Task.Run(() => SystemInfo.MemoryItems());
        };
    }

    void PercentSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdatePercentText();

    void UpdatePercentText()
    {
        if (PercentText == null) return; // evento dispara durante o InitializeComponent
        var (_, available) = MemoryTester.GetStatus();
        PercentText.Text = $"{PercentSlider.Value:0}% da memória livre (≈ {Fmt.Bytes((long)(available * PercentSlider.Value / 100))})";
    }

    async void Start_Click(object sender, RoutedEventArgs e)
    {
        var (_, available) = MemoryTester.GetStatus();
        long bytes = (long)(available * PercentSlider.Value / 100);
        int passes = int.Parse(((ComboBoxItem)PassesBox.SelectedItem).Content.ToString()!);

        _cts = new CancellationTokenSource();
        StartBtn.IsEnabled = false;
        StopBtn.IsEnabled = true;
        PercentSlider.IsEnabled = PassesBox.IsEnabled = false;
        ResultText.Visibility = Visibility.Collapsed;
        LogBox.Clear();
        TestBar.Value = 0;
        TestBar.Foreground = Ui.Hex(Palette.Accent);
        SetErrors(0);

        var progress = new Progress<MemoryTestStatus>(s =>
        {
            TestBar.Value = s.Percent;
            StageText.Text = $"{s.Stage} · {s.Percent:0}%";
            SetErrors(s.Errors);
        });
        void Log(string msg) => Dispatcher.InvokeAsync(() =>
        {
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            LogBox.ScrollToEnd();
        });

        try
        {
            var r = await MemoryTester.RunAsync(bytes, passes, progress, Log, _cts.Token);
            ShowResult(r);
        }
        catch (Exception ex) { Ui.Error("Falha no teste: " + ex.Message); }
        finally
        {
            StartBtn.IsEnabled = true;
            StopBtn.IsEnabled = false;
            PercentSlider.IsEnabled = PassesBox.IsEnabled = true;
            _cts.Dispose();
            _cts = null;
            UpdatePercentText();
        }
    }

    void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    void SetErrors(long errors)
    {
        ErrorsText.Text = errors == 0 ? "0 erros" : $"{errors:N0} ERROS";
        ErrorsText.Foreground = Ui.Hex(errors == 0 ? Palette.Good : Palette.Bad);
    }

    void ShowResult(MemoryTestResult r)
    {
        string elapsed = $"{(int)r.Elapsed.TotalMinutes}min {r.Elapsed.Seconds}s";
        string summary;
        string color;
        if (r.Errors > 0)
        {
            summary = $"{r.Errors:N0} ERROS em {Fmt.Bytes(r.TestedBytes)} testados — memória com defeito provável. Teste os pentes um de cada vez para achar o ruim.";
            color = Palette.Bad;
        }
        else if (r.Cancelled)
        {
            summary = $"Teste interrompido após {r.Passes} passada(s) completa(s), sem erros até ali ({Fmt.Bytes(r.TestedBytes)}, {elapsed}).";
            color = Palette.Warn;
        }
        else
        {
            summary = $"Nenhum erro em {Fmt.Bytes(r.TestedBytes)} testados ({r.Passes} passada(s), {elapsed}).";
            color = Palette.Good;
        }
        ResultText.Text = summary;
        ResultText.Foreground = Ui.Hex(color);
        ResultText.Visibility = Visibility.Visible;
        TestBar.Foreground = Ui.Hex(color);
        StageText.Text = r.Cancelled ? "Interrompido" : "Concluído";
        AppState.LastMemoryTest = $"{DateTime.Now:dd/MM/yyyy HH:mm} — {summary}";
    }

    void Schedule_Click(object sender, RoutedEventArgs e)
    {
        try { MemoryTester.ScheduleWindowsDiagnostic(); }
        catch (Exception ex) { Ui.Error("Não foi possível abrir o mdsched: " + ex.Message); }
    }

    async void LastResult_Click(object sender, RoutedEventArgs e)
    {
        WinDiagText.Text = "Procurando no log do Windows...";
        try
        {
            var result = await Task.Run(() => MemoryTester.LastWindowsDiagnostic());
            WinDiagText.Text = result ?? "Nenhum resultado encontrado: o diagnóstico ainda não foi executado neste PC (ou o log foi limpo).";
        }
        catch (Exception ex) { WinDiagText.Text = "Erro ao ler o log: " + ex.Message; }
    }
}
