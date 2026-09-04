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
