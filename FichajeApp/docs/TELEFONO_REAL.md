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
3. Va a imprimir una dirección como `https://algo-raro.trycloudflare.com`. **Cambia cada vez que lo abrís.**
4. Abrila en el navegador del teléfono con `/api/configuracion/estado` al final: tiene que mostrar `{"configurada":...}`.

### Decirle a la app cuál es la dirección (sin recompilar)

En la pantalla de **inicio de sesión**, tocá **5 veces seguidas el logo** (en 3 segundos). Se abre "Dirección del servidor":

- Pegá la dirección del túnel (`https://algo-raro.trycloudflare.com`; el `/api` se agrega solo).
- La app comprueba que el servidor responde y te lo dice. Se guarda en el teléfono; cada vez que el túnel cambie, repetí esto.
- Mientras uses otra dirección, el login muestra abajo "Servidor: …".
- Para volver a la original del APK: mismo gesto y dejala **vacía**.

Esto permite usar **el mismo APK** con cualquier servidor (el de pruebas, el de cada cliente). La dirección que trae el APK
es la de `Clientes/<cliente>/cliente.json` y sigue siendo la de arranque.

(Alternativa sin túnel: usar la IP de la PC en la red wifi, por ejemplo `http://192.168.1.20:5224`. Android la bloquea salvo que
se permita en `Platforms/Android/Resources/xml/network_security_config.xml`, y además hay que abrir el puerto en el firewall.
Es más trabajo y toca un archivo protegido; por eso no la recomiendo.)

## 3. Instalar la app

**Con un archivo APK (lo más cómodo, sin cable ni Visual Studio):**

```powershell
dotnet publish src/FichajeApp.Mobile -f net8.0-android -c Release -p:Cliente=dev -p:AndroidPackageFormat=apk -p:AndroidKeyStore=False
```

Tarda varios minutos. El archivo que hay que instalar queda en
`src/FichajeApp.Mobile/bin/Release/net8.0-android/publish/io.github.urrels.fichaje.dev-Signed.apk` (unos 38 MB).
Pasalo al teléfono (WhatsApp como documento, Drive, o cable), tocalo, permití "instalar apps de orígenes desconocidos"
para ese programa y, si Play Protect avisa que la app no está verificada, elegí "Instalar de todos modos".
Después cambiá la dirección del servidor con el gesto del logo (paso 2).

**Desde Visual Studio:** elegí el teléfono en la lista de dispositivos y tocá ▶ (Depurar). Instala y abre la app sola.
(Para esta vía también podés usar el gesto del logo; no hace falta tocar `cliente.json`.)
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
