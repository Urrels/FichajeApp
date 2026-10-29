namespace FichajeApp.Api.Dominio.Seguridad;

// Patrón Composite (portado de ingenieria_software): NodoPermiso es el componente,
// Rol es la rama (puede tener roles hijos y permisos) y Permiso es la hoja.
public abstract class NodoPermiso
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int? PadreId { get; set; }

    public abstract void Agregar(NodoPermiso nodo);
    public abstract void Quitar(NodoPermiso nodo);
    public abstract IList<NodoPermiso> ObtenerHijos();
    public abstract bool EsHoja();

    // Patrón Prototype: copia profunda (se usa para duplicar un rol)
    public abstract NodoPermiso Clonar();

    public override string ToString() => Nombre;
}

public class Rol : NodoPermiso
{
    // Rol del sistema: no se puede eliminar
    public bool Protegido { get; set; }

    private readonly List<NodoPermiso> _hijos = new();

    public override void Agregar(NodoPermiso nodo) => _hijos.Add(nodo);
    public override void Quitar(NodoPermiso nodo) => _hijos.Remove(nodo);
    public override IList<NodoPermiso> ObtenerHijos() => _hijos;
    public override bool EsHoja() => false;

    public override NodoPermiso Clonar()
    {
        var copia = new Rol { Id = Id, Nombre = Nombre, PadreId = PadreId, Protegido = Protegido };
        foreach (var hijo in _hijos)
            copia.Agregar(hijo.Clonar());
        return copia;
    }
}

public class Permiso : NodoPermiso
{
    public override void Agregar(NodoPermiso nodo) =>
        throw new InvalidOperationException("Un permiso no puede contener hijos.");

    public override void Quitar(NodoPermiso nodo) =>
        throw new InvalidOperationException("Un permiso no puede contener hijos.");

    public override IList<NodoPermiso> ObtenerHijos() => new List<NodoPermiso>();
    public override bool EsHoja() => true;

    public override NodoPermiso Clonar() => new Permiso { Id = Id, Nombre = Nombre, PadreId = PadreId };
}
