# Plan de regresión funcional y de integración, FichajeApp

Armado leyendo el código de `main` y de los PR #36, #37 y #38 (probados juntos en una rama local). Los casos de PDF, CSV y foto de perfil solo aplican si esos PR se mergean. No se ejecutó ningún caso: es análisis de código.

## Mapa de la aplicación

- **Navegación:** `AppShell` registra las rutas de administración. Al cerrar sesión o vencer el token, `OnSesionFinalizada` vuelve a `//LoginPage`. El menú muestra Fichar, Supervisor y Administración según los permisos.
- **Estado global:** `SessionManager` (singleton con patrón Observer) guarda el token y los permisos. El token y los permisos persisten en `SecureStorage`.
- **Clientes HTTP:**
  - `AuthService`: login.
  - `ApiService`: fichar, consultas de supervisor y kiosco. Timeout de 15 s para fichar y de 20 s para consultas.
  - `AdminApiService`: todo lo de administración, con timeout de 30 s.
  - `AparienciaService`: timeout de 4 s.
- **Traducción de errores:** `TextosDeError.Para` y `DeExcepcion`.
- **API:** `Auth`, `Fichaje`, `Kiosco`, `Supervisor`, `Admin*` (Usuarios, Roles, Bitácora, Fichajes, Liquidación, Apariencia, Cómo se ficha), `Perfil`, `Configuracion` y `Feriados`.
- **Reglas de fichaje:** se arman en `FichajeService.ValidarAsync` según la configuración.
- **Errores de negocio:** un `InvalidOperationException` sale como 400 con `{mensaje}`.

## Hallazgos del código (confirmar antes de ejecutar)

1. **`GeolocationService.cs:68`:** devuelve `ex.Message` cuando salta un `InvalidOperationException`. Contradice la regla de `TextosDeError` (nunca mostrar texto técnico). Caso REG-FIC-14.
2. **`AuthService.LoginAsync`:** no usa `CancellationToken` propio y toma el timeout por defecto del `HttpClient`, 100 s. Si el servidor responde un error sin JSON, el parseo falla y se muestra el mensaje genérico, no el del servidor. Casos REG-LOG-09 y 10.
3. **`ObtenerConfiguracionFichajeAsync`:** si falla la consulta, usa "ubicación libre, sin foto". Con foto obligatoria el servidor igual rechaza (`FOTO_REQUERIDA`), pero la persona no recibe el aviso antes de intentar. Caso REG-FIC-15.
4. **`ApiService.GetAsync`:** el texto "No tenés permiso de supervisor" se usa en todo GET con 403, incluido `fichaje/estado`. Caso REG-API-05.
5. **`GeoValidator`:** tiene coordenadas fijas con el comentario "reemplazar por las reales". Solo aplica a la ubicación "Sede fija". Caso REG-FIC-12.

## Casos de prueba

### 1. Login, sesión y servidor

