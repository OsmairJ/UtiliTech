using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace UtiliTech.Services;

/// <summary>
/// Driver PawnIO "de passagem", como o AIDA64/HWiNFO: se o PC não tem o PawnIO funcionando, o UtiliTech
/// instala o driver embutido no .exe (dispositivo virtual Root\PawnIO, igual ao instalador oficial) e,
/// ao fechar, remove o dispositivo, o pacote do driver, o serviço e o registro — o PC fica como estava.
/// Se o PawnIO já está instalado e funcionando, usa o que existe e não mexe em nada.
/// </summary>
public static class SensorDriver
{
    const string HardwareId = @"Root\PawnIO";
    const string ServiceName = "PawnIO";
    const string DevicePath = @"\\?\GLOBALROOT\Device\PawnIO";
    // A LibreHardwareMonitorLib só usa o PawnIO se achar esta chave do instalador oficial com DisplayVersion
    const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
    const string DriverVersion = "2.2.0.0";
    const string OurDisplayName = "PawnIO (temporário do UtiliTech)";
    // Marca do que foi o UtiliTech que instalou (sobrevive a um travamento, para limpar na próxima vez)
    const string MarkerKey = @"SOFTWARE\UtiliTech";
    const string MarkerValue = "PawnIOTemporario";

    static readonly object Gate = new();
    static readonly string WorkDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp", "UtiliTech", "PawnIO");

    /// <summary>True quando foi o UtiliTech que instalou o driver (e portanto vai removê-lo ao fechar).</summary>
    public static bool LoadedByUs { get; private set; }

    /// <summary>Resumo para a tela.</summary>
    public static string State { get; private set; } = "não verificado";

    /// <summary>
    /// Resultado do último carregamento. Não reabre o driver: com os sensores abertos, uma segunda
    /// abertura pode falhar mesmo com tudo funcionando.
    /// </summary>
    public static bool IsRunning { get; private set; }

    /// <summary>Garante o driver funcionando. Nunca lança exceção: sem ele os outros sensores funcionam do mesmo jeito.</summary>
    public static bool EnsureLoaded()
    {
        lock (Gate)
        {
            try
            {
                LoadedByUs = Marker; // sobra de uma execução que não fechou direito: é nossa, remove no fim
                if (DeviceResponds())
                {
                    bool registryOurs = EnsureRegistration();
                    if (registryOurs && !LoadedByUs) Logger.Info("PawnIO funcionando sem a chave do instalador; criada temporariamente");
                    State = LoadedByUs ? "carregado pelo UtiliTech (sai do PC ao fechar)" : "já instalado no PC";
                    Logger.Info("Driver PawnIO: " + State);
                    IsRunning = true;
                    return true;
                }

                Marker = true;
                LoadedByUs = true;
                Install();
                EnsureRegistration();
                if (!WaitForDevice()) throw new InvalidOperationException("o driver foi instalado mas não respondeu");
                State = "carregado pelo UtiliTech (sai do PC ao fechar)";
                Logger.Info("Driver PawnIO instalado temporariamente");
                IsRunning = true;
                return true;
            }
            catch (Exception ex)
            {
                IsRunning = false;
                State = "não foi possível carregar: " + ex.Message;
                Logger.Error("Carregar driver PawnIO", ex);
                Unload(); // não deixa nada pela metade
                return false;
            }
        }
    }

    /// <summary>Remove tudo que o UtiliTech instalou. Chamar ao fechar o app, depois de fechar os sensores.</summary>
    public static void Unload()
    {
        lock (Gate)
        {
            RemoveRegistration();
            if (!LoadedByUs) return;

            Step("remover o dispositivo", RemoveDevices);
            Step("remover o pacote do driver", RemoveDriverPackage);
            Step("remover o serviço", DeleteServiceIfPresent);
            try { if (Directory.Exists(WorkDir)) Directory.Delete(WorkDir, true); }
            catch { } // arquivo ainda em uso: a pasta temporária do Windows é limpa depois
            Marker = false;
            LoadedByUs = false;
            IsRunning = false;
            Logger.Info("Driver PawnIO removido do PC");
        }
    }

