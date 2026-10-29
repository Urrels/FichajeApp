using FichajeApp.Api.Infrastructure;
using FichajeApp.Api.Models;
using FichajeApp.Api.Repositorios;

namespace FichajeApp.Api.Services;

// Padrón de empleados en la hoja "Empleados":
//   A Id | B NombreCompleto | C Email | D PinHash | E Activo | F Rol inicial (opcional) | G IntentosFallidos | H Bloqueado
public class GoogleSheetsEmpleadosService : IEmpleadosRepositorio
{
    private const string Columnas = "A:H";
    private static readonly SemaphoreSlim _escritura = new(1, 1);

    private readonly HojaSheets _hoja;

    public GoogleSheetsEmpleadosService(GoogleSheetsClient cliente)
    {
        _hoja = new HojaSheets(cliente, "Empleados");
    }

    public async Task<List<EmpleadoDto>> ListarAsync()
    {
        var empleados = new List<EmpleadoDto>();
        foreach (var fila in await _hoja.LeerAsync(Columnas))
        {
            if (fila.Count < 5) continue;

            var rol = fila.Texto(5);
            empleados.Add(new EmpleadoDto
            {
                Id = fila.Entero(0),
                NombreCompleto = fila.Texto(1),
                Email = fila.Texto(2),
                PinHash = fila.Texto(3),
                Activo = fila.Booleano(4),
                Rol = string.IsNullOrWhiteSpace(rol) ? "Empleado" : rol,
                IntentosFallidos = fila.Entero(6),
                Bloqueado = fila.Booleano(7),
                Fila = fila.Numero
            });
        }
        return empleados;
    }

    // Nombre anterior, lo siguen usando los controllers de fichaje y de supervisor
    public Task<List<EmpleadoDto>> ObtenerEmpleadosAsync() => ListarAsync();

    public async Task<EmpleadoDto?> ObtenerPorIdAsync(int id) =>
        (await ListarAsync()).FirstOrDefault(e => e.Id == id);

    public async Task<EmpleadoDto?> BuscarPorEmailAsync(string email, bool incluirInactivos = false)
    {
        var empleados = await ListarAsync();
        return empleados.FirstOrDefault(e =>
            e.Email.Equals(email, StringComparison.OrdinalIgnoreCase) && (incluirInactivos || e.Activo));
    }

    public async Task<EmpleadoDto> CrearAsync(EmpleadoDto empleado)
    {
        await _escritura.WaitAsync();
        try
        {
            var existentes = await ListarAsync();
            empleado.Id = existentes.Count == 0 ? 1 : existentes.Max(e => e.Id) + 1;
            await _hoja.AgregarAsync(Fila(empleado));
            return empleado;
        }
        finally { _escritura.Release(); }
    }

    public async Task ActualizarAsync(EmpleadoDto empleado)
    {
        if (empleado.Fila < 2)
            throw new InvalidOperationException("El empleado no tiene fila asociada en la hoja.");

        await _escritura.WaitAsync();
        try { await _hoja.ActualizarAsync(empleado.Fila, Fila(empleado)); }
        finally { _escritura.Release(); }
    }

    private static IList<object> Fila(EmpleadoDto e) => new List<object>
    {
        e.Id, e.NombreCompleto, e.Email, e.PinHash, e.Activo, e.Rol, e.IntentosFallidos, e.Bloqueado
    };
}
