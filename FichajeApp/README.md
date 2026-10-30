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

### Pantallas de administración en la app
El menú lateral muestra **Supervisor** y **Administración** solo a quien tiene los permisos (la app consulta
`SessionManager.TienePermiso`, nunca el nombre del rol). Administración abre, según el permiso: **Usuarios** (alta, edición,
roles, restablecer PIN, desbloquear, baja, historial con rollback), **Roles y permisos** (árbol, permisos por rol, subroles,
duplicar, mover, eliminar), **Bitácora**, **Corregir fichajes** (entradas sin salida, salidas sin entrada y carga manual) y
**Liquidación de horas** (calcular, revisar por empleado, aprobar excedentes, cerrar el período, marcar como pagado,
compartir el CSV, y configurar parámetros, valor hora y feriados). Cada usuario puede **cambiar su PIN** y **cerrar sesión**.
Las acciones se hacen con diálogos del sistema: es la versión mínima, sin pantallas de formulario por cada operación.

### Apariencia editable por el administrador

Con el permiso **Gestionar apariencia** (el rol Administrador lo recibe solo al reiniciar la API), Administración →
**Apariencia** permite cambiar el nombre, los tres colores (principal, texto sobre el principal y acento) y el logo,
con vista previa. Se guarda en la pestaña `Apariencia` de la planilla (se crea sola) y todos los usuarios lo ven al abrir
la app (`GET /api/apariencia`, público; `PUT /api/admin/apariencia`, con permiso). Lo guardado pisa lo que trae
`cliente.json`, y "Volver a los originales" lo borra. Se valida el formato `#RRGGBB`, un contraste mínimo del texto
sobre el color principal, y el logo (PNG/JPG, hasta 150 KB; la app lo achica a 256 px). **El ícono del launcher no
se puede cambiar en runtime**: lo fija Android al instalar, así que solo cambia con un nuevo build del cliente.

### Cómo se ficha: ubicación y foto (por cliente)

Cada cliente elige, desde Administración → **Cómo se ficha** (permiso *Configurar fichaje*, lo recibe el rol Administrador),
qué se pide al fichar. Se guarda en la pestaña `ConfigFichaje` de su planilla y vale para todos los teléfonos al próximo fichaje:

| Opción | Valores |
|---|---|
| Ubicación | `Ninguna` (no se pide ni se guarda) · `Libre` (se registra dónde ficha, con motivo si es un lugar nuevo) · `SedeFija` |
| Foto | `No` · `Opcional` · `Obligatoria` (sin foto no se ficha) |

Mientras nadie lo cambie rige lo de siempre: `Fichaje:ModoUbicacion` de appsettings y sin foto.

**Las fotos se guardan en el servidor** (`datos/fotos/aaaa/mm/dd/<id>.jpg`, fuera de git); en la planilla, la hoja `Fichajes`
guarda solo el nombre del archivo en la columna `O`. La app la achica a 640 px (~100 KB) y la API solo acepta PNG/JPG de hasta 400 KB.
El supervisor ve la foto tocando el fichaje en su panel (`GET /api/supervisor/foto`, con permiso de supervisor).
Detrás de `IAlmacenFotos`, así que pasar a Amazon S3 u otro almacenamiento es cambiar una clase. **Hay que hacer copia de seguridad
de la carpeta `datos/`** y avisar a los empleados que se les saca una foto (es un dato personal).
**Retención y consentimiento.** Las fotos se borran solas pasado `Fichaje:FotosRetencionDias` días (90 por defecto; `0` = no borrar nunca): un proceso en segundo plano lo revisa al arrancar la API y una vez por día. El supervisor que abre una foto ya borrada ve un aviso. La primera vez que cada empleado ficha con foto en un teléfono, la app le muestra qué se hace con su foto, quién la ve y cuánto se guarda, y le pide que esté de acuerdo; si no acepta no se le saca la foto (con foto obligatoria, no ficha). Es un aviso informativo: el consentimiento que exija tu legislación laboral y de datos personales es responsabilidad de la empresa.

### Modo kiosco: un teléfono compartido por sucursal (opcional)

Para empresas donde varios empleados fichan desde el mismo teléfono (la huella no sirve: identifica al dueño del teléfono).
**Viene apagado**: por defecto cada persona ficha con su propio usuario, y el rol de las cuentas de sucursal ni siquiera existe.

