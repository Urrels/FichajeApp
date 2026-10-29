# FichajeApp

## Cómo armar el proyecto

1. Seguí los comandos `dotnet new` que ya vimos (sln + webapi + maui) para generar
   la estructura vacía de `FichajeApp.Api` y `FichajeApp.Mobile`.
2. Reemplazá/copiá el contenido de las carpetas de este zip (Controllers, Services,
   Models, Helpers, Validaciones, Storage, Observers, ViewModels, Views, Converters)
   dentro de cada proyecto generado, respetando la misma estructura de carpetas.
3. En `FichajeApp.Api`:
   - `dotnet add package Google.Apis.Sheets.v4`
   - `dotnet add package BCrypt.Net-Next`
   - `dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer`
   - Poné el JSON de tu Service Account en `credentials/service-account.json`
   - Completá `SpreadsheetId` y `Jwt:Secret` en `appsettings.json`
4. En `FichajeApp.Mobile`:
   - `dotnet workload install maui` (si no lo tenés)
   - `dotnet add package Plugin.Fingerprint`
   - Agregá los permisos de `FichajeApp.Mobile.csproj.snippet.txt` en
     `Platforms/Android/AndroidManifest.xml` y `Platforms/iOS/Info.plist`
5. Corré primero el backend (`dotnet run` en `FichajeApp.Api`, revisá el puerto en
   `Properties/launchSettings.json` y ajustá `BaseUrl` en `AuthService.cs` / `ApiService.cs`
   si no es `https://localhost:7001`).
6. Corré `FichajeApp.Mobile` apuntando a un emulador/dispositivo.

## Patrones de diseño aplicados

- **Singleton** → `Services/SessionManager.cs` (mobile): única instancia en memoria
  con la sesión activa (`SessionManager.Instance`).
- **Observer** → `ISessionObserver` (mobile, Subject = `SessionManager`) y
  `IFichajeObserver` / `LogFichajeObserver` (backend, Subject = `FichajeService`).
- **Composite** → `Validaciones/IReglaValidacion.cs`, `ValidacionUbicacion.cs`,
  `ValidacionEmpleadoActivo.cs`, `ValidacionCompuesta.cs` (backend).
- **Strategy** → `Storage/IFichajeStorageStrategy.cs` + `GoogleSheetsStorageStrategy.cs`
  (backend): permite cambiar el destino de persistencia sin tocar el resto del sistema.

## Registro de ubicación (modo "Libre")

La ubicación ya no se valida contra una sede fija: siempre se registra dónde fichó
(coordenadas, precisión, dirección y link a Google Maps). Se rechazan ubicaciones
simuladas (`IsFromMockProvider`) y precisiones peores que `Fichaje:PrecisionMaxMetros`.
La app no rastrea en segundo plano: pide ubicación solo al tocar "Fichar".

Configuración en `appsettings.json`:

```json
"Fichaje": { "ModoUbicacion": "Libre", "PrecisionMaxMetros": 50 }
```

- `ModoUbicacion = "Libre"`: si el fichaje cae dentro de un sitio de la hoja `Sitios`,
  se anota su nombre en `Lugar`; si no, `Lugar = "Otro lugar"` y se exige un motivo
  (la API responde 400 con `codigo = "MOTIVO_REQUERIDO"` y la app lo pide).
- Cualquier otro valor: modo anterior, sede fija validada con `GeoValidator`
  (`Lugar = "Sede"`).

### Cambios manuales en la planilla de Google (no se automatizan)

**Hoja nueva `Sitios`** — encabezado en fila 1, datos desde fila 2:

| A: Nombre | B: Latitud | C: Longitud | D: RadioMetros |
|-----------|------------|-------------|----------------|

**Hoja `Fichajes`** — encabezados en fila 1, columnas A-J:

| A | B | C | D | E | F | G | H | I | J |
|---|---|---|---|---|---|---|---|---|---|
| Nombre | FechaHora | Lat | Lon | Tipo | Lugar | Motivo | Precisión (m) | Dirección | Mapa |

## Administración: usuarios, roles, bitácora y liquidación de horas

Portado de `ingenieria_software` (BE/DAL/BLL de usuarios, perfiles y permisos, y el Proceso 3 de liquidación de horas)
y adaptado a esta API: planilla de Google en lugar de SQL Server, token JWT en lugar de `SessionManager`.

