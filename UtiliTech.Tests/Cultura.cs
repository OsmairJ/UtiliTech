using System.Globalization;
using System.Runtime.CompilerServices;

namespace UtiliTech.Tests;

/// <summary>
/// Os testes conferem textos em português ("22,1 GB", "22/10/2026"). O GitHub roda em inglês (en-US):
/// fixa pt-BR antes de qualquer teste, como o próprio app faz ao abrir.
/// </summary>
static class Cultura
{
    [ModuleInitializer]
    internal static void PtBr()
    {
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentCulture = pt;
        CultureInfo.DefaultThreadCurrentUICulture = pt;
        CultureInfo.CurrentCulture = pt;
        CultureInfo.CurrentUICulture = pt;
    }
}
