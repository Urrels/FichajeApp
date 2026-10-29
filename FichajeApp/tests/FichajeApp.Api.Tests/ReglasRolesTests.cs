using FichajeApp.Api.Dominio.Seguridad;

namespace FichajeApp.Api.Tests;

public class ReglasRolesTests
{
    private static Rol R(int id, string nombre, int? padre = null) => new() { Id = id, Nombre = nombre, PadreId = padre };
    private static Permiso P(int id, string nombre) => new() { Id = id, Nombre = nombre };

    [Fact]
    public void ArmarArbol_CuelgaRolesDeSuPadre_YCadaPermisoDeSuRol()
    {
        var a = R(1, "A");
        var b = R(2, "B", 1);

        var raices = ReglasRoles.ArmarArbol(new[] { a, b }, new[] { (1, P(10, "x")), (2, P(11, "y")) });

        var raiz = Assert.IsType<Rol>(Assert.Single(raices));
        Assert.Equal(new[] { "B", "x" }, raiz.ObtenerHijos().Select(h => h.Nombre).OrderBy(n => n));
        // sin herencia: B solo tiene su propio permiso, no el de su padre
        Assert.Equal(new[] { "y" }, b.ObtenerHijos().Select(h => h.Nombre));
    }

    [Fact]
    public void Permiso_NoPuedeTenerHijos()
    {
        Assert.Throws<InvalidOperationException>(() => P(1, "x").Agregar(P(2, "y")));
    }

    [Fact]
    public void Clonar_HaceUnaCopiaProfunda()
    {
        var original = R(1, "A");
        original.Agregar(P(10, "x"));
        original.Protegido = true;

        var copia = (Rol)original.Clonar();
        copia.Nombre = "Copia";
        copia.Agregar(P(11, "y"));

        Assert.Equal("A", original.Nombre);
        Assert.Single(original.ObtenerHijos());
        Assert.Equal(2, copia.ObtenerHijos().Count);
    }

    [Fact]
    public void GeneraCiclo_DetectaAlPropioRolYASusDescendientes()
    {
        var todos = new[] { R(1, "A"), R(2, "B", 1), R(3, "C", 2) };

        Assert.True(ReglasRoles.GeneraCiclo(1, 3, todos));  // C cuelga de A: A bajo C sería un ciclo
        Assert.True(ReglasRoles.GeneraCiclo(1, 1, todos));  // su propio padre
        Assert.False(ReglasRoles.GeneraCiclo(3, 1, todos)); // C bajo A: válido
    }

    [Fact]
    public void EsAncestroDelPadreActual_SoloPermiteMovimientosLaterales()
    {
        // A → B → C → D ; E es un rol aparte. El padre actual de D es C.
        var todos = new[] { R(1, "A"), R(2, "B", 1), R(3, "C", 2), R(4, "D", 3), R(5, "E") };

        Assert.True(ReglasRoles.EsAncestroDelPadreActual(3, 1, todos));   // A está por encima de C: no
        Assert.True(ReglasRoles.EsAncestroDelPadreActual(3, 2, todos));   // B también
        Assert.False(ReglasRoles.EsAncestroDelPadreActual(3, 3, todos));  // dejar el mismo padre: permitido
        Assert.False(ReglasRoles.EsAncestroDelPadreActual(3, 5, todos));  // pasar a un rol no emparentado: permitido
    }

    [Fact]
    public void SubarbolDe_IncluyeAlRolYTodosSusDescendientes()
    {
        var todos = new[] { R(1, "A"), R(2, "B", 1), R(3, "C", 2), R(4, "Otro") };

        Assert.Equal(new[] { 1, 2, 3 }, ReglasRoles.SubarbolDe(1, todos).OrderBy(i => i));
    }

    // ---- Guarda anti-lockout ----

    private static Dictionary<int, HashSet<string>> PermisosPorRol() => new()
    {
        [1] = new HashSet<string>(CatalogoPermisos.Todos),                    // Administrador
        [2] = new HashSet<string> { CatalogoPermisos.FicharAsistencia }       // Empleado
    };

    [Fact]
    public void AntiLockout_QuitarLosPermisosAlUnicoAdministrador_Falla()
    {
        var usuarios = new Dictionary<int, List<int>> { [10] = new() { 1 }, [11] = new() { 2 } };

        var perdido = ReglasRoles.PermisoQueSePerderia(usuarios, PermisosPorRol(),
            rolId: 1, nuevosPermisos: new HashSet<string> { CatalogoPermisos.FicharAsistencia });

        Assert.NotNull(perdido);
    }

    [Fact]
    public void AntiLockout_SacarleElRolAlUnicoAdministrador_Falla()
    {
        var usuarios = new Dictionary<int, List<int>> { [10] = new() { 1 }, [11] = new() { 2 } };

        var perdido = ReglasRoles.PermisoQueSePerderia(usuarios, PermisosPorRol(),
            usuarioId: 10, nuevosRolIds: new List<int> { 2 });

        Assert.NotNull(perdido);
    }

    [Fact]
    public void AntiLockout_DarDeBajaAlUnicoAdministrador_Falla()
    {
        var usuarios = new Dictionary<int, List<int>> { [10] = new() { 1 } };

        // una baja se simula con una lista vacía de roles
        Assert.NotNull(ReglasRoles.PermisoQueSePerderia(usuarios, PermisosPorRol(), usuarioId: 10, nuevosRolIds: new List<int>()));
    }

    [Fact]
    public void AntiLockout_ConOtroAdministradorActivo_SePermite()
    {
        var usuarios = new Dictionary<int, List<int>> { [10] = new() { 1 }, [12] = new() { 1 } };

        var perdido = ReglasRoles.PermisoQueSePerderia(usuarios, PermisosPorRol(),
            usuarioId: 10, nuevosRolIds: new List<int>());

        Assert.Null(perdido);
    }

    [Fact]
    public void AntiLockout_CambiosQueNoTocanLaAdministracion_SePermiten()
    {
        var usuarios = new Dictionary<int, List<int>> { [10] = new() { 1 }, [11] = new() { 2 } };

        Assert.Null(ReglasRoles.PermisoQueSePerderia(usuarios, PermisosPorRol(),
            rolId: 2, nuevosPermisos: new HashSet<string> { CatalogoPermisos.FicharAsistencia, CatalogoPermisos.CambiarPin }));
    }
}
