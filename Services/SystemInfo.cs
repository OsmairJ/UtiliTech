using System.IO;
using System.Text;
using Microsoft.Win32;

namespace UtiliTech.Services;

public record RamModule(string Slot, long Capacity, string Type, long Speed, string Manufacturer, string PartNumber);

public static class SystemInfo
{
    public static List<InfoSection> Collect()
    {
        var sections = new List<InfoSection> { Computer(), Os(), Processor(), Memory(), Video(), Storage(), Network() };
        sections.RemoveAll(s => s.Items.Count == 0);
        return sections;
    }

    public static string ToText(IEnumerable<InfoSection> sections)
    {
        var sb = new StringBuilder();
        foreach (var s in sections)
        {
            sb.AppendLine($"== {s.Title} ==");
            foreach (var i in s.Items) sb.AppendLine($"{i.Label}: {i.Value}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    static InfoSection Section(string title, IEnumerable<InfoItem> items) =>
        new(title, items.Where(i => !string.IsNullOrWhiteSpace(i.Value)).ToList());

    static InfoSection Computer()
    {
        var items = new List<InfoItem>
        {
            new("Nome do computador", Environment.MachineName),
            new("Usuário", $@"{Environment.UserDomainName}\{Environment.UserName}"),
        };
        foreach (var cs in Wmi.Query("SELECT Manufacturer, Model, Domain FROM Win32_ComputerSystem").Take(1))
        {
            items.Add(new("Fabricante", Wmi.Str(cs, "Manufacturer")));
            items.Add(new("Modelo", Wmi.Str(cs, "Model")));
            items.Add(new("Domínio / grupo de trabalho", Wmi.Str(cs, "Domain")));
        }
        foreach (var b in Wmi.Query("SELECT SerialNumber, Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS").Take(1))
        {
            items.Add(new("Número de série", Wmi.Str(b, "SerialNumber")));
            var date = Wmi.Date(b, "ReleaseDate");
            items.Add(new("BIOS", $"{Wmi.Str(b, "Manufacturer")} {Wmi.Str(b, "SMBIOSBIOSVersion")}" + (date is { } d ? $" ({d:dd/MM/yyyy})" : "")));
        }
        foreach (var mb in Wmi.Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").Take(1))
            items.Add(new("Placa-mãe", $"{Wmi.Str(mb, "Manufacturer")} {Wmi.Str(mb, "Product")}".Trim()));
        return Section("Computador", items);
    }

    static InfoSection Os()
    {
        var items = new List<InfoItem>();
        foreach (var os in Wmi.Query("SELECT Caption, OSArchitecture, InstallDate, LastBootUpTime FROM Win32_OperatingSystem").Take(1))
        {
            items.Add(new("Sistema", Wmi.Str(os, "Caption")));
            using (var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                string display = cv?.GetValue("DisplayVersion")?.ToString() ?? "";
                string build = cv?.GetValue("CurrentBuild")?.ToString() ?? "";
                string ubr = cv?.GetValue("UBR")?.ToString() ?? "";
                items.Add(new("Versão", $"{display} (build {build}.{ubr})".Trim()));
            }
            items.Add(new("Arquitetura", Wmi.Str(os, "OSArchitecture")));
            if (Wmi.Date(os, "InstallDate") is { } inst)
                items.Add(new("Instalado em", inst.ToString("dd/MM/yyyy HH:mm")));
            if (Wmi.Date(os, "LastBootUpTime") is { } boot)
            {
                var up = DateTime.Now - boot;
                items.Add(new("Último boot", $"{boot:dd/MM/yyyy HH:mm} (ligado há {(int)up.TotalDays}d {up.Hours}h {up.Minutes}min)"));
            }
        }
        items.Add(new("Ativação do Windows", Activation()));
        foreach (var svc in Wmi.Query("SELECT OA3xOriginalProductKey FROM SoftwareLicensingService").Take(1))
            items.Add(new("Chave OEM gravada na BIOS", Wmi.Str(svc, "OA3xOriginalProductKey")));
        return Section("Sistema operacional", items);
    }

    static string Activation()
    {
        // ApplicationID do Windows; PartialProductKey filtra só a licença em uso
        var p = Wmi.Query("SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL").FirstOrDefault();
        if (p == null) return "Não foi possível verificar";
        return Wmi.Long(p, "LicenseStatus") switch
        {
            0 => "Não licenciado",
            1 => "Ativado",
            2 => "Período de carência (OOB)",
            3 => "Período de carência (OOT)",
            4 => "Carência — não genuíno",
            5 => "Notificação (não ativado)",
            6 => "Carência estendida",
            var x => $"Status {x}"
        };
    }

    static InfoSection Processor()
    {
        var items = new List<InfoItem>();
        foreach (var c in Wmi.Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, SocketDesignation FROM Win32_Processor"))
        {
            items.Add(new("Modelo", Wmi.Str(c, "Name")));
            items.Add(new("Núcleos / threads", $"{Wmi.Long(c, "NumberOfCores")} / {Wmi.Long(c, "NumberOfLogicalProcessors")}"));
            items.Add(new("Clock base", $"{Wmi.Long(c, "MaxClockSpeed") / 1000.0:0.00} GHz"));
            items.Add(new("Soquete", Wmi.Str(c, "SocketDesignation")));
        }
        return Section("Processador", items);
    }

    public static List<RamModule> RamModules()
    {
        var list = new List<RamModule>();
        foreach (var m in Wmi.Query("SELECT DeviceLocator, Capacity, SMBIOSMemoryType, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber FROM Win32_PhysicalMemory"))
        {
            long speed = Wmi.Long(m, "ConfiguredClockSpeed");
            if (speed == 0) speed = Wmi.Long(m, "Speed");
            list.Add(new RamModule(Wmi.Str(m, "DeviceLocator"), Wmi.Long(m, "Capacity"),
                MemoryType((int)Wmi.Long(m, "SMBIOSMemoryType")), speed,
                Wmi.Str(m, "Manufacturer"), Wmi.Str(m, "PartNumber")));
        }
        return list;
    }

    public static List<InfoItem> MemoryItems()
    {
        var mods = RamModules();
        long installed = mods.Sum(m => m.Capacity);
        long slots = Wmi.Query("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray").Sum(a => Wmi.Long(a, "MemoryDevices"));
        var items = new List<InfoItem>
        {
            new("Instalada", Fmt.Bytes(installed) + (mods.Count > 0 ? $" {mods[0].Type}" : "")),
            new("Utilizável pelo Windows", Fmt.Bytes(MemoryTester.GetStatus().Total)),
            new("Slots", slots > 0 ? $"{mods.Count} de {slots} ocupados" : $"{mods.Count} ocupados"),
        };
        foreach (var m in mods)
        {
            var parts = new[] { $"{Fmt.Bytes(m.Capacity)} {m.Type} {m.Speed} MHz", m.Manufacturer, m.PartNumber }.Where(p => p.Trim().Length > 0);
            items.Add(new(m.Slot.Length > 0 ? m.Slot : "Módulo", string.Join(" · ", parts)));
        }
        return items;
    }

    static InfoSection Memory() => Section("Memória RAM", MemoryItems());

    public static string MemoryType(int smbiosType) => smbiosType switch
    {
        20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 27 => "LPDDR", 28 => "LPDDR2",
        29 => "LPDDR3", 30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5", _ => ""
    };

    static InfoSection Video()
    {
        var items = new List<InfoItem>();
        foreach (var v in Wmi.Query("SELECT Name, DriverVersion, DriverDate, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController"))
        {
            long w = Wmi.Long(v, "CurrentHorizontalResolution");
            long hz = Wmi.Long(v, "CurrentRefreshRate");
            string res = w > 0 ? $" · {w}x{Wmi.Long(v, "CurrentVerticalResolution")}" + (hz > 1 ? $" @ {hz} Hz" : "") : "";
            var dd = Wmi.Date(v, "DriverDate");
            string name = Wmi.Str(v, "Name");
            // Adaptador genérico do Windows = driver do fabricante não instalado
            bool generic = name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Vídeo Básico", StringComparison.OrdinalIgnoreCase);
            items.Add(new(name, (generic ? "SEM DRIVER DO FABRICANTE · " : "") +
                $"Driver {Wmi.Str(v, "DriverVersion")}" + (dd is { } d ? $" ({d:dd/MM/yyyy})" : "") + res));
        }
        return Section("Vídeo", items);
    }

    static InfoSection Storage()
    {
        var items = new List<InfoItem>();
        foreach (var d in Wmi.Query("SELECT Model, Size, InterfaceType, SerialNumber FROM Win32_DiskDrive"))
            items.Add(new(Wmi.Str(d, "Model"), $"{Fmt.DiskSize(Wmi.Long(d, "Size"))} · {Wmi.Str(d, "InterfaceType")} · S/N {Wmi.Str(d, "SerialNumber")}"));
        foreach (var v in DriveInfo.GetDrives())
        {
            try
            {
                if (!v.IsReady) continue;
                items.Add(new($"Unidade {v.Name.TrimEnd('\\')}", $"{v.VolumeLabel} {v.DriveFormat} · {Fmt.Bytes(v.AvailableFreeSpace)} livres de {Fmt.Bytes(v.TotalSize)}".Trim()));
            }
            catch { }
        }
        return Section("Armazenamento", items);
    }

    static InfoSection Network()
    {
        var items = new List<InfoItem>();
        foreach (var n in Wmi.Query("SELECT Description, IPAddress, MACAddress, DefaultIPGateway, DNSServerSearchOrder, DHCPEnabled FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE"))
        {
            string ip = string.Join(", ", Wmi.Arr(n, "IPAddress").Where(a => a.Contains('.')));
            bool dhcp = Wmi.Get(n, "DHCPEnabled") is true;
            var parts = new List<string> { $"IP {ip} ({(dhcp ? "DHCP" : "fixo")})", $"MAC {Wmi.Str(n, "MACAddress")}" };
            var gw = Wmi.Arr(n, "DefaultIPGateway").Where(a => a.Contains('.')).ToArray();
            if (gw.Length > 0) parts.Add($"Gateway {string.Join(", ", gw)}");
            var dns = Wmi.Arr(n, "DNSServerSearchOrder");
            if (dns.Length > 0) parts.Add($"DNS {string.Join(", ", dns)}");
            items.Add(new(Wmi.Str(n, "Description"), string.Join(" · ", parts)));
        }
        return Section("Rede", items);
    }
}
