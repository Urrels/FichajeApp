# Publicar en Google Play, paso a paso

Cada cliente es **una app distinta** en Play Store (su propio nombre, ícono, colores, API y planilla).
Hay dos piezas que se publican por separado: la **API** (un servidor con HTTPS) y la **app Android** (un `.aab` en Play Console).

## 0. Orden recomendado

1. Servidor de la API con HTTPS → 2. conectar la planilla de Google desde la app → 3. identidad de la app (ID, ícono, URL)
→ 4. firmar → 5. cuenta de Play Console → 6. ficha de la tienda y formularios → 7. prueba cerrada → 8. producción.

## 1. La API tiene que estar en internet con HTTPS

La app de Play Store no puede hablar con `localhost` ni con `http://` (Android lo bloquea). Por cada cliente:

- Un servidor (VPS, Azure App Service, Railway, etc.) con .NET 8 y un dominio con certificado, por ejemplo `https://api-clientea.tudominio.com`.
- Publicar la API: `dotnet publish src/FichajeApp.Api -c Release -o publicar`.
- Variable de entorno `Jwt__Secret` distinta por cliente (32+ caracteres al azar). `Setup__Clave` (opcional): el código de activación.
- La carpeta `datos/` de la API (credencial cifrada, llaves y código de activación) tiene que **persistir** entre reinicios y
  despliegues. Se puede mover con `Setup__Carpeta`. Si se pierde, hay que volver a cargar la conexión desde la app.
- Una instancia por cliente (la caché está en memoria; con varias instancias hay que cambiarla).

## 2. Conectar la planilla de Google

### Una sola vez, la hace quien instala el servidor (vos): la cuenta de servicio
1. En <https://console.cloud.google.com> crear un proyecto y activar **Google Sheets API** y **Google Drive API** (APIs y servicios → Biblioteca → Habilitar). La de Drive se usa para que el servidor cree la planilla del cliente y se la comparta.
2. IAM y administración → **Cuentas de servicio** → crear una → pestaña **Claves** → *Agregar clave* → JSON.
3. Poner ese archivo en el servidor y apuntar `GoogleSheets__CredentialsPath` a él (por ejemplo `credentials/service-account.json`).
   Con una cuenta por cliente o una sola para todos: a Google le da igual, lo que separa a los clientes es cada planilla.
4. Mandale al cliente su **código de activación** por un medio privado (el cliente no tiene acceso al servidor). Es `Setup__Clave`, o la que imprime la API al arrancar y está en `datos/clave-instalacion.txt`).

### Lo que hace cada cliente (no necesita ningún archivo)
1. Abrir la app → en el login, **Conectar mi planilla de Google** (aparece sola si el servidor todavía no tiene planilla).
2. Crear una planilla de Google Sheets vacía y **compartirla como Editor** con el correo que muestra la pantalla (botón *Copiar correo*).
3. Pegar el link de la planilla, cargar los datos del primer administrador (nombre, email, PIN de 4 dígitos) y el código de activación.
4. **Probar y guardar**: la API comprueba el acceso, crea las pestañas y al administrador. Ya se puede iniciar sesión.

Si el servidor no tiene ninguna cuenta de servicio, la misma pantalla pide el archivo `.json` (se guarda cifrado en el servidor).
Si falla, el mensaje dice qué falta: planilla no compartida, API de Sheets sin activar, link incorrecto, etc.
## 3. Identidad de la app (por cliente)

- `src/FichajeApp.Mobile/Clientes/<cliente>/`: `cliente.json` (nombre, `apiBaseUrl` con tu `https://…/api`, colores),
  `appicon.svg` + `appiconfg.svg` (ícono del launcher: 1024×1024 conceptual, el primer plano en el 66 % central) y `logo.svg`.
- `FichajeApp.Mobile.csproj`: `ApplicationId` y `ApplicationTitle` de cada cliente. **El `ApplicationId` no necesita un dominio tuyo**:
  es solo un nombre único para tu app en todo Play Store (la costumbre es un dominio al revés, pero nadie lo verifica). Ya está
  puesto como `io.github.urrels.fichaje.<cliente>` (usa tu usuario de GitHub). Podés cambiarlo por otro, por ejemplo
  `ar.tunombre.fichaje.clientea`, **hasta la primera vez que subas la app a Play: después no se puede cambiar**. Reglas: solo
  letras minúsculas, números y guion bajo; dos o más partes separadas por puntos; cada parte empieza con una letra.
- `Platforms/Android/AndroidManifest.xml`: reemplazar `TU_API_KEY_DE_GOOGLE_MAPS` por una clave de Maps (ver abajo).

