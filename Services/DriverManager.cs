using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;

namespace UtiliTech.Services;

/// <summary>Dispositivo com problema no Gerenciador de Dispositivos.</summary>
public record ProblemDevice(string Name, string DeviceId, int ErrorCode, string Problem, string Advice, string Category)
{
    public string Detail => $"Código {ErrorCode} · {Problem}";
    /// <summary>Desativado (22) foi escolha de alguém, não defeito: aparece em cinza e não conta como problema.</summary>
    public bool IsDisabled => ErrorCode == 22;
    public string Color => IsDisabled ? Palette.Muted : Palette.Warn;
}

/// <summary>Driver disponível no Windows Update para este PC.</summary>
public class DriverUpdate : Observable
{
    public string UpdateId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";

    bool _selected = true;
    public bool Selected { get => _selected; set => SetField(ref _selected, value); }

    string _status = "";
    public string Status { get => _status; set => SetField(ref _status, value); }

    string _statusColor = Palette.Muted;
    public string StatusColor { get => _statusColor; set => SetField(ref _statusColor, value); }
}

public record DriverInstallResult(int Installed, int Failed, bool RebootRequired, Dictionary<string, bool> PerUpdate);

/// <summary>Andamento da instalação: qual driver (1 de N), em que etapa e o total em %.</summary>
public record DriverInstallProgress(string UpdateId, int Index, int Count, string Title, string Phase, double Percent, bool? Success = null);

/// <summary>
/// Verificação e instalação de drivers só com ferramentas do próprio Windows:
/// dispositivos com problema (WMI), catálogo de drivers do Windows Update (API do Windows Update)
/// e pacotes de driver de uma pasta (pnputil).
/// </summary>
public static class DriverManager
{
    // ---------- Dispositivos com problema ----------

    public static List<ProblemDevice> ScanProblems()
    {
        var list = new List<ProblemDevice>();
        foreach (var d in Wmi.Query("SELECT Name, DeviceID, ConfigManagerErrorCode, PNPClass FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0"))
        {
            int code = (int)Wmi.Long(d, "ConfigManagerErrorCode");
            if (code == 45) continue; // desconectado (ex.: pendrive que já foi removido): não é problema
            var (problem, advice) = Describe(code);
            string name = Wmi.Str(d, "Name");
            list.Add(new ProblemDevice(name.Length > 0 ? name : "Dispositivo desconhecido", Wmi.Str(d, "DeviceID"), code, problem, advice,
                Wmi.Str(d, "PNPClass")));
        }
        return list.OrderBy(p => p.ErrorCode == 22 ? 1 : 0).ThenBy(p => p.Name).ToList();
    }

    static (string Problem, string Advice) Describe(int code) => code switch
    {
        1 => ("não configurado", "Procure o driver no Windows Update ou no site do fabricante."),
        3 => ("driver corrompido ou falta de memória", "Reinstale o driver."),
        10 => ("o dispositivo não pode iniciar", "Atualize o driver; se continuar, reinstale ou troque a porta/cabo."),
        12 => ("conflito de recursos", "Atualize a BIOS e os drivers do chipset."),
        14 => ("precisa reiniciar o PC", "Reinicie o computador."),
        18 => ("precisa reinstalar o driver", "Reinstale o driver."),
        19 => ("erro no registro do dispositivo", "Desinstale o dispositivo e procure alterações de hardware."),
        22 => ("desativado", "Ative pelo Gerenciador de Dispositivos, se for usado."),
        24 => ("não encontrado ou com defeito", "Confira a conexão do dispositivo."),
        28 => ("driver não instalado", "Procure no Windows Update (abaixo) ou instale de uma pasta de drivers."),
        31 => ("não está funcionando direito", "Atualize ou reinstale o driver."),
        32 => ("serviço do driver desativado", "Reinstale o driver."),
        37 => ("o driver falhou ao iniciar", "Instale um driver mais novo do fabricante."),
        39 => ("driver ausente ou corrompido", "Reinstale o driver."),
        43 => ("o dispositivo relatou problema e parou", "Atualize o driver; em USB, teste outra porta; em placa de vídeo, reinstale o driver."),
        48 => ("driver bloqueado por incompatibilidade", "Procure uma versão atual do driver no fabricante."),
        52 => ("assinatura do driver não pôde ser verificada", "Instale o driver oficial (assinado) do fabricante."),
        _ => ($"problema código {code}", "Atualize ou reinstale o driver."),
    };