| ID | Módulo/Pantalla | Archivo/Clase de Referencia | Descripción del Caso | Pasos a seguir | Resultado Esperado | Prioridad |
|---|---|---|---|---|---|---|
| REG-LOG-01 | Login | `LoginViewModel.LoginAsync`, `AuthController.Login` | Happy path de login | 1. Email y PIN válidos. 2. Tocar Entrar | Navega a la pantalla de su rol. El cartel de estado queda vacío (no "Login exitoso") y el PIN se limpia | Alta |
| REG-LOG-02 | Login | `LoginViewModel` | Campos vacíos | Dejar email o PIN vacío y entrar | "Completá email y PIN". No se hace ninguna llamada a la red | Alta |
| REG-LOG-03 | Login | `AuthController.Login` | Credenciales incorrectas | PIN erróneo con email existente | "Email o PIN incorrecto" en rojo, centrado | Alta |
| REG-LOG-04 | Login | `LoginService.VerificarAsync` (`MaxIntentos = 3`) | Bloqueo al tercer intento fallido | 3 PIN erróneos seguidos | El tercer intento muestra el mensaje de usuario bloqueado. Un cuarto intento con el PIN correcto también lo muestra. Se registran LOGIN_FALLIDO y USUARIO_BLOQUEADO | Alta |
| REG-LOG-05 | Login | `VerificarAsync` | Usuario de baja o inexistente | Email de un usuario dado de baja | "Email o PIN incorrecto" (mismo mensaje que si no existiera) y no suma intentos | Media |
| REG-LOG-06 | Login | `AuthController` (política "login", 10 por minuto por IP) | Límite de intentos por IP | 11 intentos de login en menos de 1 minuto | El 11.º es rechazado. Registrar qué ve la persona (ver REG-LOG-09) | Media |
| REG-LOG-07 | Login → Cerrar sesión | `LoginPage.OnAppearing`, `LoginViewModel.Limpiar` | Pantalla limpia después de cerrar sesión | Entrar, cerrar sesión, volver al login | No hay mensaje ni PIN de la vez anterior | Alta |
| REG-LOG-08 | Login | `AuthService.LoginAsync` | Sin conexión al servidor | Modo avión, intentar entrar | "No pudimos conectarnos. Revisá tu internet…" | Alta |
| REG-LOG-09 | Login | `AuthService.LoginAsync` | Error del servidor sin JSON (ej. 429 o 502 con cuerpo vacío o HTML) | Simular respuesta no JSON | No se cuelga ni se cierra la app. Se muestra un mensaje legible. Registrar cuál es (hoy cae en el mensaje genérico) | Alta |
| REG-LOG-10 | Login | `AuthService.LoginAsync` | Servidor que no responde | Apuntar a un servidor que no contesta | Debe terminar con un mensaje en un tiempo razonable. Medir cuánto tarda (el código no fija timeout propio) | Media |
| REG-LOG-11 | Arranque | `AuthService.RestaurarSesionAsync` | Sesión guardada | Entrar, cerrar la app y abrirla | Vuelve a la pantalla del rol sin pedir PIN | Alta |
| REG-LOG-12 | Arranque | `RestaurarSesionAsync` | Sesión guardada sin permisos (versión anterior) | Dejar `auth_permisos` vacío en el almacenamiento | Se pide iniciar sesión de nuevo | Media |
| REG-LOG-13 | Sesión | `ApiService.GetAsync` (401) | Token vencido | Invalidar el token y abrir cualquier pantalla con datos | "Por seguridad cerramos tu sesión…" y vuelve al login desde cualquier pantalla | Alta |
| REG-LOG-14 | Login | `LoginPage.OnLogoTapped`, `DireccionServidor.Normalizar` | Cambiar la dirección del servidor con 5 toques en el logo | Tocar el logo 5 veces dentro de 3 s y escribir una dirección | Acepta `host` o `https://host` y agrega `/api`. Rechaza vacío, `http://` fuera de localhost, direcciones con usuario, parámetros o `#`. Descarta la apariencia guardada del servidor anterior | Alta |
| REG-LOG-15 | Login | `LoginPage.OnLogoTapped` | Toques espaciados | 5 toques con más de 3 s entre el primero y el último | No se abre el diálogo | Baja |
| REG-LOG-16 | Apariencia | `AparienciaService.ActualizarAsync` | Servidor lento o caído al abrir | Sin conexión al abrir la app | Tarda como máximo 4 s y sigue con los colores y el logo guardados | Media |

### 2. Fichar (empleado)

