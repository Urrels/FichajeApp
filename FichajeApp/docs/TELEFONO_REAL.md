# Probar en un teléfono Android real

Hacen falta tres cosas: **el teléfono preparado**, **la app instalada** y **que el teléfono llegue a la API**.

## 1. Preparar el teléfono (una sola vez)

1. Ajustes → Acerca del teléfono → tocá **Número de compilación** 7 veces (aparece "ahora sos desarrollador").
2. Ajustes → Sistema → **Opciones de desarrollador** → activá **Depuración por USB**.
3. Conectalo a la PC con un cable de datos (no uno solo de carga). En el teléfono aceptá "¿Permitir la depuración por USB?".

En Visual Studio, la lista de dispositivos (arriba, donde dice el emulador) debe mostrar el teléfono. Si no aparece: probá otro cable
o puerto, y en Windows instalá el controlador USB de la marca del teléfono.

## 2. Que el teléfono llegue a la API

El emulador usa `10.0.2.2`, que **en un teléfono real no existe**. Y Android bloquea `http://` salvo hacia `10.0.2.2`,
así que en el teléfono la API tiene que tener **HTTPS**. La forma más rápida, sin tocar el servidor ni el firewall, es un túnel:

1. Instalá **cloudflared** (Cloudflare Tunnel, gratis, sin cuenta): <https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/>
2. Con la API corriendo en tu PC (perfil `FichajeApp.Api`, puerto 5224), en otra terminal:

   ```powershell
   cloudflared tunnel --url http://localhost:5224
   ```
3. Va a imprimir una dirección como `https://algo-raro.trycloudflare.com`. Cambia cada vez que lo abrís.
4. Abrila en el navegador del teléfono con `/api/configuracion/estado` al final: tiene que mostrar `{"configurada":...}`.
5. Poné esa dirección en `src/FichajeApp.Mobile/Clientes/dev/cliente.json`:

   ```json
   "apiBaseUrl": "https://algo-raro.trycloudflare.com/api"
   ```
   **No subas este cambio a git** (`git checkout Clientes/dev/cliente.json` para deshacerlo al terminar).

(Alternativa sin túnel: usar la IP de la PC en la red wifi, por ejemplo `http://192.168.1.20:5224`. Para eso hay que permitir ese
tráfico en `Platforms/Android/Resources/xml/network_security_config.xml`, abrir el puerto en el firewall de Windows y arrancar la API
con `--urls http://0.0.0.0:5224`. Es más trabajo y toca un archivo protegido; por eso no la recomiendo.)

## 3. Instalar la app

**Desde Visual Studio:** elegí el teléfono en la lista de dispositivos y tocá ▶ (Depurar). Instala y abre la app sola.

**Sin Visual Studio (para dársela a otra persona):**

```powershell
dotnet publish src/FichajeApp.Mobile -f net8.0-android -c Release -p:Cliente=dev -p:AndroidPackageFormat=apk -p:AndroidKeyStore=False
```

El `.apk` queda en `src/FichajeApp.Mobile/bin/Release/net8.0-android/publish/`. Se puede mandar por WhatsApp o mail, o instalar con
`adb install archivo.apk`. En el teléfono hay que permitir "instalar apps de orígenes desconocidos" para ese programa.

## 4. Qué probar en el teléfono (y no se puede en el emulador)

- La **cámara real** (foto de fichaje) y que no se trabe al volver a la app.
- El **GPS real** al aire libre: el mensaje de precisión y el fichaje con motivo en un lugar nuevo.
- La **huella** (fichaje personal).
- Rotar la pantalla y cerrar y abrir la app: la sesión tiene que seguir.
- Con el teléfono en modo avión: tiene que decir que no hay conexión, sin cerrarse.

## Si algo falla

- **"Error de conexión" al entrar**: la dirección del túnel cambió (se actualiza al reiniciar cloudflared) o la API no está corriendo.
- **Pantalla blanca o se cierra al abrir**: desinstalá la app del teléfono y volvé a instalarla; si sigue, mirá el log con
  `adb logcat | findstr /i "fichaje exception"`.
- **No aparece la cámara**: aceptá el permiso en Ajustes → Apps → Fichaje → Permisos.
