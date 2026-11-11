using System.Text.RegularExpressions;

namespace FichajeApp.Api.Tests;

// Un XAML que apunta a un manejador que no existe (Clicked="OnAlgo") compila bien y recién falla al abrir la pantalla:
// la app se cierra. Pasó al quitar un botón del login. Esta prueba lo detecta sin necesidad de un emulador.
public class XamlManejadoresTests
{
    private static readonly Regex Evento =
        new(@"\b(Clicked|Tapped|Completed|SelectionChanged|SizeChanged|Toggled|TextChanged|Appearing|Disappearing|CheckedChanged|ValueChanged|Refreshing)=""([A-Za-z_][A-Za-z0-9_]*)""");

    private static string CarpetaDeLaApp()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "FichajeApp.Mobile")))
            dir = dir.Parent;
        return dir is null ? "" : Path.Combine(dir.FullName, "src", "FichajeApp.Mobile");
    }

    [Fact]
    public void CadaManejadorDeEventoDelXaml_ExisteEnSuCodigo()
    {
        var carpeta = CarpetaDeLaApp();
        Assert.False(string.IsNullOrEmpty(carpeta), "No se encontró la carpeta de la app móvil");

        var faltan = new List<string>();
        foreach (var xaml in Directory.EnumerateFiles(carpeta, "*.xaml", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var cs = xaml + ".cs";
            if (!File.Exists(cs)) continue;
            var codigo = File.ReadAllText(cs);

            foreach (Match m in Evento.Matches(File.ReadAllText(xaml)))
            {
                var nombre = m.Groups[2].Value;
                if (!Regex.IsMatch(codigo, $@"\b{Regex.Escape(nombre)}\s*\("))
                    faltan.Add($"{Path.GetFileName(xaml)} → {nombre}");
            }
        }

        Assert.True(faltan.Count == 0, "Manejadores que el XAML usa y no existen: " + string.Join("; ", faltan));
    }
}
