# Ficha de Play Store: textos listos para pegar

Pensado para el cliente que se publique primero. Reemplazá lo que está entre `[CORCHETES]`. Los textos están en español (Argentina).
Límites de Play: nombre 30 caracteres, descripción corta 80, descripción larga 4000, novedades 500.

## Antes de subir: bloqueos y datos que faltan

| Qué | Estado |
|---|---|
| Apuntar a Android 16 (API 36) | **Hecho**: migrado a .NET 10 (ver `PLAY_STORE.md`, sección 7 bis). Falta probarlo en Android 15/16 |
| Cliente real en `Clientes/<cliente>/` con `apiBaseUrl` HTTPS | Hay `clientea` y `clienteb` de ejemplo (`https://api-clientea.example.com/api`) |
| `ApplicationId` definitivo | Hoy `io.github.urrels.fichaje.<cliente>`; no se puede cambiar después de la primera subida |
| Keystore del cliente y copia de resguardo | **Lo genera quien publica** (`PLAY_STORE.md`, sección 4); no está en el repositorio |
| Política de privacidad en una URL pública | Plantilla en `POLITICA_PRIVACIDAD_PLANTILLA.md`, sin publicar |
| Cuenta de Play Console (USD 25) y verificación de identidad | Pendiente |
| Prueba cerrada con 12 testers durante 14 días (cuenta personal) | Pendiente |
| Capturas, ícono 512×512 e imagen destacada 1024×500 | Pendiente (ver más abajo) |
| API en un servidor con HTTPS y la base de datos configurada | Pendiente por cliente |
| `ApplicationVersion` / `ApplicationDisplayVersion` | Hoy `1` / `1.0` |

## Datos básicos

- **Nombre** (30): `HoraTrack`
- **Tipo**: App · **Gratis** · Categoría: *Productividad* (o *Negocios*)
- **Correo de contacto**: `urrelsgisela@gmail.com`
- **Política de privacidad**: `https://urrels.github.io/FichajeApp/privacidad/`

## Descripción corta (80)

`Fichá tu entrada y salida desde el celular, con ubicación y verificación.`

## Descripción larga

```
HoraTrack es la app para registrar la entrada y la salida del personal de tu empresa desde el celular.

PARA EL PERSONAL
• Fichá con un solo toque: la app te dice si te toca entrada o salida.
• Si tu empresa lo pide, se registra tu ubicación al fichar.
• Si tu empresa lo activa y vos lo aceptás, la app registra tu recorrido mientras tu jornada está abierta (de la entrada a la salida), con una notificación fija. Fuera de la jornada no registra nada.
• Confirmás que sos vos con la huella, el patrón o el PIN de bloqueo de tu teléfono, y podés entrar con la huella.
• Si tu empresa lo activa, sacás una selfie al fichar.
• Tenés tu foto de perfil, podés cambiar tu PIN y recuperarlo por mail si lo olvidás.
• Un centro de notificaciones con los avisos de tu cuenta.

PARA LOS SUPERVISORES
• Mirá quién está presente en un mapa y consultá los fichajes por fecha y por persona.

PARA ADMINISTRACIÓN
• Alta, baja y permisos de usuarios, y corrección de fichajes.
• Liquidación de horas con horas extra, feriados y valor hora, con informe en PDF y CSV.
• Personalizá el nombre, los colores y el logo de la app.

PARA SUCURSALES
• Modo kiosco: un teléfono compartido donde cada persona elige su nombre y ficha con su PIN.

Necesitás una cuenta que te dé tu empresa. La app no sirve para registrarte por tu cuenta.
```

## Novedades de la primera versión (500)

`Primera versión: fichaje con ubicación, mapa para supervisores, liquidación de horas con informes en PDF y CSV, modo kiosco y foto de perfil.`

## Gráficos

- **Ícono 512×512 PNG**: exportar `Clientes/<cliente>/appicon.svg` + `appiconfg.svg` (o el ícono instalado en el teléfono).
- **Imagen destacada 1024×500**: logo del cliente sobre el color principal.
- **Capturas (mínimo 2, recomendadas 4 a 8 de teléfono)**: Fichar con el botón y la confirmación, mapa del supervisor, lista de usuarios
  con fotos, Liquidación con el resumen, Kiosco. Sacarlas con datos de prueba, **sin nombres ni fotos de personas reales**.

## Acceso para el revisor de Google (Contenido de la app → Acceso a la app)

```
Esta app requiere iniciar sesión con una cuenta creada por la empresa.
Servidor de prueba activo: [URL de la API]
Administrador de prueba  — Email: [EMAIL]  PIN: [PIN]
Empleado de prueba       — Email: [EMAIL]  PIN: [PIN]
Para fichar el teléfono tiene que tener un bloqueo de pantalla (PIN, patrón o huella) y el permiso de ubicación. En la cuenta de prueba la ubicación está desactivada (Cómo se ficha → Ubicación: Ninguna), así que se puede probar en un emulador.
```

