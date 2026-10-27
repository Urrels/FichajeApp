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
