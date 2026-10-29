using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FichajeApp.Mobile.ViewModels;

// Base de los ViewModels de administración: notificación de cambios y estado de "procesando"
public abstract class VmBase : INotifyPropertyChanged
{
    private bool _procesando;
    private string _estado = string.Empty;

    public bool Procesando
    {
        get => _procesando;
        protected set => Set(ref _procesando, value);
    }

    public string Estado
    {
        get => _estado;
        protected set => Set(ref _estado, value);
    }

    // Ejecuta una operación mostrando el indicador de espera; nunca deja la pantalla "procesando" si falla
    protected async Task ConEspera(Func<Task> operacion)
    {
        if (Procesando) return;
        Procesando = true;
        try { await operacion(); }
        finally { Procesando = false; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T campo, T valor, [CallerMemberName] string? nombre = null)
    {
        if (EqualityComparer<T>.Default.Equals(campo, valor)) return false;
        campo = valor;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nombre));
        return true;
    }

    protected void Notificar([CallerMemberName] string? nombre = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nombre));
}
