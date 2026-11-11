# Poner la API en internet

La app de Play Store habla con la API por internet con **HTTPS**. Hace falta **una API por cliente** (cada una con su propio
`Jwt__Secret`, su base de datos y su carpeta `datos`). No necesitás un dominio propio.

> Los archivos de esta guía (`Dockerfile`, `docker-compose.yml`, `Caddyfile`) están escritos pero **no se probaron** en esta
> máquina (no tiene Docker). Probalos primero con un cliente de prueba antes de dárselos a uno real.

## Qué necesita cualquier servidor

| Variable | Para qué |
|---|---|
| `Jwt__Secret` | Clave que firma las sesiones: 32+ caracteres al azar, **distinta por cliente**. La API **se niega a arrancar en producción** con la de ejemplo. |
| `ConnectionStrings__Fichaje` | **Obligatoria.** La cadena de conexión de la base SQL (ver abajo). Sin ella la API se niega a arrancar. |
| `Fichaje__FotosRetencionDias` | Días que se guardan las fotos (90 por defecto, `0` = nunca se borran). |
| `Fichaje__PrecisionMaxMetros` | Valor **inicial** del error máximo del GPS que se acepta al fichar (50 m por defecto). El administrador lo cambia desde la app (Cómo se ficha → Precisión del GPS) y eso manda sobre este valor. Adentro de un edificio el GPS suele dar ±30 a ±150 m: para un cliente que ficha en un lugar cerrado conviene "Flexible" (100 m) o ubicación "Ninguna". |
| `Feriados__SincronizacionAutomatica` | `true` por defecto: una vez por día (y al arrancar) el servidor trae los feriados nacionales de la API pública ArgentinaDatos a la pestaña **Feriados**. Lo cargado a mano nunca se pisa. Si el servidor no tiene salida a internet, ponerlo en `false` (los feriados se cargan a mano). El administrador también puede forzarlo desde Liquidación → Configuración → Feriados → Actualizar. |
| `Proxy__Confiar=true` | Solo si hay un proxy delante (Caddy, Railway, Render…). Sin esto el límite de intentos de login vería una sola IP para todos. Ya viene activado en el `Dockerfile`. |
| `TZ` | Zona horaria del servidor. El `Dockerfile` ya trae `America/Argentina/Buenos_Aires`; si lo desplegás sin Docker, ponela vos. Sin esto un servidor en UTC guarda los fichajes 3 h corridos y los de la noche aparecen en el día siguiente (el supervisor no los ve en "Hoy"). |
| `Setup__Carpeta` | Carpeta persistente para `datos` (en el `Dockerfile`: `/datos`). |

**Lo más importante:** la carpeta `datos` guarda **las fotos** (de fichajes y de perfil).
Tiene que ser un **volumen persistente** y tener **copia de seguridad**. Si se pierde, se pierden las fotos (los fichajes están en la base).

Para generar una clave al azar (PowerShell):

```powershell
-join ((48..57)+(65..90)+(97..122) | Get-Random -Count 40 | ForEach-Object {[char]$_})
```

## Dónde se guardan los datos: base SQL

La API guarda todo en **SQL Server / Azure SQL**. Ya no se puede usar una planilla de Google: esa opción se eliminó.

| Variable | Para qué |
|---|---|
| `ConnectionStrings__Fichaje` | Cadena de conexión. Azure SQL: `Server=tcp:SERVIDOR.database.windows.net,1433;Database=NOMBRE;User ID=USUARIO;Password=CLAVE;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;` |
| `Setup__AdminNombre`, `Setup__AdminEmail`, `Setup__AdminPin` | El **primer administrador**. Se crea solo la primera vez, si todavía no hay ningún empleado (PIN de 4 dígitos). Después se pueden sacar. |
| `Datos__Migrar` | `true` por defecto: al arrancar crea o actualiza las tablas (migraciones de Entity Framework). Con `false` hay que aplicarlas a mano. |

- Al arrancar la API crea las tablas, siembra los permisos y roles y crea el primer administrador. No hay que correr scripts.
- Azure SQL *serverless* se pausa solo cuando no se usa: la primera consulta después de un rato puede tardar unos segundos.
  La API reintenta la conexión sola (hasta ~1 minuto), pero el primer fichaje del día puede sentirse lento.