| ID | Módulo/Pantalla | Archivo/Clase de Referencia | Descripción del Caso | Pasos a seguir | Resultado Esperado | Prioridad |
|---|---|---|---|---|---|---|
| REG-FIC-01 | Fichar | `FichajeViewModel.FicharAsync`, `FichajeController.RegistrarFichaje` | Happy path con ubicación en sitio conocido | Config "Libre" y sin foto. Fichar dentro de un sitio registrado | Confirmación "¡Listo! Entrada registrada a las HH:mm" (se oculta a los 8 s), aparece el enlace al mapa y el botón pasa a "Fichar salida" | Alta |
| REG-FIC-02 | Fichar | `ProximoTipoAsync` | Alternancia Entrada/Salida | Fichar dos veces seguidas | Primero Entrada, luego Salida. El botón siempre muestra lo que viene | Alta |
| REG-FIC-03 | Fichar | `ValidacionMotivoSiLugarDesconocido`, `FichajeViewModel` | Fuera de un sitio conocido, ubicación libre | Fichar lejos de cualquier sitio | Pide el motivo. Si lo escribe, registra con "Otro lugar" y el motivo. Si cancela: "Cancelamos el fichaje porque falta el motivo…" | Alta |
| REG-FIC-04 | Fichar | `ValidacionUbicacionReal` | Ubicación simulada | Fichar con una app de GPS falso | Rechazado con el aviso de ubicación simulada | Alta |
| REG-FIC-05 | Fichar | `ValidacionUbicacionReal` | Precisión insuficiente | Fichar con precisión peor que el máximo (por defecto 50 m) | Mensaje de "no es lo bastante precisa" con la ayuda extra sobre ubicación "Precisa" | Alta |
| REG-FIC-06 | Fichar | `FichajeViewModel` | Permiso de ubicación denegado | Denegar el permiso | "Necesitamos el permiso de ubicación…" y se termina de procesar | Alta |
| REG-FIC-07 | Fichar | `ValidacionEmpleadoActivo` | Empleado dado de baja con token vigente | Dar de baja al usuario y fichar con la sesión abierta | "Empleado no autorizado o inactivo" | Media |
| REG-FIC-08 | Fichar | `ConfiguracionFichaje` (Ubicacion "Ninguna") | Fichar sin ubicación | Admin configura ubicación "Ninguna" y se ficha | No pide permiso ni GPS. Se guarda con lugar "Sin ubicación" y coordenadas 0,0 | Alta |
| REG-FIC-09 | Fichar | `FichajeViewModel`, `AvisoFoto`, `ValidacionFoto` | Foto obligatoria, camino feliz | Foto "Obligatoria". Aceptar el aviso, sacar la selfie y fichar | Registra y la foto aparece en el panel de supervisor | Alta |
| REG-FIC-10 | Fichar | `FichajeViewModel` | Foto obligatoria rechazada o cancelada | Rechazar el aviso, o cancelar la cámara | Rechazar: "Para fichar con foto tenés que estar de acuerdo…". Cancelar la cámara: "Este fichaje requiere una foto…". No se registra nada | Alta |
| REG-FIC-11 | Fichar | `FichajeController`, `ReglasFotos`/`AlmacenFotos` (400 KB, PNG/JPG) | Foto inválida o pesada | Mandar a la API una foto >400 KB, no PNG/JPG, o base64 roto | 400 con código `FOTO_INVALIDA`. La app muestra "No pudimos usar esa imagen…" | Media |
| REG-FIC-12 | Fichar | `GeoValidator`, `ValidacionUbicacion` | Config "Sede fija" | Fichar con ubicación "Sede fija", cerca y lejos de las coordenadas fijas del código | Dentro de 100 m registra con lugar "Sede". Fuera: "Fuera del rango permitido para fichar". Confirmar si las coordenadas están ajustadas al cliente | Media |
| REG-FIC-13 | Fichar | `ApiService.RegistrarFichajeAsync` | Timeout al registrar (15 s) | Cortar la red justo al enviar | "El servidor tardó en responder. Antes de volver a fichar, revisá si tu fichaje quedó registrado." No se reintenta solo | Alta |
| REG-FIC-14 | Fichar | `GeolocationService.ObtenerUbicacionAsync` | GPS apagado (`InvalidOperationException`) | Apagar el GPS y fichar | Mensaje legible para la persona. Verificar que no se vea texto técnico (el código usa `ex.Message`) | Media |
| REG-FIC-15 | Fichar | `ApiService.ObtenerConfiguracionFichajeAsync` | Falla la consulta de configuración | Cortar la red solo en `fichaje/configuracion` | Usa el valor por defecto (ubicación libre, sin foto). Con foto obligatoria el servidor rechaza con `FOTO_REQUERIDA` y la app muestra "Para fichar necesitamos una foto…" | Media |
| REG-FIC-16 | Fichar | `FichajeController`, `BloqueoPorClave` | Doble toque o dos teléfonos a la vez | Tocar Fichar dos veces rápido, o fichar al mismo tiempo desde dos dispositivos con la misma cuenta | Se registran en orden y alternando Entrada/Salida, sin dos "Entrada" seguidas | Alta |
| REG-FIC-17 | Fichar | `FichajePage` (UI) | Botón en pantallas chicas y letra grande | Pantalla angosta y escala de fuente alta | El botón y su texto se ven completos y centrados | Media |
| REG-FIC-18 | Selfie | `SelfiePage` | Cámara frontal | Abrir la cámara varias veces seguidas, tocar atrás y cancelar | Abre en la frontal. El botón de sacar foto se habilita cuando la cámara está lista. No se cierra la app | Alta |
| REG-FIC-19 | Selfie | `SelfiePage` | Sin cámara frontal | Dispositivo sin cámara frontal | Cae a la cámara del sistema (`SelfieNoDisponibleException`) | Media |

