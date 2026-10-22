using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

public partial class StressPage : UserControl
{
    enum EndReason { Completed, Temperature, RamError, User }

    StressTest? _test;
    DateTime _startedAt;
    TimeSpan _duration;
    int _limit;
    float _maxTemp;
    float _baselineClock;
    int _lowClockSeconds;
    bool _throttled, _started, _fastHeld;

    public StressPage()
    {
        InitializeComponent();
        Application.Current.Exit += (_, _) => _test?.Stop();
        IsVisibleChanged += (_, _) => UpdateFastMode();
        Loaded += async (_, _) =>
        {
            if (_started) return;
            _started = true;
            await SensorHub.StartAsync();
            SensorHub.Updated += s => Dispatcher.InvokeAsync(() => OnSnapshot(s));
            if (SensorHub.Last is { } last) OnSnapshot(last);
            UpdateFastMode();
        };
    }

    // Leitura a cada 1 s enquanto a tela está aberta ou o teste está rodando
    void UpdateFastMode()
    {
        bool want = IsVisible || _test?.Running == true;
        if (want == _fastHeld) return;
        _fastHeld = want;
        if (want) SensorHub.RequestFast();
        else SensorHub.ReleaseFast();
    }

    void OnSnapshot(MonitorSnapshot s)
    {
        bool running = _test?.Running == true;
        bool hasTemp = s.CpuTemp.HasValue;

        if (!running)
        {
            NoTempBanner.Visibility = hasTemp ? Visibility.Collapsed : Visibility.Visible;
            StartBtn.IsEnabled = hasTemp;
            Status.Text = hasTemp ? $"Pronto · {s.CpuName}" : "Sem temperatura da CPU: o teste fica bloqueado";
        }

        TempText.Text = s.CpuTemp is { } t ? $"{t:0} °C" : "—";
        TempText.Foreground = Ui.Hex(HardwareMonitor.TempColor(s.CpuTemp));
        ClockText.Text = s.CpuClock is { } c ? $"{c / 1000:0.00} GHz" : "—";
        LoadText.Text = (s.CpuLoad is { } l ? $"{l:0}%" : "—") + (s.CpuPower is { } p ? $" · {p:0} W" : "");

        if (!running || s.CpuTemp is not { } temp) return;

        var elapsed = DateTime.Now - _startedAt;
        TempSpark.Push(temp);
        if (temp > _maxTemp) _maxTemp = temp;
        MaxText.Text = $"{_maxTemp:0} °C";
        MaxText.Foreground = Ui.Hex(HardwareMonitor.TempColor(_maxTemp));
        TimeText.Text = $"{elapsed:mm\\:ss} / {_duration:mm\\:ss}";
        TimeBar.Value = Math.Min(100, elapsed.TotalSeconds * 100 / _duration.TotalSeconds);
        Status.Text = $"Testando · {Environment.ProcessorCount} threads a 100%" + (_test!.RamBytes > 0 ? $" + {Fmt.Bytes(_test.RamBytes)} de RAM" : "");

        // Throttling: o clock cai bem abaixo do que a CPU fazia no começo do teste e fica lá
        if (s.CpuClock is { } clock)
        {
            if (elapsed.TotalSeconds is > 5 and < 30) _baselineClock = Math.Max(_baselineClock, clock);
            else if (elapsed.TotalSeconds >= 30 && _baselineClock > 0)
            {
                _lowClockSeconds = clock < _baselineClock * 0.75 ? _lowClockSeconds + 1 : 0;
                if (_lowClockSeconds >= 10 && !_throttled)
                {
                    _throttled = true;
                    Log($"Clock caiu de {_baselineClock / 1000:0.00} para {clock / 1000:0.00} GHz com {temp:0} °C: a CPU está se protegendo do calor (throttling).");
                }
            }
        }

        if (_test.RamErrors > 0) Finish(EndReason.RamError);
        else if (temp >= _limit) Finish(EndReason.Temperature);
        else if (elapsed >= _duration) Finish(EndReason.Completed);
    }

