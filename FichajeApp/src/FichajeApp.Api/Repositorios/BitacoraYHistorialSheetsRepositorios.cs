using FichajeApp.Api.Infrastructure;
using FichajeApp.Api.Models;

namespace FichajeApp.Api.Repositorios;

// Hoja Bitacora: FechaHora | Usuario | Accion  (solo se agregan filas)
public class BitacoraSheetsRepositorio : IBitacoraRepositorio
{
    private readonly HojaSheets _hoja;

    public BitacoraSheetsRepositorio(GoogleSheetsClient cliente) => _hoja = new HojaSheets(cliente, Planilla.Bitacora);

    public Task RegistrarAsync(EntradaBitacora e) =>
        _hoja.AgregarAsync(new List<object> { HojaSheets.FechaHora(e.FechaHora), e.Usuario, e.Accion });

    public async Task<List<EntradaBitacora>> ListarAsync() =>
        (await _hoja.LeerAsync("A:C")).Select(f => new EntradaBitacora
        {
            FechaHora = f.Fecha(0),
            Usuario = f.Texto(1),
            Accion = f.Texto(2)
        }).ToList();
}

// Hoja UsuarioHistorial: foto del usuario en cada cambio. Solo inserción: no hay actualizaciones ni bajas.
public class HistorialUsuariosSheetsRepositorio : IHistorialUsuariosRepositorio
{
    private static readonly SemaphoreSlim _escritura = new(1, 1);
    private readonly HojaSheets _hoja;

    public HistorialUsuariosSheetsRepositorio(GoogleSheetsClient cliente) =>
        _hoja = new HojaSheets(cliente, Planilla.UsuarioHistorial);

    public async Task<int> RegistrarAsync(HistorialUsuario foto)
    {
        await _escritura.WaitAsync();
        try
        {
            var existentes = await _hoja.LeerAsync("A:A");
            foto.Id = existentes.Count == 0 ? 1 : existentes.Max(f => f.Entero(0)) + 1;

            await _hoja.AgregarAsync(new List<object>
            {
                foto.Id, HojaSheets.FechaHora(foto.FechaHora), foto.EmpleadoId, foto.Tipo, foto.RealizadoPor,
                foto.Nombre, foto.Email, foto.Activo, foto.Bloqueado, foto.IntentosFallidos,
                string.Join(",", foto.RolIds.OrderBy(r => r)), foto.VersionOrigen?.ToString() ?? ""
            });
            return foto.Id;
        }
        finally { _escritura.Release(); }
    }

    public async Task<List<HistorialUsuario>> ListarAsync(int? empleadoId = null)
    {
        var todos = (await _hoja.LeerAsync("A:L")).Select(Mapear);
        if (empleadoId.HasValue) todos = todos.Where(h => h.EmpleadoId == empleadoId.Value);
        return todos.OrderByDescending(h => h.FechaHora).ThenByDescending(h => h.Id).ToList();
    }

    public async Task<HistorialUsuario?> ObtenerAsync(int id) =>
        (await ListarAsync()).FirstOrDefault(h => h.Id == id);

    private static HistorialUsuario Mapear(FilaHoja f) => new()
    {
        Id = f.Entero(0),
        FechaHora = f.Fecha(1),
        EmpleadoId = f.Entero(2),
        Tipo = f.Texto(3),
        RealizadoPor = f.Texto(4),
        Nombre = f.Texto(5),
        Email = f.Texto(6),
        Activo = f.Booleano(7),
        Bloqueado = f.Booleano(8),
        IntentosFallidos = f.Entero(9),
        RolIds = f.Texto(10).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse).ToList(),
        VersionOrigen = f.EnteroONulo(11)
    };
}