### 3. Kiosco

| ID | Módulo/Pantalla | Archivo/Clase de Referencia | Descripción del Caso | Pasos a seguir | Resultado Esperado | Prioridad |
|---|---|---|---|---|---|---|
| REG-KIO-01 | Kiosco | `KioscoController`, `KioscoService.ListarAsync` | Lista de empleados | Entrar con la cuenta de kiosco con el modo activado | Muestra empleados activos con permiso de fichar, ordenados, con "Entrada" o "Salida". La cuenta de la sucursal no figura | Alta |
| REG-KIO-02 | Kiosco | `KioscoService.FicharAsync` | Fichaje correcto | Elegir empleado, PIN correcto | Registra a nombre del empleado con el nombre de la sucursal como lugar | Alta |
| REG-KIO-03 | Kiosco | `KioscoViewModel`, `KioscoService` | PIN incorrecto | PIN erróneo | "PIN incorrecto." Se queda en el mismo empleado. No cierra la sesión del kiosco (no responde 401) | Alta |
| REG-KIO-04 | Kiosco | `KioscoViewModel` | PIN con menos de 4 dígitos o con letras | Escribir 3 dígitos o letras | "El PIN tiene 4 dígitos." sin llamar a la API | Media |
| REG-KIO-05 | Kiosco | `KioscoService.FicharAsync` | Empleado bloqueado | Bloquear y fichar | Mensaje de bloqueo. Contar intentos fallidos como en login (3) | Alta |
| REG-KIO-06 | Kiosco | `KioscoController.SiNoEstaActivado` | Modo kiosco desactivado | Admin pone el modo "Personal" y la cuenta de kiosco abre la lista | 409 con `KIOSCO_DESACTIVADO`. La app muestra el mensaje y no cierra la sesión | Alta |
| REG-KIO-07 | Kiosco | `KioscoViewModel`, `KioscoController` | Foto obligatoria en kiosco | Foto "Obligatoria". Fichar sin foto, con foto y rechazando el aviso | Sin foto: "Este fichaje requiere una foto…". Con foto: registra. Rechazo: mensaje de consentimiento | Media |
| REG-KIO-08 | Kiosco | `KioscoViewModel` | Lista vacía o error al cargar | Sin empleados con permiso de fichar, y también cortando la red | Estado vacío legible, y `ErrorDeCarga` con el mensaje del error | Media |
| REG-KIO-09 | Kiosco | `AuthController` (30 días para kiosco) | Sesión larga del kiosco | Entrar con cuenta solo-kiosco | El token dura 30 días. La baja o el bloqueo de la cuenta cortan el acceso | Baja |

### 4. Supervisor

| ID | Módulo/Pantalla | Archivo/Clase de Referencia | Descripción del Caso | Pasos a seguir | Resultado Esperado | Prioridad |
|---|---|---|---|---|---|---|
| REG-SUP-01 | Supervisor | `SupervisorController.Presentes`, `SupervisorViewModel` | Presentes en el mapa | Abrir el panel con gente que fichó | El mapa muestra a quienes fichan con ubicación. Sin ubicación (0,0) no aparecen en el mapa (`TieneUbicacion`) | Alta |
| REG-SUP-02 | Supervisor | `SupervisorController.Fichajes` | Consulta por rango y empleado | Elegir fechas y filtrar por empleado | Lista con fecha, tipo, lugar, motivo e ícono de foto. El filtro funciona | Alta |
| REG-SUP-03 | Supervisor | `SupervisorController.Fichajes` | Rango inválido | "Hasta" anterior a "Desde"; y un rango mayor a 62 días | 400 con "La fecha 'hasta' no puede ser anterior…" y "El rango no puede superar 62 días" | Alta |
| REG-SUP-04 | Supervisor | `SupervisorController.Foto`, `ApiService.DescargarFotoAsync` | Ver la foto de un fichaje | Abrir un fichaje con foto | Se ve la foto. Si ya se borró (retención), 404 con "La foto ya no está disponible…" y la app no se rompe | Media |
| REG-SUP-05 | Supervisor | `AlmacenFotos` (regex de nombre) | Ruta de foto manipulada | Pedir `archivo=../x` o un nombre que no cumple el formato | No devuelve archivos. Respuesta 404 | Alta |
| REG-SUP-06 | Supervisor | `RequierePermiso` | Usuario sin permiso | Empleado común abre `/api/supervisor/*` | 403. La app muestra "No tenés permiso de supervisor". El menú no muestra la sección | Alta |
| REG-SUP-07 | Supervisor | `SupervisorViewModel` | Sin datos o error de red | Rango sin fichajes; red cortada | Estado vacío claro, y el mensaje de conexión | Media |