1. El administrador entra a Administración → **Cómo se ficha** → **Modo** → **Kiosco** y guarda. En ese momento se crea el
   rol **Kiosco** (permiso *Fichar en kiosco*). Volver a **Personal** apaga el kiosco; el rol y las cuentas ya creadas quedan.
2. En Administración → Usuarios, crear una cuenta por sucursal (por ejemplo "Sucursal Centro", con un email y PIN propios)
   y asignarle el rol **Kiosco**.
3. Iniciar sesión **una vez** en el teléfono de esa sucursal con esa cuenta. La sesión dura 30 días y se guarda en el teléfono;
   la baja o el bloqueo de la cuenta la cortan igual.
4. El teléfono muestra la lista de empleados con lo que les toca (Entrada o Salida). Cada uno toca su nombre, escribe su PIN
   personal y, si el cliente pidió foto, se saca la selfie. El fichaje queda **a nombre del empleado** y con **el nombre de la
   sucursal como lugar** (sin ubicación GPS).

El PIN tiene el mismo control que el login: 3 errores bloquean al empleado hasta que un administrador lo desbloquee. Un PIN
incorrecto responde 400 (no 401) para no cerrar la sesión del teléfono. La cuenta de la sucursal no ficha por sí misma, y el
Administrador (que tiene todos los permisos, incluido el del kiosco) **no** se trata como cuenta de sucursal: ve su panel.
Con el modo en Personal, los endpoints del kiosco responden 409 con un mensaje que explica cómo activarlo.
### Conectar Google Sheets desde la app

Cada cliente conecta su propia planilla sin tocar archivos del servidor: en el login, **Conectar mi planilla de Google**
(aparece sola si el servidor todavía no está configurado). Se carga el link de la planilla, los datos del primer administrador y la **código de activación**; la cuenta de servicio (`.json`) la pone una vez quien instala el servidor (`GoogleSheets:CredentialsPath`) y la app muestra el correo con el que hay que compartir la planilla (si el servidor no tiene ninguna, se carga desde la app). La API (`POST /api/configuracion/google`) prueba el
acceso contra Google, guarda la credencial **cifrada en el servidor** (`datos/`, fuera de git) y crea las pestañas.
El código de activación (que quien instala el servidor le manda al cliente) es `Setup:Clave` o, si no se definió, una que la API genera y escribe en el log y en
`datos/clave-instalacion.txt`. Lo que haya en `appsettings` (`GoogleSheets:*`) sigue funcionando como alternativa.
La guía completa para publicar en Play Store está en [docs/PLAY_STORE.md](docs/PLAY_STORE.md).

## Varios clientes (una API por cliente)

Cada cliente tiene su propia instancia de la API y su propia planilla. Por instancia se
configuran por variables de entorno: `GoogleSheets__SpreadsheetId`,
`GoogleSheets__CredentialsPath`, `Jwt__Secret` (distinto en cada una),
`Fichaje__ModoUbicacion`, `Fichaje__PrecisionMaxMetros`, `Fichaje__CacheMinutos`.

La app móvil se compila **una vez por cliente** (cada una con su ícono, nombre, colores y URL
de API). Cada cliente tiene una carpeta `src/FichajeApp.Mobile/Clientes/<cliente>/` con
`cliente.json`, `appicon.svg`, `appiconfg.svg` y `logo.svg`. Los colores del tema
(`Resources/Styles/Tema.xaml`) se pisan al iniciar con los de `cliente.json`.
Las propiedades del `.csproj` y los comandos de build están en
`FichajeApp.Mobile.Clientes.csproj.snippet.txt`:

```bash
dotnet publish -f net8.0-android -c Release -p:Cliente=clientea
```

Para sumar un cliente: copiar una carpeta de `Clientes/`, editar su `cliente.json` y
reemplazar los SVG (los actuales son placeholders), y agregar su bloque al `.csproj`.

## Guías

- [Publicar en Google Play, paso a paso](docs/PLAY_STORE.md)
- [Poner la API en internet (Docker, HTTPS, sin dominio propio)](docs/SERVIDOR.md)
- [Probar en un teléfono Android real](docs/TELEFONO_REAL.md)
- [Prueba completa de punta a punta (lista para tildar)](docs/PRUEBA_COMPLETA.md)
- [Plantilla de política de privacidad y datos para Play Console](docs/POLITICA_PRIVACIDAD_PLANTILLA.md)