### Pestañas nuevas (se crean solas al iniciar la API)
`Roles`, `Permisos`, `RolPermiso`, `UsuarioRol`, `Bitacora`, `UsuarioHistorial`, `Parametros`, `ValorHora`, `Feriados`,
`ExcedentesAprobados`, `Liquidaciones` y `LiquidacionDias`. Se puede apagar con `Planilla:AutoCrear = false`.
La API también siembra el catálogo de permisos, los roles iniciales y los parámetros de liquidación.

Cambios en pestañas existentes:
- **`Empleados`**: dos columnas nuevas, `G IntentosFallidos` y `H Bloqueado`. La columna `F` (opcional) solo sirve como
  **rol inicial**: `Administrador`, `Supervisor` o vacío (= Empleado). Después los roles se gestionan en `UsuarioRol`.
- **`Fichajes`**: columnas nuevas `K Corregido | L MotivoCorreccion | M CorregidoPor | N FechaCorreccion`.
  El identificador de un fichaje es su **número de fila**: no hay que borrar ni reordenar filas a mano.

### Primer administrador
Antes de la primera ejecución, poné `Administrador` en la columna `F` de tu fila de `Empleados`, o configurá
`Planilla:AdministradorInicial` con tu email: si no hay ningún administrador activo, la API te asigna el rol.

### Roles y permisos (patrón Composite)
`Rol` es la rama y `Permiso` la hoja (`Dominio/Seguridad`). **No hay herencia de permisos**: el padre de un rol es solo
organizativo. Catálogo: *Administrar usuarios*, *Gestión de roles*, *Ver bitácora*, *Cambiar PIN*, *Fichar asistencia*,
*Ver panel de supervisor*, *Corregir fichajes*, *Liquidar horas*. Roles iniciales: `Administrador` (protegido, todos),
`Supervisor` y `Empleado`. Los endpoints se protegen con `[RequierePermiso("...")]`; el token lleva los permisos.

Guardas (todas con test): no se elimina un rol protegido ni uno con usuarios, no se crean ciclos ni se mueve un rol a un
ancestro, y **anti-lockout**: ningún cambio puede dejar al sistema sin un usuario activo con *Administrar usuarios* y
*Gestión de roles*.

### Inicio de sesión
PIN de 4 dígitos con BCrypt (el original usaba SHA-256 sin sal). **3 PIN incorrectos bloquean** al usuario hasta que un
administrador lo desbloquee. Límite de 10 intentos por minuto por IP. Una baja o un bloqueo cortan el acceso en
menos de un minuto aunque el token siga vigente; un cambio de roles se ve en el próximo inicio de sesión.
Un usuario no se borra: se da de baja (sus fichajes y liquidaciones siguen siendo datos de pago).

### Auditoría
`Bitacora` registra login y cambios de seguridad; `UsuarioHistorial` guarda una foto del usuario en cada cambio
(solo inserción, sin el PIN) y permite **rollback**, que a su vez queda registrado y no se puede encadenar.

### Cálculo de horas para el pago (`Dominio/Liquidacion`)
Las jornadas se arman con los pares Entrada/Salida de `Fichajes` (un turno nocturno cuenta en el día que empezó).
Por empleado y día: horas fichadas → redondeo (`REDONDEO_MINUTOS`) → normales hasta `JORNADA_NORMAL_HORAS`, extra con
`RECARGO_EXTRA_PCT`, domingos y feriados con `RECARGO_FERIADO_PCT` → importe con el **valor hora vigente ese día**
(`ValorHora` guarda el historial). Sin turnos asignados (el caso de hoy) se paga lo fichado; con turnos se aplicarían la
tolerancia y la aprobación de excedentes del original.
Cerrar un período (solo períodos terminados, de hasta 62 días) exige que no haya fichajes incompletos ni empleados sin
valor hora, guarda una **foto** que ya no se recalcula y después se marca como pagada. Corregir un fichaje exige motivo,
no superpone jornadas, no toca períodos ya liquidados y deja quién, cuándo y el valor anterior. Hay exportación CSV
(`/api/admin/liquidacion/liquidaciones/{id}/csv`).

### Límites a tener en cuenta
- Google Sheets no tiene transacciones: las escrituras de varias filas se serializan con un lock **dentro de una sola
  instancia de la API**. Con varias instancias hace falta una base de datos.
- Los empleados se identifican por **nombre completo** en `Fichajes` (único): no se puede renombrar a quien ya fichó.
- Cada consulta lee hojas completas; sin caché. Con mucho volumen conviene una base de datos.

### Pruebas
```bash
dotnet test tests/FichajeApp.Api.Tests
```
Cubren el cálculo de horas, las reglas de roles y los servicios (con repositorios en memoria) y la API completa por HTTP.
**No** cubren la lectura y escritura reales contra Google Sheets.
