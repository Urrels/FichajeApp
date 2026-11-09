# Poner la API en internet

La app de Play Store habla con la API por internet con **HTTPS**. Hace falta **una API por cliente** (cada una con su propio
`Jwt__Secret`, su código de activación y su carpeta `datos`). No necesitás un dominio propio.

> Los archivos de esta guía (`Dockerfile`, `docker-compose.yml`, `Caddyfile`) están escritos pero **no se probaron** en esta
> máquina (no tiene Docker). Probalos primero con un cliente de prueba antes de dárselos a uno real.

## Qué necesita cualquier servidor

| Variable | Para qué |
|---|---|
| `Jwt__Secret` | Clave que firma las sesiones: 32+ caracteres al azar, **distinta por cliente**. La API **se niega a arrancar en producción** con la de ejemplo. |
| `Setup__Clave` | Código de activación que le mandás al cliente (ver README). |
| `Fichaje__FotosRetencionDias` | Días que se guardan las fotos (90 por defecto, `0` = nunca se borran). |
| `Fichaje__PrecisionMaxMetros` | Valor **inicial** del error máximo del GPS que se acepta al fichar (50 m por defecto). El administrador lo cambia desde la app (Cómo se ficha → Precisión del GPS) y eso manda sobre este valor. Adentro de un edificio el GPS suele dar ±30 a ±150 m: para un cliente que ficha en un lugar cerrado conviene "Flexible" (100 m) o ubicación "Ninguna". |
| `Feriados__SincronizacionAutomatica` | `true` por defecto: una vez por día (y al arrancar) el servidor trae los feriados nacionales de la API pública ArgentinaDatos a la pestaña **Feriados**. Lo cargado a mano nunca se pisa. Si el servidor no tiene salida a internet, ponerlo en `false` (los feriados se cargan a mano). El administrador también puede forzarlo desde Liquidación → Configuración → Feriados → Actualizar. |
| `Proxy__Confiar=true` | Solo si hay un proxy delante (Caddy, Railway, Render…). Sin esto el límite de intentos de login vería una sola IP para todos. Ya viene activado en el `Dockerfile`. |
| `TZ` | Zona horaria del servidor. El `Dockerfile` ya trae `America/Argentina/Buenos_Aires`; si lo desplegás sin Docker, ponela vos. Sin esto un servidor en UTC guarda los fichajes 3 h corridos y los de la noche aparecen en el día siguiente (el supervisor no los ve en "Hoy"). |
| `Setup__Carpeta` | Carpeta persistente para `datos` (en el `Dockerfile`: `/datos`). |

**Lo más importante:** la carpeta `datos` guarda la credencial cifrada, las llaves, el código de activación y **las fotos**.
Tiene que ser un **volumen persistente** y tener **copia de seguridad**. Si se pierde, el cliente tiene que volver a conectar su planilla.

Para generar una clave al azar (PowerShell):

```powershell
-join ((48..57)+(65..90)+(97..122) | Get-Random -Count 40 | ForEach-Object {[char]$_})
```

## Dónde se guardan los datos: base SQL (recomendado) o planilla de Google

Si definís la variable `ConnectionStrings__Fichaje`, la API guarda todo en **SQL Server / Azure SQL** y **no usa Google Sheets**.
Sin esa variable sigue funcionando con la planilla, como antes.

| Variable (solo con base SQL) | Para qué |
|---|---|
| `ConnectionStrings__Fichaje` | Cadena de conexión. Azure SQL: `Server=tcp:SERVIDOR.database.windows.net,1433;Database=NOMBRE;User ID=USUARIO;Password=CLAVE;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;` |
| `Setup__AdminNombre`, `Setup__AdminEmail`, `Setup__AdminPin` | El **primer administrador**. Se crea solo la primera vez, si todavía no hay ningún empleado (PIN de 4 dígitos). Después se pueden sacar. |
| `Datos__Migrar` | `true` por defecto: al arrancar crea o actualiza las tablas (migraciones de Entity Framework). Con `false` hay que aplicarlas a mano. |