- La app entra directo al login: no hay clave de activación ni pantalla para conectar nada.
- **Lugares conocidos (tabla `Sitios`)**: se cargan desde la app, en Administración → Lugares conocidos (hace falta el permiso
  «Configurar fichaje»). Lo más cómodo es agregar cada lugar estando ahí: la app toma la ubicación. Un cambio se ve en el
  próximo fichaje.
- **Jerarquía**: Administrador → Supervisores → Empleados. En Administración → Usuarios → *Supervisor a cargo* se elige quién tiene
  a cargo a cada empleado. El supervisor solo ve, corrige y recibe avisos de los suyos; si un empleado no tiene supervisor (o el
  suyo se dio de baja), los avisos van al administrador.
- **Copia de seguridad**: en Azure SQL son automáticas (restauración a un momento dado, 7 días por defecto). Probá restaurar una vez.
- Las **fotos** (de fichajes y de perfil) y las llaves de seguridad siguen en la carpeta `datos`, no en la base: necesitan disco persistente.
- Para probar en tu máquina sin instalar nada: LocalDB (viene con Visual Studio):
  `ConnectionStrings__Fichaje=Server=(localdb)\MSSQLLocalDB;Database=Fichaje;Trusted_Connection=True;TrustServerCertificate=True`

### Azure App Service (Linux, .NET 8)

Configuración → Variables de entorno de la aplicación web: `Jwt__Secret`, `ConnectionStrings__Fichaje`, `Setup__Admin*`,
`Proxy__Confiar=true`, `TZ=America/Argentina/Buenos_Aires` y `Setup__Carpeta=/home/datos` (`/home` es lo único que persiste entre
reinicios). Con el plan Gratuito F1 la app se duerme y tiene un tope diario de CPU: para la prueba con testers conviene B1.

## Opción A: servicio en la nube con HTTPS incluido (la más simple, sin dominio)

Sirven Railway, Render, Fly.io y similares: les das el repositorio de GitHub, detectan el `Dockerfile` y te dan una dirección
`https://algo.up.railway.app` con certificado.

1. Creá una cuenta y un proyecto nuevo desde el repositorio de GitHub (carpeta raíz: `FichajeApp`, usa el `Dockerfile`).
2. Cargá las variables de la tabla de arriba.
3. **Agregá un volumen persistente** montado en `/datos` (si no, cada despliegue borra todo).
4. Esperá a que despliegue y abrí `https://TU-DIRECCION/api/configuracion/estado`: tiene que responder `{"configurada":true,...}`.
5. Esa dirección más `/api` es la `apiBaseUrl` del cliente (`Clientes/<cliente>/cliente.json`).

Los precios cambian: revisalos en el sitio de cada servicio. Para 22 empleados alcanza con el plan más chico.

## Opción B: un servidor propio (VPS) con Docker y Caddy

Sirve cualquier VPS con Ubuntu y Docker. Caddy consigue y renueva solo el certificado HTTPS.

1. **Una dirección**: si no tenés dominio, creá un nombre gratis en <https://www.duckdns.org> (por ejemplo
   `fichaje-sol.duckdns.org`) apuntando a la IP del servidor.
2. Abrí los puertos 80 y 443 del servidor.
3. En el servidor: `git clone` del repositorio y `cd FichajeApp`.
4. `cp .env.example .env` y completá `DOMINIO`, `JWT_SECRET` y `CONEXION_SQL` (la cadena de conexión de la base).
5. `docker compose up -d --build`
6. Abrí `https://TU-DOMINIO/api/configuracion/estado`.

El volumen `datos` queda en Docker: respaldalo con `docker run --rm -v fichajeapp_datos:/d -v $PWD:/b alpine tar czf /b/datos.tgz -C /d .`
(el nombre del volumen lleva el nombre de la carpeta; verificalo con `docker volume ls`).

## Antes de dárselo al cliente

- [ ] `https://TU-DIRECCION/api/configuracion/estado` responde.
- [ ] Entré con el administrador (`Setup__Admin*`) y todo carga.
- [ ] Reinicié el servidor y las fotos siguieron ahí (el volumen persiste).
- [ ] Anoté el `Jwt__Secret`, la cadena de conexión y dónde está la copia de seguridad.
- [ ] El `apiBaseUrl` del cliente es `https://…/api` (con `s`: Android bloquea `http` fuera del emulador).

## Seguridad del inicio de sesión

