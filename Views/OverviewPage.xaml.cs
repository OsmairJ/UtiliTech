using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UtiliTech.Services;

namespace UtiliTech.Views;

/// <summary>Linha da tabela de componentes.</summary>
public class ComponentRow : Observable
{
    public string Glyph { get; init; } = "";
    public string Name { get; init; } = "";
    public string Key { get; init; } = "";

    string _sub = "", _temp = "—", _load = "—", _clock = "—", _usage = "—", _tempColor = Palette.Text;
    public string Sub { get => _sub; set => SetField(ref _sub, value); }
    public string Temp { get => _temp; set => SetField(ref _temp, value); }
    public string TempColor { get => _tempColor; set => SetField(ref _tempColor, value); }
    public string Load { get => _load; set => SetField(ref _load, value); }
    public string Clock { get => _clock; set => SetField(ref _clock, value); }
    public string Usage { get => _usage; set => SetField(ref _usage, value); }
}

public record DotItem(string Label, string Value, string Color);
public record EventItem(string Title, string Detail, string Color);

public partial class OverviewPage : UserControl, IRefreshable
{
    /// <summary>F5: atualiza a tela.</summary>
    public Task RefreshAsync() => LoadIssuesAsync();

    static readonly string SystemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

    readonly ComponentRow _cpu = new() { Glyph = "\uE950", Name = "Processador", Key = "monitor" };
    readonly ComponentRow _gpu = new() { Glyph = "\uE7F4", Name = "Placa de vídeo", Key = "drivers" };
    readonly ComponentRow _ram = new() { Glyph = "\uE964", Name = "Memória RAM", Key = "memory" };
    readonly ComponentRow _disk = new() { Glyph = "\uEDA2", Name = $"Disco {SystemDrive.TrimEnd('\\')}", Key = "disks" };

    TimeSpan _span = TimeSpan.FromMinutes(5);
    string _metric = "temp";
    bool _started, _fastHeld;