- Al arrancar la API crea las tablas, siembra los permisos y roles y crea el primer administrador. No hay que correr scripts.
- Azure SQL *serverless* se pausa solo cuando no se usa: la primera consulta después de un rato puede tardar unos segundos.
  La API reintenta la conexión sola (hasta ~1 minuto), pero el primer fichaje del día puede sentirse lento.
- Con base SQL ya no existen la clave de activación ni la pantalla "Conectar mi planilla": la app entra directo al login.
- **Lugares conocidos (tabla `Sitios`)**: se cargan desde la app, en Administración → Lugares conocidos (hace falta el permiso
  «Configurar fichaje»). Lo más cómodo es agregar cada lugar estando ahí: la app toma la ubicación. Un cambio se ve en el
  próximo fichaje. Con la planilla de Google se siguen cargando en la pestaña «Sitios».
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
4. Esperá a que despliegue y abrí `https://TU-DIRECCION/api/configuracion/estado`: tiene que responder `{"configurada":false,...}`.
5. Esa dirección más `/api` es la `apiBaseUrl` del cliente (`Clientes/<cliente>/cliente.json`).

Como en estos servicios es incómodo subir un archivo, **no pongas el `.json` de Google en el servidor**: cargalo desde la app
(la pantalla de conexión lo pide cuando el servidor no tiene credencial; se guarda cifrado en `/datos`).

Los precios cambian: revisalos en el sitio de cada servicio. Para 22 empleados alcanza con el plan más chico.

## Opción B: un servidor propio (VPS) con Docker y Caddy

Sirve cualquier VPS con Ubuntu y Docker. Caddy consigue y renueva solo el certificado HTTPS.

1. **Una dirección**: si no tenés dominio, creá un nombre gratis en <https://www.duckdns.org> (por ejemplo
   `fichaje-sol.duckdns.org`) apuntando a la IP del servidor.
2. Abrí los puertos 80 y 443 del servidor.
3. En el servidor: `git clone` del repositorio y `cd FichajeApp`.
4. `cp .env.example .env` y completá `DOMINIO`, `JWT_SECRET` y `CODIGO_ACTIVACION`.
5. `docker compose up -d --build`
6. Abrí `https://TU-DOMINIO/api/configuracion/estado`.

El volumen `datos` queda en Docker: respaldalo con `docker run --rm -v fichajeapp_datos:/d -v $PWD:/b alpine tar czf /b/datos.tgz -C /d .`
(el nombre del volumen lleva el nombre de la carpeta; verificalo con `docker volume ls`).

## Antes de dárselo al cliente

- [ ] `https://TU-DIRECCION/api/configuracion/estado` responde.
- [ ] Probé conectar una planilla de prueba desde la app y entrar con el administrador.
- [ ] Reinicié el servidor y siguió conectada (el volumen persiste).
- [ ] Anoté el `Jwt__Secret`, el código de activación y dónde está la copia de seguridad.
- [ ] El `apiBaseUrl` del cliente es `https://…/api` (con `s`: Android bloquea `http` fuera del emulador).

> Ojo: `dotnet publish` copia `credentials/service-account.json` a la carpeta de salida. No subas esa carpeta a ningún lado.
> La imagen de Docker **no** la incluye (`.dockerignore`).

## Seguridad del inicio de sesión

- 3 PIN incorrectos frenan la cuenta **15 minutos** (se libera sola; no queda marcada en la base). Un administrador puede seguir bloqueando una cuenta a mano.
- El mensaje de error es el mismo exista o no el mail, y el servidor tarda lo mismo en responder.
- Los PIN obvios (0000, 1234, 4321, 2580...) se rechazan al crear o cambiar un PIN.
- La sesión de la cuenta de sucursal (kiosco) dura 14 días. Si se pierde el teléfono, dar de baja la cuenta corta la sesión enseguida.
- Los pedidos tienen un tope de 1 MB y todas las respuestas llevan cabeceras de seguridad (HSTS fuera de desarrollo).