Importante: la API tiene que estar encendida con esos usuarios mientras dure la revisión. La app rechaza ubicaciones simuladas
(`EsUbicacionFalsa`), así que en la cuenta de prueba conviene configurar *Cómo se ficha → Ubicación: Ninguna*.

## Seguridad de los datos (Data safety)

| Pregunta | Respuesta |
|---|---|
| ¿Recopila o comparte datos? | Sí, recopila. No comparte con terceros (el servidor es del proveedor y la base de datos es del cliente) |
| Ubicación precisa | Recopilada · opcional · finalidad: funcionalidad de la app. **Ahora también se recopila con la app cerrada** (recorrido durante la jornada): en Data safety, al marcar ubicación, indicá que se recopila también en segundo plano |
| Identificadores de dispositivo u otros | Token de Firebase Cloud Messaging para los push: recopilado, se asocia a la cuenta, finalidad: funcionalidad de la app (no publicidad), no se comparte con terceros salvo Google como proveedor del servicio de mensajes |
| Historial de ubicaciones | Es el recorrido durante la jornada: opcional (lo activa la empresa y la persona acepta un aviso) · funcionalidad de la app |
| Ubicación aproximada | No se usa (el permiso `ACCESS_COARSE_LOCATION` se declara solo porque Android lo exige junto con el preciso) |
| Nombre, dirección de correo | Recopilados · obligatorios · funcionalidad y administración de la cuenta |
| Fotos | Recopiladas (selfie al fichar y foto de perfil) · opcional según el cliente · funcionalidad de la app |
| Otro contenido / actividad | Registros de asistencia · funcionalidad |
| Info de autenticación (PIN) | Se recopila, se guarda cifrado (hash) y no se comparte |
| Biometría | La app no recibe ni guarda la huella: la verifica el sistema del teléfono |
| Cifrado en tránsito | Sí (HTTPS) |
| Pedir que se borren los datos | Sí, contactando a la empresa. Poner la URL o el correo en la ficha |

## Otros formularios

- **Clasificación de contenido**: sin violencia, sexo, drogas, apuestas, ni contenido generado por usuarios → apta para todos.
- **Público objetivo**: 18 años o más. **No es para niños.**
- **Anuncios**: no tiene. **Apps de gobierno / noticias / finanzas / salud**: no.
- **Permisos sensibles**: la app **no declara** `ACCESS_BACKGROUND_LOCATION` (no hace falta: el recorrido usa un servicio en primer plano
  iniciado con la app a la vista), así que **no** aplica el formulario de «ubicación en segundo plano».
- **Servicios en primer plano** (Contenido de la app → Permisos de servicios en primer plano): hay que declarar el tipo **Ubicación**
  (`FOREGROUND_SERVICE_LOCATION`). Google pide una descripción y **un video** que muestre: (1) la empresa activando «Recorrido» en Cómo se ficha;
  (2) un empleado fichando la entrada y viendo el **aviso destacado** de consentimiento; (3) la **notificación fija** visible mientras corre;
  (4) la salida, que la hace desaparecer. Texto sugerido: «La app registra la ubicación del empleado cada pocos minutos únicamente mientras su
  jornada laboral está abierta, para que su empleador vea el recorrido. Se muestra una notificación persistente y se detiene al fichar la salida.»
- **Notificaciones** (`POST_NOTIFICATIONS`): se usa para la notificación fija del servicio y no para publicidad.
- **Declaración de uso de la cámara**: selfie al fichar (opcional, la activa el administrador) y foto de perfil.
- **Declaración de ID de publicidad**: la app no lo usa.

## Permisos del manifiesto (revisión)

| Permiso | Para qué | Nota |
|---|---|---|
| `INTERNET`, `ACCESS_NETWORK_STATE` | Hablar con la API | |
| `ACCESS_FINE_LOCATION` | Ubicación del fichaje y del recorrido | Primer plano; el recorrido usa un servicio en primer plano |
| `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_LOCATION` | Servicio del recorrido durante la jornada | Requiere la declaración de servicios en primer plano en Play Console |
| `POST_NOTIFICATIONS` | Notificación fija del recorrido | Android 13 o más |
| `ACCESS_COARSE_LOCATION` | Requerido junto al preciso desde Android 12 | |
| `CAMERA` | Selfie y foto de perfil | `uses-feature camera` con `required=false`: se instala sin cámara |
| `USE_BIOMETRIC`, `USE_FINGERPRINT` | Confirmar identidad | `USE_FINGERPRINT` está en desuso; lo declara la librería de huella. Verificar si se puede quitar al migrar |

Cambios hechos en el manifiesto para publicar: `allowBackup="false"` (el token de sesión no debe viajar a otro teléfono) y
se quitó un comentario viejo que hablaba de una clave de Google Maps que ya no se usa.

## Notas de versiones siguientes

Subir `ApplicationVersion` (entero) en cada subida a Play; Play rechaza un `.aab` con un número ya usado.