    /// <summary>Pede ao Windows para detectar dispositivos novos (Gerenciador → Verificar alterações de hardware).</summary>
    public static Task<int> ScanHardwareAsync(Action<string> log) => Repairs.Step(log, "pnputil", "/scan-devices");

    // ---------- Pasta de drivers (pnputil) ----------

    /// <summary>Instala todos os .inf da pasta e subpastas nos dispositivos que servirem.</summary>
    public static async Task<string> InstallFromFolderAsync(string folder, Action<string> log)
    {
        if (!Directory.EnumerateFiles(folder, "*.inf", SearchOption.AllDirectories).Any())
            throw new InvalidOperationException("Nenhum arquivo .inf nesta pasta. Escolha a pasta onde o pacote de drivers foi extraído.");

        var lines = new List<string>();
        int code = await Repairs.Step(m => { lines.Add(m); log(m); }, "pnputil", $"/add-driver \"{Path.Combine(folder, "*.inf")}\" /subdirs /install");

        // O resumo do pnputil termina com números (pacotes adicionados / instalados); mostra a última linha útil
        var summary = lines.LastOrDefault(l => Regex.IsMatch(l, @"\d")) ?? "";
        return code switch
        {
            0 => "Drivers adicionados e instalados" + (summary.Length > 0 ? $" · {summary.Trim()}" : ""),
            3010 => "Drivers instalados · reinicie o PC",
            259 => "Nenhum driver novo serviu para este PC (já estão atualizados ou são de outro modelo)",
            _ => $"O pnputil terminou com código {code}; veja o registro"
        };
    }

    // ---------- Windows Update ----------

    // A API do Windows Update é COM e precisa de uma thread STA fixa: os objetos encontrados na busca
    // são usados depois na instalação, então tudo roda nesta mesma thread.
    static readonly StaWorker Wua = new();
    static readonly Dictionary<string, dynamic> Found = new();
    static dynamic? _session;

