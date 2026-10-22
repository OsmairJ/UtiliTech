using System.IO;
using System.Management;
using Microsoft.Win32;

namespace UtiliTech.Services;

public enum RestorePointStatus { Created, Recent, Disabled, Failed }

public record RestorePointResult(RestorePointStatus Status, string Message);

/// <summary>
/// Ponto de restauração do Windows (WMI root\default:SystemRestore) antes de mexer no PC:
/// redefinir rede ou Windows Update, instalar drivers, renomear e ingressar no domínio.
/// </summary>
public static class RestorePoint
{
    const string Scope = @"root\default";
    const string FrequencyKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
    const string FrequencyValue = "SystemRestorePointCreationFrequency";

    /// <summary>Tipos de ponto do Windows (RESTOREPOINTINFO.dwRestorePtType).</summary>
    public const int ModifySettings = 12, DeviceDriverInstall = 10;

    /// <summary>Um ponto por sessão já basta para várias ações seguidas; depois de 30 min cria outro.</summary>
    static readonly TimeSpan ReuseWindow = TimeSpan.FromMinutes(30);
    static DateTime? _lastCreated;

    /// <summary>Cria o ponto (leva de 10 s a 1 min). Nunca lança exceção: o resultado diz o que houve.</summary>
    public static RestorePointResult Create(string description, int type = ModifySettings)
    {
        if (_lastCreated is { } last && DateTime.Now - last < ReuseWindow)
            return new(RestorePointStatus.Recent, $"Ponto de restauração já criado às {last:HH:mm} nesta sessão");

        // O Windows só cria 1 ponto a cada 24 h por padrão (e diz que criou). Zera o limite só durante a chamada.
        object? oldFrequency = null;
        bool changedFrequency = false;
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(FrequencyKey, true);
            oldFrequency = key.GetValue(FrequencyValue);
            key.SetValue(FrequencyValue, 0, RegistryValueKind.DWord);
            changedFrequency = true;
        }
        catch (Exception ex) { Logger.Error("Ponto de restauração: não foi possível ajustar o limite de 24 h", ex); }

        try
        {
            using var sr = new ManagementClass(Scope, "SystemRestore", null);
            var args = sr.GetMethodParameters("CreateRestorePoint");
            args["Description"] = "UtiliTech: " + description;
            args["RestorePointType"] = type;
            args["EventType"] = 100; // BEGIN_SYSTEM_CHANGE
            var result = sr.InvokeMethod("CreateRestorePoint", args, null);
            uint code = Convert.ToUInt32(result?["ReturnValue"] ?? 1u);
            if (code == 0)
            {
                _lastCreated = DateTime.Now;
                Logger.Info($"Ponto de restauração criado: {description}");
                return new(RestorePointStatus.Created, $"Ponto de restauração criado ({DateTime.Now:HH:mm})");
            }
            // 1058 = serviço desativado: a Proteção do Sistema está desligada
            if (code == 1058 || !IsEnabled())
                return new(RestorePointStatus.Disabled, "A Proteção do Sistema está desligada na unidade do Windows");
            return new(RestorePointStatus.Failed, $"O Windows recusou o ponto de restauração (código {code})");
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.InvalidClass)
        {
            return new(RestorePointStatus.Failed, "Esta edição do Windows não tem Restauração do Sistema");
        }
        catch (Exception ex)
        {
            Logger.Error("Ponto de restauração", ex);
            return new(RestorePointStatus.Failed, ex.Message);
        }
        finally
        {
            if (changedFrequency)
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(FrequencyKey, true);
                    if (oldFrequency == null) key?.DeleteValue(FrequencyValue, false);
                    else key?.SetValue(FrequencyValue, oldFrequency, RegistryValueKind.DWord);
                }
                catch { }
        }
    }

    /// <summary>Liga a Proteção do Sistema na unidade do Windows (o próprio Windows define o espaço reservado).</summary>
    public static bool Enable()
    {
        try
        {
            string drive = Path.GetPathRoot(Environment.SystemDirectory)!; // "C:\"
            using var sr = new ManagementClass(Scope, "SystemRestore", null);
            var args = sr.GetMethodParameters("Enable");
            args["Drive"] = drive;
            var result = sr.InvokeMethod("Enable", args, null);
            uint code = Convert.ToUInt32(result?["ReturnValue"] ?? 1u);
            Logger.Info($"Proteção do Sistema ligada em {drive} (código {code})");
            return code == 0;
        }
        catch (Exception ex)
        {
            Logger.Error("Ligar a Proteção do Sistema", ex);
            return false;
        }
    }

    /// <summary>A Proteção do Sistema está ligada? (RPSessionInterval = 1 quando há unidade protegida)</summary>
    static bool IsEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(FrequencyKey);
            return key?.GetValue("RPSessionInterval") is int v && v != 0;
        }
        catch { return true; }
    }
}
