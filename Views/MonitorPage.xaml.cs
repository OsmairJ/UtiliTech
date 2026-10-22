using System.IO;
using System.Windows;
using System.Windows.Controls;
using UtiliTech.Services;

namespace UtiliTech.Views;

public partial class MonitorPage : UserControl
{
    VolumeInfo? _systemDrive;
    int _ticks;
    bool _started, _paused, _fastHeld;

    public MonitorPage()
    {
        InitializeComponent();
        CpuSpark.SetColor("#3B82F6");
        GpuSpark.SetColor("#A78BFA");
        RamSpark.SetColor("#22D3EE");
        DiskSpark.SetColor("#F59E0B");

        // Leitura a cada 1 s só enquanto a tela está à vista
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible == _fastHeld) return;
            _fastHeld = IsVisible;
            if (IsVisible) SensorHub.RequestFast();
            else SensorHub.ReleaseFast();
        };
        Loaded += async (_, _) =>
        {
            if (_started) return;
            _started = true;
            SensorHub.Updated += s => Dispatcher.InvokeAsync(() =>
            {
                if (!_paused && IsVisible) Render(s);
            });
            await OpenAsync(SensorHub.StartAsync());
        };
    }

    async Task OpenAsync(Task<HardwareMonitor?> opening)
    {
        Status.Text = "Abrindo sensores...";
        var monitor = await opening;
        // O driver é carregado sozinho ao abrir; o aviso só aparece se isso falhou (ex.: sem administrador)
        DriverBanner.Visibility = SensorDriver.IsRunning ? Visibility.Collapsed : Visibility.Visible;
        DriverStateText.Text = "Driver de sensores: " + SensorDriver.State;
        if (monitor == null)
        {
            Status.Text = "Não foi possível abrir os sensores.";
            return;
        }
        GroupList.ItemsSource = monitor.Groups;
        if (SensorHub.Last is { } last) Render(last);
    }

    void Render(MonitorSnapshot s)
    {
        SensorHub.Monitor?.RefreshRows();
        // Temperatura da CPU chegando = driver funcionando: o aviso não faz sentido
        if (s.CpuTemp.HasValue) DriverBanner.Visibility = Visibility.Collapsed;

        CpuTitle.Text = s.CpuName.ToUpperInvariant();
        SetMain(CpuValue, s.CpuTemp, s.CpuLoad);
        CpuDetail.Text = Join(
            s.CpuClock is { } clock ? $"{clock / 1000:0.00} GHz" : null,
            $"Uso {Pct(s.CpuLoad)}",
            s.CpuPower is { } cpuW ? $"{cpuW:0} W" : null);
        CpuSpark.Push(s.CpuLoad);

        GpuTitle.Text = s.GpuName.ToUpperInvariant();
        SetMain(GpuValue, s.GpuTemp, s.GpuLoad);
        GpuDetail.Text = s.GpuLoad == null && s.GpuTemp == null ? "Sem leitura · falta driver?" : Join($"Uso {Pct(s.GpuLoad)}",
            s.GpuMemUsed is { } used ? (s.GpuMemTotal is { } total ? $"VRAM {used / 1024:0.0}/{total / 1024:0.0} GB" : $"VRAM {used / 1024:0.0} GB") : null,
            s.GpuPower is { } gpuW ? $"{gpuW:0} W" : null);
        GpuSpark.Push(s.GpuLoad);

        RamValue.Text = Pct(s.RamLoad);
        RamValue.Foreground = Ui.Hex(s.RamLoad is >= 90 ? Palette.Bad : s.RamLoad is >= 75 ? Palette.Warn : Palette.Text);
        RamDetail.Text = s.RamUsed is { } ramUsed && s.RamAvailable is { } ramFree
            ? $"{ramUsed:0.0} de {ramUsed + ramFree:0.0} GB em uso"
            : " ";
        RamSpark.Push(s.RamLoad);

        // Disco: temperatura do mais quente; embaixo, o espaço livre da unidade do Windows
        var hottest = s.Disks.Where(d => d.Temp.HasValue).Select(d => d.Temp).DefaultIfEmpty(null).Max();
        string freeText = _systemDrive is { } sys ? $"{Fmt.Bytes(sys.FreeBytes)} livres de {Fmt.Bytes(sys.TotalBytes)} em {SystemDriveLetter}:" : "Sem leitura";
        if (hottest != null)
        {
            SetMain(DiskValue, hottest, null);
            DiskDetail.Text = freeText;
        }
        else
        {
            // Sem sensor de temperatura: o destaque vira o espaço livre da unidade do Windows
            DiskValue.Text = _systemDrive != null ? Fmt.Bytes(_systemDrive.FreeBytes) : "—";
            DiskValue.Foreground = Ui.Hex(_systemDrive?.BarColor == Palette.Accent ? Palette.Text : _systemDrive?.BarColor ?? Palette.Muted);
            DiskDetail.Text = _systemDrive != null ? $"livres de {Fmt.Bytes(_systemDrive.TotalBytes)} em {SystemDriveLetter}:" : "Sem leitura";
        }
        DiskSpark.Push(s.Disks.Select(d => d.Activity).DefaultIfEmpty(null).Max());

        // Espaço das unidades muda devagar: relê a cada 10 leituras
        if (_ticks++ % 10 == 0) _ = RefreshStorageAsync();

        Status.Text = $"Atualizado às {DateTime.Now:HH:mm:ss} · leitura a cada 1 s";
    }

    static readonly string SystemDriveLetter = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\', ':') ?? "C";

    async Task RefreshStorageAsync()
    {
        var (physical, volumes) = await Task.Run(() =>
        {
            var disks = Wmi.Query("SELECT Model, Size, InterfaceType FROM Win32_DiskDrive")
                .Select(d => new InfoItem(Wmi.Str(d, "Model"), $"{Fmt.DiskSize(Wmi.Long(d, "Size"))} · {Wmi.Str(d, "InterfaceType")}"))
                .ToList();
            return (disks, DiskHealth.Volumes());
        });
        PhysicalList.ItemsSource = physical;
        VolumeList.ItemsSource = volumes;
        _systemDrive = volumes.FirstOrDefault(v => v.Name.StartsWith(SystemDriveLetter + ":", StringComparison.OrdinalIgnoreCase));
    }

    static void SetMain(TextBlock tb, float? temp, float? load)
    {
        if (temp is { } t)
        {
            tb.Text = $"{t:0} °C";
            tb.Foreground = Ui.Hex(HardwareMonitor.TempColor(t));
        }
        else
        {
            tb.Text = load is { } l ? $"{l:0} %" : "—";
            tb.Foreground = Ui.Hex(load.HasValue ? Palette.Text : Palette.Muted);
        }
    }

    static string Pct(float? v) => v is { } x ? $"{x:0}%" : "—";

    static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));

    void Pause_Click(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        PauseBtn.Content = _paused ? "Continuar" : "Pausar";
        if (_paused) Status.Text = "Pausado";
    }

    void Reset_Click(object sender, RoutedEventArgs e)
    {
        SensorHub.Monitor?.ResetMinMax();
        if (SensorHub.Last is { } last) Render(last);
    }

    async void InstallDriver_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Instalar o driver PawnIO pelo winget (namazso.PawnIO)?\n\nEle permite ler a temperatura da CPU e da placa-mãe.",
                "UtiliTech", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        DriverBtn.IsEnabled = false;
        DriverBtn.Content = "Instalando...";
        var r = await AppInstaller.InstallAsync("namazso.PawnIO");
        DriverBtn.IsEnabled = true;
        DriverBtn.Content = "Instalar driver PawnIO";
        if (!r.Success)
        {
            Ui.Error("O driver não foi instalado: " + r.Message);
            return;
        }

        // Reabre os sensores para já usar o driver instalado
        GroupList.ItemsSource = null;
        await OpenAsync(SensorHub.RestartAsync());
    }
}