### 5. Administración

| ID | Módulo/Pantalla | Archivo/Clase de Referencia | Descripción del Caso | Pasos a seguir | Resultado Esperado | Prioridad |
|---|---|---|---|---|---|---|
| REG-ADM-01 | Usuarios | `UsuariosController`, `UsuariosServicios` | Alta de usuario | Crear con nombre, email, PIN de 4 dígitos y rol | Aparece en la lista y puede iniciar sesión | Alta |
| REG-ADM-02 | Usuarios | `ValidadorPin`, `UsuariosServicios` | Alta inválida | Nombre vacío; PIN de 3 dígitos o con letras; email o nombre repetido; sin rol | Mensajes: "Indicá el nombre completo", "El PIN debe tener exactamente 4 dígitos", "Ya existe un empleado con ese email/nombre", "Asigná al menos un rol" | Alta |
| REG-ADM-03 | Usuarios | `UsuariosServicios.ActualizarDatos` | Cambiar el nombre de alguien con fichajes | Editar el nombre de un empleado con fichajes | "No se puede cambiar el nombre de un empleado que ya tiene fichajes." | Media |
| REG-ADM-04 | Usuarios | `DarDeBajaAsync`, `_guarda.ValidarAsync` | Baja y protección del último administrador | Dar de baja a un usuario común y luego intentar con el último administrador | La baja del usuario común funciona (queda en el historial). Con el último administrador la guarda lo rechaza | Alta |
| REG-ADM-05 | Usuarios | `ResetearPinAsync`, `DesbloquearAsync` | Restablecer PIN y desbloquear | Bloquear un usuario, restablecer su PIN, entrar | Queda desbloqueado, con intentos en 0. Entra con el PIN nuevo. Queda en la bitácora | Alta |
| REG-ADM-06 | Usuarios | `UsuariosController.historial/rollback` | Historial y restauración | Editar un usuario, abrir el historial y restaurar una versión | Restaura. Una versión generada por un rollback no se puede restaurar | Media |
| REG-ADM-07 | Cambiar PIN | `CuentaController`, `CambiarPinPage` | Cambio de PIN propio | PIN actual correcto y nuevo válido; luego con actual incorrecto; con repetición distinta | Éxito, "El PIN actual no es correcto." y "El PIN nuevo y su repetición no coinciden." | Alta |
| REG-ADM-08 | Roles | `RolesService` | Árbol de roles | Crear, duplicar, cambiar padre, eliminar | No permite padre inexistente, referencia circular, nombre repetido ni vacío. Un rol del sistema o con usuarios no se elimina | Alta |
| REG-ADM-09 | Roles / permisos | `RolesService`, `RequierePermiso` | Cambio de permisos en vivo | Quitarle un permiso a un rol y usar esa función con una sesión abierta | Esa función responde 403 y el menú deja de mostrarla (tras volver a entrar) | Alta |
| REG-ADM-10 | Bitácora | `BitacoraController` | Consulta de bitácora | Hacer login, login fallido y una baja; abrir la bitácora | Aparecen LOGIN, LOGIN_FALLIDO y las acciones de administración. Sin permiso: 403 | Media |
| REG-ADM-11 | Fichajes (corrección) | `JornadasYFichajesAdmin` | Completar entrada o salida faltante | Con un fichaje "sin salida", completarlo | Registra con motivo. Muestra errores: "La entrada indicada no existe", "Esa salida ya tiene su entrada", "Indicá el motivo…" | Alta |
| REG-ADM-12 | Fichajes (corrección) | `JornadasYFichajesAdmin` | Fichaje manual inválido | Fecha futura; egreso anterior al ingreso; jornada >24 h; superposición; fecha en período liquidado | Cada caso con su mensaje: "No se puede cargar un fichaje con fecha futura", "El egreso tiene que ser posterior al ingreso", "Una jornada no puede durar más de 24 horas", "El fichaje se superpone…", "Esa fecha pertenece a un período ya liquidado" | Alta |
| REG-ADM-13 | Cómo se ficha | `ConfiguracionFichajeService`, `ConfigFichajeViewModel` | Cambiar ubicación, foto, modo y precisión | Guardar cada combinación válida; probar valores inválidos por API | Se aplica en el siguiente fichaje. Un valor inválido: "La opción de … no es válida. Opciones: …" | Alta |
| REG-ADM-14 | Apariencia | `AparienciaService` (servidor), `ClienteService` | Cambiar nombre, colores y logo | Guardar nombre, colores y un logo PNG o JPG | La app toma colores y logo. Rechaza: nombre largo, color que no sea `#RRGGBB`, logo que no sea PNG/JPG o muy pesado | Media |
| REG-ADM-15 | Configuración | `ConfiguracionController`, `ConfiguracionPage` | Estado de conexión con Google | Abrir el estado y cargar el .json de la cuenta de servicio | Muestra el estado. Con un archivo que no es válido: "No pudimos leer ese archivo…" | Media |

