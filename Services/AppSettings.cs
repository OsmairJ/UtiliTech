using System.IO;
using System.Text;
using System.Text.Json;

namespace UtiliTech.Services;

/// <summary>Preferências do app (settings.json ao lado do .exe, ou no AppData se a pasta não aceitar escrita).</summary>
public class AppSettings
{
    const string FileName = "settings.json";

    public bool AlertsEnabled { get; set; } = true;
    public int CpuAlertLimit { get; set; } = 90;
    public int GpuAlertLimit { get; set; } = 90;
    public bool LightTheme { get; set; }
    /// <summary>Repositório do GitHub onde saem as versões (usuário/nome); vazio = não procura atualização.</summary>
    public string UpdateRepo { get; set; } = "OsmairJ/UtiliTech";
    public bool CheckUpdatesOnStart { get; set; } = true;
    /// <summary>Domínio (DNS) usado na ferramenta Nome e domínio; digitado pelo técnico na tela e salvo só aqui.</summary>
    public string DomainName { get; set; } = "";
    /// <summary>Usuário do domínio usado por último (a senha nunca é salva).</summary>
    public string LastDomainUser { get; set; } = "";

    public static AppSettings Current { get; } = Load();

    static string[] Paths() => new[]
    {
        Path.Combine(AppContext.BaseDirectory, FileName),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UtiliTech", FileName),
    };

    static AppSettings Load()
    {
        foreach (var path in Paths())
        {
            try
            {
                if (File.Exists(path) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) is { } s) return s;
            }
            catch { }
        }
        return new AppSettings();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        foreach (var path in Paths())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, json, Encoding.UTF8);
                return;
            }
            catch { }
        }
    }
}