    /// <summary>Procura drivers que faltam ou estão desatualizados no catálogo do Windows Update (pode levar 1–2 min).</summary>
    public static Task<List<DriverUpdate>> SearchWindowsUpdateAsync() => Wua.Run(() =>
    {
        _session ??= Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session")
                                              ?? throw new InvalidOperationException("Windows Update indisponível neste PC."))!;
        _session.ClientApplicationID = "UtiliTech";
        dynamic searcher = _session.CreateUpdateSearcher();
        searcher.Online = true;
        searcher.ServerSelection = 2; // ssWindowsUpdate: catálogo da Microsoft, mesmo em PC configurado com WSUS
        dynamic result = searcher.Search("IsInstalled=0 and Type='Driver' and IsHidden=0");

        Found.Clear();
        var list = new List<DriverUpdate>();
        for (int i = 0; i < (int)result.Updates.Count; i++)
        {
            dynamic u = result.Updates.Item(i);
            string id = u.Identity.UpdateID;
            Found[id] = u;
            string manufacturer = Safe(() => (string)u.DriverManufacturer);
            string cls = Safe(() => (string)u.DriverClass);
            string date = Safe(() => ((DateTime)u.DriverVerDate).ToString("dd/MM/yyyy"));
            long size = Safe<long>(() => Convert.ToInt64((object)u.MaxDownloadSize));
            var parts = new[] { cls, manufacturer, date.Length > 0 ? "versão de " + date : "", size > 0 ? Fmt.Bytes(size) : "" }
                .Where(p => p.Length > 0);
            // O título do catálogo é técnico ("Intel - Other hardware - ... - A123"); o modelo é o nome que o técnico reconhece
            string model = Safe(() => (string)u.DriverModel);
            list.Add(new DriverUpdate { UpdateId = id, Title = model.Length > 0 ? model : (string)u.Title, Detail = string.Join(" · ", parts) });
        }
        return list;
    });

    /// <summary>
    /// Baixa e instala os drivers escolhidos da última busca, um por vez, para a tela mostrar qual está
    /// em andamento. O total em % é pesado pelo tamanho de cada driver (baixar = 80% do peso, instalar = 20%).
    /// </summary>
    public static Task<DriverInstallResult> InstallWindowsUpdateAsync(IEnumerable<string> updateIds,
        IProgress<DriverInstallProgress> progress, Action<string> log) => Wua.Run(() =>
    {
        if (_session == null) throw new InvalidOperationException("Faça a busca antes de instalar.");
        var chosen = updateIds.Where(Found.ContainsKey).Select(id => (Id: id, Update: Found[id])).ToList();
        if (chosen.Count == 0) throw new InvalidOperationException("Nenhum driver selecionado.");

        // Drivers pequenos (só .inf) contam no mínimo 1 MB, para a barra andar também com eles
        var weights = chosen.Select(c => Math.Max(1_000_000d, Safe<double>(() => Convert.ToDouble((object)c.Update.MaxDownloadSize)))).ToList();
        double total = weights.Sum(), done = 0;

        var per = new Dictionary<string, bool>();
        int ok = 0, failed = 0;
        bool reboot = false;
        for (int i = 0; i < chosen.Count; i++)
        {
            var (id, u) = chosen[i];
            string title = Safe(() => (string)u.DriverModel);
            if (title.Length == 0) title = (string)u.Title;
            void Report(string phase, double fraction, bool? success = null) =>
                progress.Report(new DriverInstallProgress(id, i + 1, chosen.Count, title, phase,
                    Math.Min(100, (done + weights[i] * fraction) * 100 / total), success));

            bool success = false;
            try
            {
                if (!(bool)u.EulaAccepted) u.AcceptEula();
                dynamic one = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")!)!;
                one.Add(u);

                Report("Baixando", 0);
                log($"[{i + 1}/{chosen.Count}] Baixando {title}...");
                dynamic downloader = _session.CreateUpdateDownloader();
                downloader.Updates = one;
                int downloadCode = (int)downloader.Download().ResultCode;
                if (downloadCode is not (2 or 3)) throw new InvalidOperationException($"download falhou (código {downloadCode})");

                Report("Instalando", 0.8);
                log($"[{i + 1}/{chosen.Count}] Instalando {title}...");
                dynamic installer = _session.CreateUpdateInstaller();
                installer.Updates = one;
                dynamic result = installer.Install();
                int code = (int)result.ResultCode; // 2 = sucesso, 3 = sucesso com erros
                success = code is 2 or 3;
                if ((bool)result.RebootRequired) reboot = true;
                log($"[{i + 1}/{chosen.Count}] {title}: {(success ? "instalado" : $"falhou (código {code})")}");
            }
            catch (Exception ex) { log($"[{i + 1}/{chosen.Count}] {title}: falhou ({ex.Message})"); }

            per[id] = success;
            if (success) ok++; else failed++;
            done += weights[i];
            Report(success ? "Instalado" : "Falhou", 0, success);
        }
        return new DriverInstallResult(ok, failed, reboot, per);
    });

    static T Safe<T>(Func<T> get)
    {
        try { return get() ?? default!; }
        catch { return default!; }
    }
    static string Safe(Func<string> get)
    {
        try { return get() ?? ""; }
        catch { return ""; }
    }

    /// <summary>Thread STA dedicada que executa as tarefas em fila.</summary>
    sealed class StaWorker
    {
        readonly BlockingCollection<Action> _queue = new();

        public StaWorker()
        {
            var t = new Thread(() => { foreach (var job in _queue.GetConsumingEnumerable()) job(); })
            {
                IsBackground = true,
                Name = "Windows Update (STA)",
            };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        public Task<T> Run<T>(Func<T> work)
        {
            var tcs = new TaskCompletionSource<T>();
            _queue.Add(() =>
            {
                try { tcs.SetResult(work()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }
    }
}