### 6. Liquidación

| ID | Módulo/Pantalla | Archivo/Clase de Referencia | Descripción del Caso | Pasos a seguir | Resultado Esperado | Prioridad |
|---|---|---|---|---|---|---|
| REG-LIQ-01 | Liquidación | `LiquidacionController.calcular`, `LiquidacionViewModel` | Calcular un período | Elegir fechas con fichajes y calcular | Resumen por empleado con horas a pagar, extra, dom/fer, ausencias e importe. Sin horas: "No hay horas en ese período" | Alta |
| REG-LIQ-02 | Liquidación | `LiquidacionService.Cerrar` | Cerrar un período | Calcular y cerrar | Queda "Cerrada" y aparece en Cerradas | Alta |
| REG-LIQ-03 | Liquidación | `LiquidacionService` | Cierres inválidos | Fecha futura; período que se superpone con otro; sin horas; empleado sin valor hora; desde posterior a hasta; período mayor al máximo | Cada caso con su mensaje: "No se puede liquidar una fecha futura", "Ya existe una liquidación que se superpone…", "No hay horas para liquidar en el período", "Falta cargar el valor hora de: …", "La fecha desde no puede ser posterior a la fecha hasta", "El período no puede superar los N días" | Alta |
| REG-LIQ-04 | Liquidación | `LiquidacionService` | Marcar como pagada | Marcar una liquidación cerrada; luego probar con una ya pagada | La primera pasa a pagada. Solo se paga una "Cerrada" ("Solo se puede marcar como pagada una liquidación cerrada") | Media |
| REG-LIQ-05 | Liquidación | `LiquidacionController.csv/pdf`, `ReportePdf` | Descarga de informe | Cerradas → período → Descargar PDF y CSV | El menú de Android permite guardar o enviar. El PDF abre en un visor con resumen, total y detalle. El CSV abre en Excel con columnas separadas | Alta |
| REG-LIQ-06 | Liquidación | `ReportePdf.Liquidacion` | PDF con muchos empleados o caracteres especiales | Período con muchos empleados, nombres con tildes, paréntesis o `\` | Continúa en otra página con "Página X de N". Los caracteres especiales no rompen el archivo | Media |
| REG-LIQ-07 | Liquidación | `LiquidacionController.pdf` | Liquidación inexistente o sin permiso | Pedir el PDF de un id inexistente y con un usuario sin permiso | 400 "La liquidación no existe." / 403 | Media |
| REG-LIQ-08 | Liquidación | `LiquidacionViewModel` | Error durante la descarga | Cortar la red al descargar | El estado "Armando el PDF…" desaparece y se muestra el error. No queda "Procesando" activo | Media |
| REG-LIQ-09 | Parámetros | `LiquidacionService` (validaciones de parámetros) | Valores fuera de rango | Tolerancia >60; redondeo >60; jornada normal 0 o >24; recargo >300%; máximo de horas abiertas fuera de 1–24 | Cada caso rechazado con su mensaje. Los valores válidos se guardan | Media |
| REG-LIQ-10 | Valor hora | `LiquidacionService` | Valor hora inválido | Valor 0 o negativo; empleado inexistente | "El valor hora debe ser mayor a cero." / "El empleado no existe." | Media |
| REG-LIQ-11 | Excedentes | `LiquidacionController.excedentes/aprobar` y `revocar` | Aprobar y revocar horas excedentes | Aprobar un excedente, recalcular y luego revocar | El importe cambia al aprobar y vuelve al revocar | Media |
| REG-LIQ-12 | Feriados | `FeriadosService`, `FeriadosController` | Sincronizar feriados oficiales | Actualizar con internet y sin internet en el servidor | Con internet: dice cuántos agregó. Repetir: ya estaban al día. Sin internet: aviso de conexión y no cambia nada | Media |
| REG-LIQ-13 | Feriados | `LiquidacionService` | Feriado manual | Fecha inválida; nombre vacío; mismo día que un oficial | "La fecha del feriado no es válida", "Escribí un nombre para el feriado". El oficial conserva su nombre al sincronizar | Baja |
| REG-LIQ-14 | Liquidación | `LiquidacionPage` (UI) | Botones en distintas pantallas | Pantalla angosta y letra grande | "Configuración" y "Cerradas" se ven completos y centrados | Media |

### 7. Foto de perfil

| ID | Módulo/Pantalla | Archivo/Clase de Referencia | Descripción del Caso | Pasos a seguir | Resultado Esperado | Prioridad |
|---|---|---|---|---|---|---|
| REG-FOT-01 | Fichar | `FichajePage.OnMiFotoTapped`, `PerfilController` (PUT/DELETE `api/perfil/foto`) | Cambiar y quitar la foto propia | Tocar la foto redonda, sacar foto nueva; luego "Quitar mi foto" | Queda en el encabezado. Al quitarla vuelve a las iniciales | Alta |
| REG-FOT-02 | Usuarios | `UsuariosController` (PUT/DELETE `{id}/foto`) | Foto de otra persona por el administrador | Usuarios → persona → Foto de perfil | Queda guardada, con registro en la bitácora. Sin permiso: 403 | Media |
| REG-FOT-03 | Perfil | `PerfilController`, `AlmacenPerfiles` | Foto inválida | Mandar foto >400 KB o no PNG/JPG; id de usuario inexistente | 400 con mensaje claro. Usuario inexistente: "El usuario no existe." | Media |
| REG-FOT-04 | Usuarios / Kiosco | `PerfilFotos`, `AvatarUsuario` | Fotos en listas | Abrir Usuarios y Kiosco con y sin fotos | Muestra la foto o las iniciales. Una persona sin foto no rompe la lista. Cada foto se baja una sola vez | Media |
| REG-FOT-05 | Perfil | `PerfilController` `versiones`, `PerfilFotos` | Cambio de foto en otro teléfono | Cambiar la foto en el teléfono A y abrir la lista en el B | En el B se ve la nueva con la versión actualizada (a lo sumo en un minuto) | Baja |

### 8. Integración API y cliente HTTP

| ID | Módulo/Pantalla | Archivo/Clase de Referencia | Descripción del Caso | Pasos a seguir | Resultado Esperado | Prioridad |
|---|---|---|---|---|---|---|
| REG-API-01 | Cliente HTTP | `ApiService.GetAsync`, `AdminApiService.RevisarErrores` | 401 en cualquier endpoint | Responder 401 | `NotificarSesionExpirada` borra `SecureStorage` y vuelve al login con "Por seguridad cerramos tu sesión…" | Alta |
| REG-API-02 | Cliente HTTP | `RevisarErrores` | 400 con `{mensaje}` | Provocar un error de negocio (ej. liquidar sin horas) | La pantalla muestra el texto del servidor, sin código ni jerga | Alta |
| REG-API-03 | Cliente HTTP | `RevisarErrores`, `GetAsync` | 5xx o cuerpo no JSON | Responder 500 con HTML o texto | "El servidor respondió con error (500)". No se cierra la app | Alta |
| REG-API-04 | Cliente HTTP | `Pedir` (30 s), `GetAsync` (20 s) | Timeout | Servidor que no responde | "El servidor tardó demasiado en responder…" después del tiempo límite | Alta |
| REG-API-05 | Cliente HTTP | `ApiService.GetAsync` | 403 en distintas pantallas | Provocar 403 en `fichaje/estado` y en `supervisor/*` | Verificar el texto que ve la persona, porque el texto de supervisor se usa para cualquier 403 | Baja |
| REG-API-06 | Cliente HTTP | `ApiService.RegistrarFichajeAsync` | 403 al fichar | Usuario sin el permiso de fichar | "Tu usuario no tiene permiso para fichar" | Media |
| REG-API-07 | API | `ManejarReglasNegocio` | Excepción de negocio | Provocar un `InvalidOperationException` | 400 con `{mensaje}` | Alta |
| REG-API-08 | API | `Program.cs`, `ConfiguracionSegura` | Arranque sin secreto JWT | Iniciar sin `Jwt__Secret`, o con menos de 32 caracteres | No arranca y explica qué falta | Media |
| REG-API-09 | API | `RequierePermiso` en cada controlador | Matriz de permisos | Llamar a un endpoint de cada controlador con un rol sin el permiso | Todos responden 403. Sin token: 401 | Alta |
| REG-API-10 | API | `Servicios/ConexionService`, `ConfiguracionController` | Hoja de Google no conectada | Arrancar con `conexion.json` renombrado | Muestra el código de activación en el log y la app guía al usuario a configurar. No hay errores sin explicación | Media |
| REG-API-11 | Datos | Repositorios de Google Sheets | Hoja con datos incompletos | Fichaje sin empleado, fila sin Id | La API devuelve error controlado (400, 404 o mensaje de negocio) y no un 500 sin explicación | Media |

### 9. Casos borde de la interfaz y transiciones

| ID | Módulo/Pantalla | Archivo/Clase de Referencia | Descripción del Caso | Pasos a seguir | Resultado Esperado | Prioridad |
|---|---|---|---|---|---|---|
| REG-UI-01 | Todas | `AppShell.OnSesionFinalizada` | Sesión vencida en medio de una pantalla | Estar en Liquidación y dejar vencer el token | Vuelve al login sin quedar pantallas apiladas. Al volver a entrar no hay datos de la sesión anterior | Alta |
| REG-UI-02 | Navegación | `AppShell`, `AppShell.ActualizarMenu` | Menú según rol | Entrar con Empleado, Supervisor, Administrador y Kiosco | Cada uno ve solo lo suyo. El kiosco no ve "Fichar" | Alta |
| REG-UI-03 | Navegación | botón Atrás | Atrás en pantallas raíz y modales | Presionar atrás en Fichar, en la cámara y en el login | En la cámara cancela. En raíz no deja la app en un estado roto | Media |
| REG-UI-04 | Listas | pantallas con listas | Estado vacío | Abrir listas sin datos (usuarios filtrados sin resultados, bitácora vacía, cerradas vacías) | Muestran un estado vacío legible, sin errores | Media |
| REG-UI-05 | Formularios | `Entry` de PIN/email | Entradas malformadas | Espacios, emojis, texto muy largo, caracteres especiales en nombre, motivo y descripciones | No rompe. Se respetan los límites (motivo 120 caracteres, descripción de feriado 100, nombre de rol) con mensaje claro | Media |
| REG-UI-06 | Procesos largos | `Procesando` en ViewModels | Doble toque en botones con llamadas | Tocar rápido dos veces Entrar, Fichar, Guardar, Cerrar período | Una sola operación; el botón queda deshabilitado mientras procesa | Alta |
| REG-UI-07 | Rotación y segundo plano | páginas con estado | Rotar el teléfono o pasar la app a segundo plano a mitad de un fichaje | Hacerlo durante "Registrando fichaje…" | El fichaje no se duplica y el estado final es coherente. Si se perdió la respuesta, rige REG-FIC-13 | Media |
| REG-UI-08 | Accesibilidad visual | `Tema.xaml` | Letra grande y distintos tamaños de pantalla | Escala de fuente 1.3, pantallas de 320 a 412 dp | Textos de botones sin cortes y alineados | Media |

## Orden de ejecución sugerido

1. Prioridad alta de los módulos 1, 2 y 8: login, sesión, fichar y errores del cliente HTTP.
2. Prioridad alta de Liquidación, Kiosco y Administración.
3. Prioridad media y baja.
4. Los 5 hallazgos, antes de dar el plan por cerrado.

## Alcance y límites

- El plan se basa en lo que está en el código. Los resultados esperados de interfaz salen de los textos del código, no de cómo se ve cada pantalla en un teléfono.
- Los casos de timeout, 5xx y respuestas no JSON necesitan un servidor falso o cortar la red a propósito. La API ya tiene 422 tests automáticos; estos casos son los que hoy solo se pueden probar a mano.