    void Start_Click(object sender, RoutedEventArgs e)
    {
        if (SensorHub.Last?.CpuTemp == null) return;
        _duration = TimeSpan.FromMinutes(int.Parse((string)((ComboBoxItem)DurationBox.SelectedItem).Tag));
        _limit = int.Parse((string)((ComboBoxItem)LimitBox.SelectedItem).Tag);
        _maxTemp = 0;
        _baselineClock = 0;
        _lowClockSeconds = 0;
        _throttled = false;

        TempSpark.Clear();
        TempSpark.LimitLine = _limit;
        TimeBar.Value = 0;
        ResultText.Visibility = Visibility.Collapsed;
        LogBox.Clear();
        SetRunning(true);

        _test = new StressTest();
        _startedAt = DateTime.Now;
        AlertWatcher.Suppressed = true; // o próprio teste cuida do limite
        _test.Start(RamBox.IsChecked == true);
        UpdateFastMode();
        Log($"Iniciado: {_duration.TotalMinutes:0} min, parar em {_limit} °C, temperatura inicial {SensorHub.Last?.CpuTemp:0} °C" +
            (RamBox.IsChecked == true ? ", com RAM." : "."));
    }

    void Stop_Click(object sender, RoutedEventArgs e) => Finish(EndReason.User);

    void Finish(EndReason reason)
    {
        if (_test == null) return;
        _test.Stop();
        long ramErrors = _test.RamErrors;
        _test = null;
        AlertWatcher.Suppressed = false;
        SetRunning(false);
        UpdateFastMode();

        var elapsed = DateTime.Now - _startedAt;
        string time = $"{(int)elapsed.TotalMinutes}min {elapsed.Seconds:00}s";
        string verdict;
        string color;
        switch (reason)
        {
            case EndReason.Temperature:
                verdict = $"PAROU EM {_limit} °C após {time}. O resfriamento não dá conta: limpe o cooler, confira a ventoinha e troque a pasta térmica.";
                color = Palette.Bad;
                break;
            case EndReason.RamError:
                verdict = $"ERRO DE MEMÓRIA ({ramErrors:N0} valores incorretos) após {time}. Teste os pentes de RAM um por vez.";
                color = Palette.Bad;
                break;
            case EndReason.User:
                verdict = $"Interrompido após {time}. Temperatura máxima: {_maxTemp:0} °C.";
                color = Palette.Warn;
                break;
            default:
                if (_maxTemp >= 85 || _throttled)
                {
                    verdict = $"Aguentou {time}, mas esquentou ({_maxTemp:0} °C)" + (_throttled ? " e reduziu o clock por calor" : "") + ". Vale limpar o cooler e trocar a pasta térmica.";
                    color = Palette.Warn;
                }
                else
                {
                    verdict = $"Estável: {time} a 100% com máxima de {_maxTemp:0} °C. Resfriamento ok.";
                    color = Palette.Good;
                }
                break;
        }
        if (_throttled && reason != EndReason.Completed) verdict += " Houve queda de clock por calor.";

        ResultText.Text = verdict;
        ResultText.Foreground = Ui.Hex(color);
        ResultText.Visibility = Visibility.Visible;
        TimeBar.Foreground = Ui.Hex(color);
        Log("Fim: " + verdict);
        AppState.LastStressTest = $"{DateTime.Now:dd/MM/yyyy HH:mm} — {verdict}";
    }

    void SetRunning(bool running)
    {
        StartBtn.IsEnabled = !running;
        StopBtn.IsEnabled = running;
        DurationBox.IsEnabled = LimitBox.IsEnabled = RamBox.IsEnabled = !running;
        if (running) TimeBar.Foreground = Ui.Hex(Palette.Accent);
    }

    void Log(string msg)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        LogBox.ScrollToEnd();
    }
}