### Clave de Google Maps (para el mapa del supervisor)
1. En <https://console.cloud.google.com>, con el proyecto de tu cuenta de servicio: *APIs y servicios → Biblioteca → Maps SDK for Android → Habilitar*.
2. *Credenciales → Crear credenciales → Clave de API*.
3. Editá la clave → *Restricciones de la aplicación → Apps de Android* → agregá el `ApplicationId` y la huella **SHA-1**:
   - la de pruebas: `keytool -list -v -keystore "%USERPROFILE%\.android\debug.keystore" -alias androiddebugkey -storepass android -keypass android`
   - la de producción: Play Console → tu app → *Integridad de la app → Firma de la app* → "Certificado de firma de la clave de firma de la app" (SHA-1).
4. Restringila también a *Maps SDK for Android* y pegala en el manifest. La misma app usa una clave por cliente (una por `ApplicationId`).
5. La facturación de Google Cloud tiene que estar activa para que el mapa cargue (hay un uso gratuito mensual; revisá los precios vigentes).
- Los **colores, el nombre y el logo interno** después los puede cambiar el administrador desde la app (Administración → Apariencia).
  El **ícono del launcher** no: solo cambia con una versión nueva.

## 4. Firmar la app

Play exige que cada actualización venga firmada con la misma llave. Una llave por cliente:

```bash
keytool -genkeypair -v -keystore keystores/clientea.keystore -alias clientea -keyalg RSA -keysize 2048 -validity 10000
```

- Guardá el `.keystore` y las contraseñas en un lugar seguro y con copia (no se sube al repositorio: está en `.gitignore`).
- Al crear la app en Play Console aceptá **Play App Signing**: Google guarda la llave final y la tuya queda como *llave de subida*,
  que se puede reponer si la perdés.

## 5. Generar el .aab

Subir siempre `ApplicationVersion` (entero, +1 en cada subida) y `ApplicationDisplayVersion` (lo que ve el usuario) en el `.csproj`.

```bash
dotnet publish src/FichajeApp.Mobile -f net8.0-android -c Release -p:Cliente=clientea -p:AndroidSigningKeyPass=CLAVE -p:AndroidSigningStorePass=CLAVE
```

El `.aab` queda en `src/FichajeApp.Mobile/bin/Release/net8.0-android/publish/`. Probalo antes con un `.apk`
(`-p:AndroidPackageFormat=apk`) instalado en un teléfono real: fichar, mapa, huella y logo.

## 6. Cuenta de Play Console

1. <https://play.google.com/console> → pagar el registro único (USD 25) y verificar identidad.
2. Si la cuenta es **personal** (no de organización), Google exige una **prueba cerrada con al menos 12 testers durante 14 días
   seguidos** antes de poder pedir producción. Una cuenta de **organización** (con número D-U-N-S) no lo exige.
3. *Crear app* → idioma, nombre, "App", "Gratis".

## 7. Ficha de la tienda y formularios

- **Ficha**: nombre (30 caracteres), descripción corta (80) y larga, ícono 512×512, imagen destacada 1024×500,
  mínimo 2 capturas de teléfono.
- **Política de privacidad** (URL pública, obligatoria): tiene que decir que se usa la **ubicación precisa** al fichar,
  la **huella/PIN del teléfono** (solo para confirmar identidad, no se guarda) y qué datos se guardan (nombre, email,
  fichajes con hora y lugar) y dónde (la planilla del cliente). Hay generadores gratuitos; ajustalo a lo anterior.
- **Seguridad de los datos (Data safety)**: ubicación precisa, nombre y email; se recopilan y se comparten con el servidor
  del cliente; se cifran en tránsito (HTTPS); el usuario puede pedir que se borren.
- **Permisos de ubicación**: la app usa ubicación **solo en primer plano** (al tocar Fichar). No pidas ubicación en segundo plano:
  dispara una revisión extra.
- **Clasificación de contenido**: cuestionario (la app no tiene contenido sensible → apta para todos).
- **Público objetivo**: adultos (no es para niños). **Anuncios**: no. **Acceso a la app**: dar un usuario y PIN de prueba
  para que el revisor de Google pueda entrar (y que la API esté activa).
- **API objetivo**: Play exige apuntar a un nivel reciente (hoy Android 15 / API 35 para apps nuevas). Si el build avisa, subí
  `TargetFramework` a `net9.0-android` o el SDK correspondiente.

## 8. Subir y publicar

1. *Pruebas → Prueba cerrada* → crear versión → subir el `.aab` → agregar testers (lista de emails o grupo de Google).
2. Compartir el enlace de opt-in con los testers; esperar los 14 días si la cuenta es personal.
3. *Producción → Crear versión* → subir el mismo `.aab` (o uno nuevo) → enviar a revisión (tarda de horas a unos días).
4. Para cada actualización: subir `ApplicationVersion`, volver a generar el `.aab`, subirlo y enviar.

## 9. Después de publicar

- Los **cambios de apariencia** no requieren nueva versión (los hace el administrador).
- Los cambios de código sí: nueva versión y revisión.
- Respaldá el `.keystore`, las contraseñas y la carpeta `datos/` de la API.
- Cada cliente nuevo = repetir pasos 2 a 8 con su carpeta en `Clientes/`, su `ApplicationId` y su keystore.