- 3 PIN incorrectos frenan la cuenta **15 minutos** (se libera sola; no queda marcada en la base). Un administrador puede seguir bloqueando una cuenta a mano.
- El mensaje de error es el mismo exista o no el mail, y el servidor tarda lo mismo en responder.
- Los PIN obvios (0000, 1234, 4321, 2580...) se rechazan al crear o cambiar un PIN.
- La sesión de la cuenta de sucursal (kiosco) dura 14 días. Si se pierde el teléfono, dar de baja la cuenta corta la sesión enseguida.
- Los pedidos tienen un tope de 1 MB y todas las respuestas llevan cabeceras de seguridad (HSTS fuera de desarrollo).

## Mails (bienvenida y "Olvidé mi PIN")

La API manda dos mails: la **bienvenida** al dar de alta a alguien (sin el PIN, por seguridad) y el **código de 6 dígitos** para cambiar el PIN
desde "Olvidé mi PIN" en el login. Hace falta un servidor SMTP. **Sin configurarlo la app funciona igual**, solo que no salen mails y
"Olvidé mi PIN" avisa que hay que pedirle al administrador que restablezca el PIN.

Variables de entorno (en Azure: App Service → Configuración → Variables de entorno):

| Variable | Valor |
|---|---|
| `Correo__Host` | Servidor SMTP, por ejemplo `smtp.gmail.com` |
| `Correo__Puerto` | `587` (STARTTLS, por defecto) o `465` (TLS directo) |
| `Correo__Usuario` | Cuenta que envía, por ejemplo la casilla de Gmail |
| `Correo__Clave` | Contraseña de aplicación (ver abajo). **Nunca** la contraseña normal de la cuenta |
| `Correo__Remitente` | Opcional: la dirección que se muestra como remitente (por defecto, el usuario) |
| `Correo__NombreRemitente` | Opcional: nombre que se muestra (por defecto `HoraTrack`) |
| `Correo__NombreApp` | Opcional: nombre de la app en los textos de los mails (por defecto `HoraTrack`) |

**Con Gmail:** la cuenta necesita verificación en dos pasos; después se crea una *contraseña de aplicación* en
`myaccount.google.com/apppasswords` y esa es la que va en `Correo__Clave`. Gmail permite unos 500 mails por día, de sobra para este uso.
Otros servicios (Brevo, Mailjet, el correo del hosting) también sirven: cambian solo el host, el puerto y las credenciales.

Después de guardar las variables hay que reiniciar el App Service. Los códigos de recuperación viven en memoria del servidor: si se reinicia,
hay que pedir uno nuevo. Cada código vale 15 minutos, se puede pedir uno por minuto y se bloquea a los 5 intentos fallidos.

## Notificaciones dentro de la app

El menú lateral tiene **Notificaciones** (con el número de avisos sin leer). Hoy se generan al darte de alta, cuando cambia tu PIN y cuando
cambia tu horario. Se guardan en la base SQL. No son notificaciones "push": se ven al abrir la app.

## Notificaciones push (Firebase)

Cada aviso del centro de notificaciones (bienvenida, cambio de PIN, cambio de horario) también llega como **push** al teléfono, aunque la app esté cerrada.
Además, si la empresa activó el recorrido y el registro se cortó con la jornada abierta (por ejemplo, se reinició el teléfono), se le manda un push
«Retomá el registro de tu recorrido». Usa **Firebase Cloud Messaging** (gratis). Sin configurarlo, la app funciona igual pero sin push.

1. En `console.firebase.google.com` crear un proyecto (se puede desactivar Google Analytics).
2. Dentro del proyecto: **Agregar app → Android**, con el nombre de paquete **`io.github.urrels.fichaje`**. Descargar **`google-services.json`** y dejarlo en
   `src/FichajeApp.Mobile/` (no va al repositorio). Hay que armar el AAB **después** de dejarlo ahí.
3. En **Configuración del proyecto → Cuentas de servicio → Generar nueva clave privada**: baja un archivo JSON. **Es una clave secreta**: no se sube a GitHub ni se pega en chats.
4. En Azure (App Service → Variables de entorno) crear `Push__CredencialesJson` y pegar como valor **todo el contenido** de ese JSON. Reiniciar el App Service.
5. Probar: iniciar sesión en la app (acepta el permiso de notificaciones), y desde Administración cambiar el horario de esa persona: debería llegar el push.

Cada teléfono registra su token al iniciar sesión y lo quita al cerrarla. Los tokens que Firebase informa como inválidos (app desinstalada) se borran solos.
