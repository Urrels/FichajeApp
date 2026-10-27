using System.Collections.Concurrent;

namespace FichajeApp.Api.Concurrency;

// Exclusión mutua por clave (ej. empleado): evita que dos fichajes simultáneos
// del mismo empleado lean el mismo "último tipo" y queden ambos como "Entrada".
// Válido para una sola instancia de la API; con varias instancias hace falta
// un lock distribuido o una restricción en la base de datos.
public class BloqueoPorClave
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaforos = new();

    public async Task<IDisposable> AdquirirAsync(string clave)
    {
        var semaforo = _semaforos.GetOrAdd(clave, _ => new SemaphoreSlim(1, 1));
        await semaforo.WaitAsync();
        return new Liberador(semaforo);
    }

    private sealed class Liberador : IDisposable
    {
        private SemaphoreSlim? _semaforo;
        public Liberador(SemaphoreSlim semaforo) => _semaforo = semaforo;
        public void Dispose() => Interlocked.Exchange(ref _semaforo, null)?.Release();
    }
}