    static void Step(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { Logger.Error($"PawnIO: {what}", ex); }
    }

    // ---------- Instalação (dispositivo virtual + driver) ----------

    static void Install()
    {
        string inf = ExtractFiles();

        var classGuid = Guid.Empty;
        var className = new char[256];
        if (!SetupDiGetINFClass(inf, out classGuid, className, className.Length, out _)) throw new Win32Exception();

        var set = SetupDiCreateDeviceInfoList(ref classGuid, IntPtr.Zero);
        if (set == InvalidHandle) throw new Win32Exception();
        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            if (!SetupDiCreateDeviceInfo(set, new string(className).TrimEnd('\0'), ref classGuid, null, IntPtr.Zero, DicdGenerateId, ref data))
                throw new Win32Exception();
            var ids = Encoding.Unicode.GetBytes(HardwareId + "\0\0");
            if (!SetupDiSetDeviceRegistryProperty(set, ref data, SpdrpHardwareId, ids, ids.Length)) throw new Win32Exception();
            if (!SetupDiCallClassInstaller(DifRegisterDevice, set, ref data)) throw new Win32Exception();
        }
        finally { SetupDiDestroyDeviceInfoList(set); }

        // Instala o driver (assinado pela Microsoft) no dispositivo recém-criado, sem janelas
        if (!UpdateDriverForPlugAndPlayDevices(IntPtr.Zero, HardwareId, inf, InstallFlagForce | InstallFlagNonInteractive, out _))
            throw new Win32Exception();
    }

    static string ExtractFiles()
    {
        Directory.CreateDirectory(WorkDir);
        foreach (var name in new[] { "PawnIO.inf", "PawnIO.cat", "PawnIO.sys" })
        {
            using var res = typeof(SensorDriver).Assembly.GetManifestResourceStream("UtiliTech.Drivers." + name)
                            ?? throw new InvalidOperationException($"{name} não está embutido no app");
            using var file = File.Create(Path.Combine(WorkDir, name));
            res.CopyTo(file);
        }
        return Path.Combine(WorkDir, "PawnIO.inf");
    }

    static bool WaitForDevice()
    {
        for (int i = 0; i < 20; i++)
        {
            if (DeviceResponds()) return true;
            Thread.Sleep(250);
        }
        return false;
    }

    // ---------- Remoção ----------

    static void RemoveDevices()
    {
        var set = SetupDiGetClassDevs(IntPtr.Zero, "ROOT", IntPtr.Zero, DigcfAllClasses);
        if (set == InvalidHandle) throw new Win32Exception();
        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            var buffer = new byte[2048];
            for (int i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                if (!SetupDiGetDeviceRegistryProperty(set, ref data, SpdrpHardwareId, out _, buffer, buffer.Length, out int size)) continue;
                var ids = Encoding.Unicode.GetString(buffer, 0, size).Split('\0');
                if (!ids.Any(id => id.Equals(HardwareId, StringComparison.OrdinalIgnoreCase))) continue;
                if (!DiUninstallDevice(IntPtr.Zero, set, ref data, 0, out _)) throw new Win32Exception();
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    // O pacote fica no DriverStore como oemN.inf; o serviço diz qual é ("Owners")
    static void RemoveDriverPackage()
    {
        using var svc = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ServiceName}");
        if (svc?.GetValue("Owners") is not string[] owners) return;
        foreach (var oem in owners.Where(o => o.StartsWith("oem", StringComparison.OrdinalIgnoreCase)))
            if (!SetupUninstallOEMInf(oem, SuoiForceDelete, IntPtr.Zero)) throw new Win32Exception();
    }

    static void DeleteServiceIfPresent()
    {
        var scm = OpenSCManager(null, null, ScManagerAllAccess);
        if (scm == IntPtr.Zero) return;
        try
        {
            var svc = OpenService(scm, ServiceName, ServiceAllAccess);
            if (svc == IntPtr.Zero) return;
            DeleteService(svc);
            CloseServiceHandle(svc);
        }
        finally { CloseServiceHandle(scm); }
    }

    // ---------- Chave de registro que a biblioteca de sensores exige ----------

    /// <summary>Cria a chave se o instalador oficial não estiver lá. Devolve true se a chave é nossa.</summary>
    static bool EnsureRegistration()
    {
        using (var existing = Registry.LocalMachine.OpenSubKey(UninstallKey))
            if (existing?.GetValue("DisplayVersion") is string)
                return existing.GetValue(MarkerValue) != null;

        using var key = Registry.LocalMachine.CreateSubKey(UninstallKey, true);
        key.SetValue("DisplayName", OurDisplayName);
        key.SetValue("DisplayVersion", DriverVersion);
        key.SetValue("Publisher", "namazso");
        key.SetValue("SystemComponent", 1, RegistryValueKind.DWord); // não aparece em "Aplicativos instalados"
        key.SetValue(MarkerValue, 1, RegistryValueKind.DWord);
        return true;
    }

    static void RemoveRegistration()
    {
        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(UninstallKey))
                if (key?.GetValue(MarkerValue) == null) return; // é do instalador oficial: não mexe
            Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false);
        }
        catch (Exception ex) { Logger.Error("Remover registro temporário do PawnIO", ex); }
    }

    static bool Marker
    {
        get
        {
            using var k = Registry.LocalMachine.OpenSubKey(MarkerKey);
            return k?.GetValue(MarkerValue) is int v && v == 1;
        }
        set
        {
            using var k = Registry.LocalMachine.CreateSubKey(MarkerKey, true);
            if (value) k.SetValue(MarkerValue, 1, RegistryValueKind.DWord);
            else k.DeleteValue(MarkerValue, false);
        }
    }

    static bool DeviceResponds()
    {
        var h = CreateFile(DevicePath, 0xC0000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
        if (h == InvalidHandle) return false;
        CloseHandle(h);
        return true;
    }

    // ---------- Windows: SetupAPI, newdev, serviços ----------

    static readonly IntPtr InvalidHandle = new(-1);
    const int DicdGenerateId = 0x1;
    const int SpdrpHardwareId = 0x1;
    const int DifRegisterDevice = 0x19;
    const int DigcfAllClasses = 0x4;
    const int InstallFlagForce = 0x1;
    const int InstallFlagNonInteractive = 0x4;
    const int SuoiForceDelete = 0x1;
    const int ScManagerAllAccess = 0xF003F;
    const int ServiceAllAccess = 0xF01FF;

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetINFClass(string infName, out Guid classGuid, [Out] char[] className, int classNameSize, out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid classGuid, IntPtr hwndParent);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiCreateDeviceInfo(IntPtr set, string deviceName, ref Guid classGuid, string? description,
        IntPtr hwndParent, int flags, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiSetDeviceRegistryProperty(IntPtr set, ref SP_DEVINFO_DATA data, int property, byte[] buffer, int size);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref SP_DEVINFO_DATA data, int property, out int regType,
        byte[] buffer, int size, out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiCallClassInstaller(int installFunction, IntPtr set, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string? enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupUninstallOEMInf(string infFileName, int flags, IntPtr reserved);

    [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool UpdateDriverForPlugAndPlayDevices(IntPtr hwnd, string hardwareId, string fullInfPath, int flags, out bool rebootRequired);

    [DllImport("newdev.dll", SetLastError = true)]
    static extern bool DiUninstallDevice(IntPtr hwnd, IntPtr set, ref SP_DEVINFO_DATA data, int flags, out bool rebootRequired);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr OpenSCManager(string? machine, string? database, int access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr OpenService(IntPtr scm, string name, int access);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool DeleteService(IntPtr svc);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);
}
