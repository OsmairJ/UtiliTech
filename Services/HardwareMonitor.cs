using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace UtiliTech.Services;

/// <summary>Uma linha da lista de sensores (atual / mínimo / máximo), atualizada a cada leitura.</summary>
public class SensorRow : Observable
{
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    internal ISensor Sensor { get; init; } = null!;

    string _value = "—", _min = "—", _max = "—", _color = Palette.Muted;
    public string Value { get => _value; set => SetField(ref _value, value); }
    public string Min { get => _min; set => SetField(ref _min, value); }
    public string Max { get => _max; set => SetField(ref _max, value); }
    public string ValueColor { get => _color; set => SetField(ref _color, value); }

    internal void Refresh()
    {
        Value = HardwareMonitor.Format(Sensor.SensorType, Sensor.Value);
        Min = HardwareMonitor.Format(Sensor.SensorType, Sensor.Min);
        Max = HardwareMonitor.Format(Sensor.SensorType, Sensor.Max);
        ValueColor = Sensor.SensorType == SensorType.Temperature ? HardwareMonitor.TempColor(Sensor.Value) : Palette.Text;
    }
}

public record HardwareGroup(string Name, List<SensorRow> Sensors);

/// <summary>Resumo para os cartões do topo.</summary>
public record MonitorSnapshot(
    string CpuName, float? CpuTemp, float? CpuLoad, float? CpuClock, float? CpuPower,
    string GpuName, float? GpuTemp, float? GpuLoad, float? GpuMemUsed, float? GpuMemTotal, float? GpuPower,
    float? RamLoad, float? RamUsed, float? RamAvailable,
    List<(string Name, float? Temp, float? Activity)> Disks);

/// <summary>
/// Leitura dos sensores com a LibreHardwareMonitorLib. Temperatura da CPU e da placa-mãe exigem o
/// driver PawnIO; GPU, RAM e discos funcionam sem ele.
/// </summary>
public sealed class HardwareMonitor : IDisposable
{
    // Ordem das linhas dentro de cada componente
    static readonly SensorType[] ShownTypes =
    {
        SensorType.Temperature, SensorType.Load, SensorType.Clock, SensorType.Power, SensorType.Fan,
        SensorType.Data, SensorType.SmallData, SensorType.Level,
    };

    readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true,
        IsStorageEnabled = true,
        IsBatteryEnabled = true,
    };
    readonly object _lock = new();

    // Reserva para o clock quando o sensor não informa (sem PawnIO/administrador): API de energia do Windows
    float? _powerApiMhz;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct ProcessorPowerInformation
    {
        public uint Number, MaxMhz, CurrentMhz, MhzLimit, MaxIdleState, CurrentIdleState;
    }

    [System.Runtime.InteropServices.DllImport("powrprof.dll")]
    static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputLength,
        [System.Runtime.InteropServices.Out] ProcessorPowerInformation[] output, uint outputLength);

    static float? PowerApiMhz()
    {
        const int ProcessorInformation = 11;
        var info = new ProcessorPowerInformation[Environment.ProcessorCount];
        uint size = (uint)(info.Length * System.Runtime.InteropServices.Marshal.SizeOf<ProcessorPowerInformation>());
        if (CallNtPowerInformation(ProcessorInformation, IntPtr.Zero, 0, info, size) != 0) return null;
        var mhz = info.Where(i => i.CurrentMhz > 0).Select(i => (float)i.CurrentMhz).ToList();
        return mhz.Count > 0 ? mhz.Average() : null;
    }

    public List<HardwareGroup> Groups { get; } = new();

    /// <summary>Abre os sensores (demora 1–3 s; chame fora da thread da tela).</summary>
    public void Open()
    {
        _computer.Open();
        // Duas leituras: alguns sensores (uso da CPU, atividade do disco) só têm valor a partir da segunda
        Update();
        Thread.Sleep(400);
        Update();
        foreach (var hw in _computer.Hardware)
        {
            if (IsVirtualMemory(hw)) continue;
            AddGroup(hw, GroupTitle(hw));
            foreach (var sub in hw.SubHardware) AddGroup(sub, "Placa-mãe · " + sub.Name);
        }
    }

    static bool IsVirtualMemory(IHardware hw) =>
        hw.HardwareType == HardwareType.Memory && hw.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase);

    static string GroupTitle(IHardware hw) => hw.HardwareType switch
    {
        HardwareType.Memory when hw.Name.Contains("Total", StringComparison.OrdinalIgnoreCase) => "Memória RAM",
        HardwareType.Motherboard => "Placa-mãe · " + hw.Name,
        HardwareType.Battery => "Bateria · " + hw.Name,
        _ => hw.Name
    };

    void AddGroup(IHardware hw, string title)
    {
        var rows = new List<SensorRow>();
        foreach (var s in hw.Sensors
                     // Sem leitura (ex.: sem driver) = some; consumo zerado também é falta de driver
                     .Where(s => ShownTypes.Contains(s.SensorType) && s.Value.HasValue && !(s.SensorType == SensorType.Power && s.Value <= 0))
                     .OrderBy(s => Array.IndexOf(ShownTypes, s.SensorType)).ThenBy(s => s.Index))
        {
            var label = FriendlyName(hw, s);
            if (label != null && rows.All(r => r.Name != label))
                rows.Add(new SensorRow { Name = label, Kind = KindName(s.SensorType), Sensor = s });
        }
        if (rows.Count > 0) Groups.Add(new HardwareGroup(title, rows));
    }

    /// <summary>
    /// Nome simples em português para os sensores que interessam ao técnico; null esconde o sensor
    /// (por núcleo/thread, tensões, distância ao TjMax, leituras/gravações acumuladas etc.).
    /// </summary>
    static string? FriendlyName(IHardware hw, ISensor s)
    {
        string n = s.Name;
        bool Is(string name) => n.Equals(name, StringComparison.OrdinalIgnoreCase);

        switch (s.SensorType)
        {
            case SensorType.Temperature:
                if (n.Contains("Distance", StringComparison.OrdinalIgnoreCase)) return null;
                if (Is("CPU Package") || Is("Core (Tctl/Tdie)") || Is("GPU Core") || Is("Temperature")) return "Temperatura";
                if (Is("Core Max")) return "Temperatura (núcleo mais quente)";
                if (Is("GPU Hot Spot")) return "Temperatura (ponto mais quente)";
                if (Is("GPU Memory Junction")) return "Temperatura da memória";
                if (hw.HardwareType is HardwareType.SuperIO or HardwareType.Motherboard)
                    return "Temperatura " + n.Replace("Temperature #", "", StringComparison.OrdinalIgnoreCase).Trim();
                return null;
            case SensorType.Load:
                if (Is("CPU Total") || Is("Memory") || Is("GPU Core")) return "Uso";
                if (Is("Total Activity")) return "Atividade";
                if (Is("Used Space")) return "Espaço usado";
                if (Is("GPU Memory")) return "Uso da VRAM";
                return null;
            case SensorType.Clock:
                if (Is("GPU Core")) return "Clock";
                if (Is("GPU Memory")) return "Clock da memória";
                if (n.StartsWith("CPU Core #1", StringComparison.OrdinalIgnoreCase) || Is("Core #1")) return "Clock";
                return null;
            case SensorType.Power:
                if (Is("CPU Package") || Is("GPU Package") || Is("GPU Power")) return "Consumo";
                return null;
            case SensorType.Fan:
                return "Ventoinha " + n.Replace("Fan #", "", StringComparison.OrdinalIgnoreCase).Trim();
            case SensorType.Data:
                if (Is("Memory Used")) return "Em uso";
                if (Is("Memory Available")) return "Livre";
                return null;
            case SensorType.SmallData:
                if (Is("GPU Memory Used")) return "VRAM em uso";
                if (Is("GPU Memory Total")) return "VRAM total";
                return null;
            case SensorType.Level:
                if (Is("Charge Level")) return "Carga";
                if (Is("Degradation Level")) return "Desgaste";
                return null;
            default:
                return null;
        }
    }

    public void Update()
    {
        lock (_lock)
        {
            foreach (var hw in _computer.Hardware)
            {
                hw.Update();
                foreach (var sub in hw.SubHardware) sub.Update();
            }
            try { _powerApiMhz = PowerApiMhz(); }
            catch { _powerApiMhz = null; }
        }
    }

    /// <summary>Copia os valores para as linhas (chamar na thread da tela).</summary>
    public void RefreshRows()
    {
        foreach (var g in Groups)
            foreach (var r in g.Sensors) r.Refresh();
    }

    public void ResetMinMax()
    {
        lock (_lock)
        {
            foreach (var g in Groups)
                foreach (var r in g.Sensors) { r.Sensor.ResetMin(); r.Sensor.ResetMax(); }
        }
    }

    public MonitorSnapshot Snapshot()
    {
        var hws = _computer.Hardware.ToList();

        var cpu = hws.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        float? cpuTemp = Value(cpu, SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Core Max", "Core Average")
                         ?? Max(cpu, SensorType.Temperature, s => !s.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase));
        float? cpuClock = Average(cpu, SensorType.Clock, s => s.Name.Contains("Core #", StringComparison.OrdinalIgnoreCase))
                          ?? _powerApiMhz;

        // Placa dedicada tem prioridade sobre a integrada
        var gpu = hws.FirstOrDefault(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd)
                  ?? hws.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel);
        float? gpuLoad = Value(gpu, SensorType.Load, "GPU Core", "D3D 3D") ?? Max(gpu, SensorType.Load, _ => true);

        // "Total Memory" é a RAM física; "Virtual Memory" inclui o arquivo de paginação
        var ram = hws.FirstOrDefault(h => h.HardwareType == HardwareType.Memory && !h.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                  ?? hws.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);

        var disks = hws.Where(h => h.HardwareType == HardwareType.Storage)
            .Select(d => (d.Name, Max(d, SensorType.Temperature, _ => true), Value(d, SensorType.Load, "Total Activity")))
            .ToList();

        return new MonitorSnapshot(
            cpu?.Name ?? "Processador", cpuTemp, Value(cpu, SensorType.Load, "CPU Total"), cpuClock,
            Value(cpu, SensorType.Power, "CPU Package") is > 0 and var cpuPower ? cpuPower : null, // 0 W = sem driver
            gpu?.Name ?? "Placa de vídeo", Value(gpu, SensorType.Temperature, "GPU Core") ?? Max(gpu, SensorType.Temperature, _ => true), gpuLoad,
            Value(gpu, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used"),
            Value(gpu, SensorType.SmallData, "GPU Memory Total"),
            Value(gpu, SensorType.Power, "GPU Package", "GPU Power"),
            Value(ram, SensorType.Load, "Memory"), Value(ram, SensorType.Data, "Memory Used"), Value(ram, SensorType.Data, "Memory Available"),
            disks);
    }

    static IEnumerable<ISensor> All(IHardware? hw) =>
        hw == null ? Enumerable.Empty<ISensor>() : hw.Sensors.Concat(hw.SubHardware.SelectMany(s => s.Sensors));

    static float? Value(IHardware? hw, SensorType type, params string[] names)
    {
        foreach (var n in names)
        {
            var s = All(hw).FirstOrDefault(x => x.SensorType == type && x.Name.Equals(n, StringComparison.OrdinalIgnoreCase) && x.Value.HasValue);
            if (s != null) return s.Value;
        }
        return null;
    }

    static float? Max(IHardware? hw, SensorType type, Func<ISensor, bool> filter)
    {
        var values = All(hw).Where(s => s.SensorType == type && s.Value is > 0 && filter(s)).Select(s => s.Value!.Value).ToList();
        return values.Count > 0 ? values.Max() : null;
    }

    static float? Average(IHardware? hw, SensorType type, Func<ISensor, bool> filter)
    {
        var values = All(hw).Where(s => s.SensorType == type && s.Value is > 0 && filter(s)).Select(s => s.Value!.Value).ToList();
        return values.Count > 0 ? values.Average() : null;
    }

    /// <summary>Driver usado para ler temperatura da CPU/placa-mãe (o WinRing0 antigo é bloqueado pelo Defender).</summary>
    public static bool PawnIoInstalled
    {
        get
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
            return k != null;
        }
    }

    public static string TempColor(float? t) => t switch
    {
        null => Palette.Muted,
        < 70 => Palette.Good,
        < 85 => Palette.Warn,
        _ => Palette.Bad
    };

    public static string Format(SensorType type, float? v)
    {
        if (v is not { } x || float.IsNaN(x)) return "—";
        return type switch
        {
            SensorType.Temperature => $"{x:0} °C",
            SensorType.Load or SensorType.Control or SensorType.Level => $"{x:0} %",
            SensorType.Clock => $"{x:0} MHz",
            SensorType.Power => $"{x:0.0} W",
            SensorType.Fan => $"{x:0} RPM",
            SensorType.Voltage => $"{x:0.000} V",
            SensorType.Data => $"{x:0.0} GB",
            SensorType.SmallData => $"{x:0} MB",
            SensorType.Throughput => $"{x / 1024 / 1024:0.0} MB/s",
            _ => $"{x:0.##}"
        };
    }

    static string KindName(SensorType t) => t switch
    {
        SensorType.Temperature => "Temperatura",
        SensorType.Load => "Uso",
        SensorType.Clock => "Clock",
        SensorType.Power => "Consumo",
        SensorType.Fan => "Ventoinha",
        SensorType.Control => "Ventoinha (%)",
        SensorType.Voltage => "Tensão",
        SensorType.Data or SensorType.SmallData => "Memória",
        SensorType.Throughput => "Transferência",
        SensorType.Level => "Nível",
        _ => t.ToString()
    };

    public void Dispose()
    {
        lock (_lock)
        {
            _computer.Close();
        }
    }
}