    public OverviewPage()
    {
        InitializeComponent();
        ComponentList.ItemsSource = new[] { _cpu, _gpu, _ram, _disk };
        Span5.IsChecked = true;
        MetricTemp.IsChecked = true;

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible == _fastHeld) return;
            _fastHeld = IsVisible;
            if (IsVisible) { SensorHub.RequestFast(); RefreshEvents(); }
            else SensorHub.ReleaseFast();
        };
        Loaded += async (_, _) =>
        {
            if (_started) return;
            _started = true;
            SensorHub.Updated += s => Dispatcher.InvokeAsync(() => { if (IsVisible) Render(s); });
            _ = LoadSummaryAsync();
            _ = LoadHealthAsync();
            _ = LoadIssuesAsync();
            RefreshEvents();
            await SensorHub.StartAsync();
            if (SensorHub.Last is { } last) Render(last);
        };
    }

    // ---------- Gráfico e componentes (a cada leitura) ----------

    void Option_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } rb) return;
        if (rb.GroupName == "Span") _span = TimeSpan.FromMinutes(int.Parse(tag));
        else _metric = tag;
        if (SensorHub.Last is { } last && IsLoaded) Render(last);
    }

    void Render(MonitorSnapshot s)
    {
        RenderChart(s);

        _cpu.Sub = s.CpuName;
        _cpu.Temp = Temp(s.CpuTemp);
        _cpu.TempColor = s.CpuTemp.HasValue ? HardwareMonitor.TempColor(s.CpuTemp) : Palette.Text;
        _cpu.Load = Pct(s.CpuLoad);
        _cpu.Clock = s.CpuClock is { } c ? $"{c / 1000:0.00} GHz" : "—";
        _cpu.Usage = s.CpuPower is { } p ? $"{p:0} W" : "—";

        _gpu.Sub = s.GpuName;
        _gpu.Temp = Temp(s.GpuTemp);
        _gpu.TempColor = s.GpuTemp.HasValue ? HardwareMonitor.TempColor(s.GpuTemp) : Palette.Text;
        _gpu.Load = Pct(s.GpuLoad);
        _gpu.Usage = s.GpuPower is > 0 and { } gp ? $"{gp:0} W" : s.GpuMemUsed is { } vram ? $"{vram / 1024:0.0} GB VRAM" : "—";
        _ram.TempColor = Palette.Text; // a RAM não tem temperatura: só acompanha a cor do tema

        float? ramTotal = s.RamUsed + s.RamAvailable;
        _ram.Sub = ramTotal is { } rt ? $"{rt:0.0} GB utilizáveis" : "";
        _ram.Load = Pct(s.RamLoad);
        _ram.Usage = s.RamUsed is { } ru ? $"{ru:0.0} GB" : "—";
        RamBig.Text = s.RamUsed is { } used ? $"{used:0.0} GB" : "—";
        RamDetail.Text = ramTotal is { } total ? $"RAM em uso de {total:0.0} GB · {Pct(s.RamLoad)}" : "";
        RamBar.Value = s.RamLoad ?? 0;

        var hottestDisk = s.Disks.Where(d => d.Temp.HasValue).Select(d => d.Temp).DefaultIfEmpty(null).Max();
        _disk.Temp = Temp(hottestDisk);
        _disk.TempColor = hottestDisk.HasValue ? HardwareMonitor.TempColor(hottestDisk) : Palette.Text;
        _disk.Load = Pct(s.Disks.Select(d => d.Activity).DefaultIfEmpty(null).Max());
        try
        {
            var drive = new DriveInfo(SystemDrive);
            double usedPct = OverviewText.UsedPercent(drive.TotalSize, drive.TotalFreeSpace);
            _disk.Sub = s.Disks.FirstOrDefault().Name ?? "";
            _disk.Usage = $"{Fmt.Bytes(drive.TotalFreeSpace)} livres";
            DiskText.Text = $"Disco {SystemDrive.TrimEnd('\\')}";
            DiskDetail.Text = $"{Fmt.Bytes(drive.TotalFreeSpace)} livres de {Fmt.Bytes(drive.TotalSize)}";
            DiskPct.Text = $"{usedPct:0}% usado";
            DiskBar.Value = usedPct;
        }
        catch { }
    }

    void RenderChart(MonitorSnapshot s)
    {
        var samples = SensorHistory.Since(_span);
        (string caption, Func<MonitorSnapshot, float?> cpu, Func<MonitorSnapshot, float?> gpu, string unit) = _metric switch
        {
            "load" => ("CPU · CARGA", (Func<MonitorSnapshot, float?>)(x => x.CpuLoad), (Func<MonitorSnapshot, float?>)(x => x.GpuLoad), "%"),
            "power" => ("CPU · CONSUMO", x => x.CpuPower, x => x.GpuPower, " W"),
            _ => ("CPU · TEMPERATURA", x => x.CpuTemp, x => x.GpuTemp, "°C"),
        };
        ChartCaption.Text = caption;
        string Fmt1(float? v) => v is { } x ? $"{x:0} {unit.Trim()}" : "—";
        CpuBig.Text = Fmt1(cpu(s));
        GpuBig.Text = Fmt1(gpu(s));
        CpuSubtitle.Text = cpu(s).HasValue ? s.CpuName : "sem leitura da CPU (abra como administrador)";

        var points = samples.Select(x => (x.Time, A: (double?)cpu(x.Snapshot), B: (double?)gpu(x.Snapshot))).ToList();
        double peak = points.SelectMany(p => new[] { p.A ?? 0, p.B ?? 0 }).DefaultIfEmpty(0).Max();
        var (min, max) = OverviewText.ChartRange(_metric, peak);
        Chart.Plot(points, _span, min, max, unit.Trim());
    }

    static string Temp(float? t) => OverviewText.Temp(t);
    static string Pct(float? v) => OverviewText.Pct(v);

    void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ComponentRow row }) MainWindow.Navigate(row.Key);
    }

    void Report_Click(object sender, RoutedEventArgs e) => MainWindow.Navigate("system");

    // ---------- Resumo do PC (uma vez) ----------

    async Task LoadSummaryAsync()
    {
        var (model, serial, items, cpu, gpu) = await Task.Run(() =>
        {
            var cs = Wmi.Query("SELECT Manufacturer, Model FROM Win32_ComputerSystem").FirstOrDefault();
            var bios = Wmi.Query("SELECT SerialNumber FROM Win32_BIOS").FirstOrDefault();
            var os = Wmi.Query("SELECT Caption, InstallDate FROM Win32_OperatingSystem").FirstOrDefault();
            using var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string model = Environment.MachineName;
            if (cs != null)
            {
                model = OverviewText.DisplayModel(Wmi.Str(cs, "Manufacturer"), Wmi.Str(cs, "Model"), model);
            }
            string serial = bios == null ? "" : "Nº de série " + Wmi.Str(bios, "SerialNumber");
            var list = new List<InfoItem>
            {
                new("Computador", Environment.MachineName),
                new("Windows", (os == null ? "" : Wmi.Str(os, "Caption").Replace("Microsoft ", "")) + $" {cv?.GetValue("DisplayVersion")}"),
                new("Instalado em", os != null && Wmi.Date(os, "InstallDate") is { } d ? d.ToString("dd/MM/yyyy") : "—"),
                new("RAM instalada", Fmt.Bytes(SystemInfo.RamModules().Sum(m => m.Capacity))),
            };
            string cpuName = Wmi.Query("SELECT Name FROM Win32_Processor").Select(p => Wmi.Str(p, "Name")).FirstOrDefault() ?? "";
            string gpuName = GpuDrivers.Detect().Select(g => g.Name).FirstOrDefault() ?? "";
            return (model, serial, list, cpuName, gpuName);
        });
        ModelText.Text = model;
        SerialText.Text = serial;
        SummaryList.ItemsSource = items;
        CpuNameText.Text = OverviewText.ShortCpuName(cpu);
        GpuNameText.Text = gpu;
    }

    // ---------- Saúde do PC (uma vez, é mais demorado) ----------

    async Task LoadHealthAsync()
    {
        var items = await Task.Run(() =>
        {
            var list = new List<DotItem>();
            var disks = DiskHealth.Collect();
            if (disks.Count > 0)
            {
                var worst = disks.OrderBy(d => d.VerdictColor == Palette.Bad ? 0 : d.VerdictColor == Palette.Warn ? 1 : 2).First();
                list.Add(new DotItem(disks.Count == 1 ? "Disco" : $"Discos ({disks.Count})", worst.Verdict, worst.VerdictColor));
            }
            var battery = BatteryInfo.Collect();
            if (battery.Present)
                list.Add(new DotItem("Bateria", battery.HealthPercent is { } h ? $"{battery.Verdict} · {h:0}%" : battery.Verdict, battery.VerdictColor));
            int problems = DriverManager.ScanProblems().Count(p => !p.IsDisabled);
            list.Add(new DotItem("Drivers", problems == 0 ? "em dia" : $"{problems} com problema", problems == 0 ? Palette.Good : Palette.Warn));
            var gpu = GpuDrivers.Detect().FirstOrDefault();
            if (gpu != null && !gpu.HasVendorDriver) list.Add(new DotItem("Vídeo", "sem driver", Palette.Warn));
            list.Add(new DotItem("Sensores da CPU", SensorDriver.IsRunning ? "ok" : "sem driver", SensorDriver.IsRunning ? Palette.Good : Palette.Muted));
            return list;
        });
        HealthList.ItemsSource = items;
        HealthLoading.Visibility = Visibility.Collapsed;
    }

    // ---------- Problemas encontrados ----------

    bool _issuesBusy;

    async Task LoadIssuesAsync()
    {
        if (_issuesBusy) return;
        _issuesBusy = true;
        IssuesRefresh.IsEnabled = false;
        IssuesTitle.Text = "Procurando problemas...";
        IssuesIcon.Text = "";
        IssuesIcon.Foreground = Ui.Hex(Palette.Muted);
        try
        {
            var issues = await Task.Run(() => PcIssues.Build(PcIssues.Gather()));
            IssueList.ItemsSource = issues;
            int serious = issues.Count(i => i.Serious);
            if (issues.Count == 0)
            {
                IssuesTitle.Text = "Nenhum problema encontrado";
                IssuesIcon.Text = "";
                IssuesIcon.Foreground = Ui.Hex(Palette.Good);
            }
            else
            {
                IssuesTitle.Text = $"{issues.Count} problema(s) encontrado(s)" + (serious > 0 ? $" · {serious} grave(s)" : "");
                IssuesIcon.Text = serious > 0 ? "" : "";
                IssuesIcon.Foreground = Ui.Hex(serious > 0 ? Palette.Bad : Palette.Warn);
            }
            IssuesSubtitle.Text = $"Discos, espaço, drivers, bateria, segurança e eventos dos últimos {PcIssues.EventDays} dias · verificado às {DateTime.Now:HH:mm}";
        }
        catch (Exception ex)
        {
            Logger.Error("Problemas encontrados", ex);
            IssuesTitle.Text = "Não foi possível verificar";
            IssuesSubtitle.Text = ex.Message;
        }
        finally
        {
            _issuesBusy = false;
            IssuesRefresh.IsEnabled = true;
        }
    }

    async void IssuesRefresh_Click(object sender, RoutedEventArgs e) => await LoadIssuesAsync();

    void Issue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page } && page.Length > 0) MainWindow.Navigate(page);
    }

    // ---------- Eventos recentes ----------

    void RefreshEvents()
    {
        var events = new List<EventItem>();
        foreach (var a in AlertWatcher.History.Take(4))
            events.Add(new EventItem(a.Title, $"{a.TimeText} · {a.Message}", a.Color));
        if (AppState.LastStressTest is { } stress)
            events.Add(new EventItem("Teste de estresse", stress, stress.Contains("Estável") ? Palette.Good : Palette.Warn));
        if (AppState.LastMemoryTest is { } mem)
            events.Add(new EventItem("Teste de memória RAM", mem, mem.Contains("ERROS") ? Palette.Bad : Palette.Good));
        EventList.ItemsSource = events;
        EventsEmpty.Visibility = events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
