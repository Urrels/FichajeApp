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
| `Proxy__Confiar=true` | Solo si hay un proxy delante (Caddy, Railway, Render…). Sin esto el límite de intentos de login vería una sola IP para todos. Ya viene activado en el `Dockerfile`. |
| `Setup__Carpeta` | Carpeta persistente para `datos` (en el `Dockerfile`: `/datos`). |

**Lo más importante:** la carpeta `datos` guarda la credencial cifrada, las llaves, el código de activación y **las fotos**.
Tiene que ser un **volumen persistente** y tener **copia de seguridad**. Si se pierde, el cliente tiene que volver a conectar su planilla.

Para generar una clave al azar (PowerShell):

```powershell
-join ((48..57)+(65..90)+(97..122) | Get-Random -Count 40 | ForEach-Object {[char]$_})
```

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
