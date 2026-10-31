# Prueba completa, de punta a punta

Hasta ahora casi todo se probó con tests automáticos y compilando, **no con una planilla real y un teléfono**. Esta lista sirve para
recorrerlo todo una vez antes de dárselo a un cliente. Marcá cada punto; si algo falla, anotá el mensaje exacto de la pantalla.

Usá una **planilla de Google nueva y vacía** y los datos de prueba (administrador `admin@prueba.com`, PIN `1234`).

## Empezar de cero con una planilla nueva

Importante: la conexión que se guarda desde la app queda en `src/FichajeApp.Api/datos/conexion.json` y **manda sobre** el
`SpreadsheetId` de `appsettings`. Por eso, vaciar el `SpreadsheetId` no alcanza para cambiar de planilla.

**Opción 1, desde la app (la más simple):** entrá como administrador → Administración → **Conexión con Google Sheets**.
Compartí la planilla nueva (vacía) con el correo que muestra la pantalla, pegá su link y completá los datos del primer
administrador de esa planilla. Al guardar, la app cierra tu sesión y tenés que entrar con el administrador nuevo.
La planilla vieja no se toca.

**Opción 2, desde la PC (para empezar totalmente limpio):**
1. Cerrá la API.
2. Borrá **solo** el archivo `src/FichajeApp.Api/datos/conexion.json` (no la carpeta: ahí están las fotos y las llaves).
3. En `appsettings.Development.json` dejá `"SpreadsheetId": ""` y `"Setup": { "Clave": "prueba123" }`.
4. Iniciá la API. En el teléfono, al abrir la app, si todavía tenía una sesión de la planilla vieja se cierra sola y te lleva
   a "Conectar Google Sheets": compartí la planilla nueva con el correo, pegá el link y completá el administrador.

Las fotos de la prueba anterior quedan en `datos/fotos`; si querés borrarlas, borrá esa carpeta.
## A. Conectar la planilla (como lo haría un cliente)
- [ ] **Todo de una vez**: con "Crear la planilla por mí" activado, escribís tu Gmail, el nombre de la empresa y los datos del administrador, tocás **Crear y conectar** y el servidor crea la planilla, la prepara y te la comparte (te llega un mail y aparece en Drive → «Compartidos conmigo»). Requiere **Google Drive API** habilitada en el proyecto de Google Cloud.
- [ ] Si la planilla conectada se **borra** (probar borrándola en Drive), al abrir la app aparece "Conectar Google Sheets" con un aviso rojo que explica que ya no está disponible, y se puede crear una nueva con el código de activación.
- [ ] La app, al abrir, te lleva sola a "Conectar Google Sheets" (o con el botón del login).
- [ ] La pantalla muestra el correo de la cuenta de servicio para compartir la planilla; "Copiar correo" funciona.
- [ ] Con la planilla **sin compartir**: el mensaje dice con qué correo hay que compartirla.
- [ ] Con un código de activación incorrecto: dice que no es correcto.
- [ ] Con todo bien: se guarda y aparecen en la planilla las pestañas `Empleados`, `Fichajes`, `Sitios`, `Roles`, `Permisos`, `Parametros`, etc.
- [ ] Entrás con el administrador que cargaste y ves **su panel** (Supervisor/Administración), no la pantalla del kiosco.
- [ ] Desde Administración → "Conexión con Google Sheets" se puede volver a abrir (solo el administrador).
- [ ] Sin sesión de administrador, intentar reconectar con el código **ya no funciona** ("La planilla ya está conectada…").

- [ ] (Teléfono) 5 toques al logo del login cambian la dirección del servidor; una dirección mala se rechaza y una caída avisa "Guardada, pero no responde"; vacía vuelve a la original.

- [ ] (Teléfono en modo oscuro) los textos se leen bien: la app siempre se ve clara.
- [ ] Al guardar la planilla con un dato mal puesto aparece un **cartel** "No se pudo conectar" con el motivo, y se puede reintentar con el mismo código.
- [ ] Al guardar bien, la app vuelve **sola al inicio de sesión**.

## B. Usuarios y roles
- [ ] Administración → Usuarios: crear dos empleados y un supervisor; el empleado entra y ve solo "Fichar".
- [ ] El supervisor ve el panel de supervisor; el empleado no ve Administración.
- [ ] 3 PIN incorrectos bloquean al usuario; el administrador lo desbloquea.
- [ ] Cambiar mi PIN funciona y el viejo deja de servir.

## C. Fichaje personal
- [ ] El botón dice **Fichar entrada**; después de fichar pasa a **Fichar salida**.
- [ ] Con ubicación "Libre": fuera de un sitio conocido pide un motivo; dentro de uno cargado en `Sitios` no.
- [ ] Una ubicación simulada (app de GPS falso) se rechaza.
- [ ] Aparece el botón "Ver en Google Maps" tras fichar.
- [ ] Con foto obligatoria: sacar la foto y tocar la **tilde** no cierra la app (la foto se achica al leerla). Se abre **la cámara frontal dentro de la app** (botón "Sacar foto"); si el teléfono no tiene frontal o no se puede abrir, usa la cámara del sistema.
- [ ] Ficha **dos veces seguidas** (entrada y salida, una a continuación de la otra): las dos veces toma la ubicación. Si falla, el mensaje dice por qué (GPS apagado, sin permiso o sin lectura) y se puede reintentar.
- [ ] **GPS**: al abrir la pantalla de fichar el teléfono empieza a buscar señal; al tocar el botón se ve "Buscando señal GPS… ±NN m" y baja con los segundos. Al aire libre pasa casi enseguida; adentro puede tardar hasta 30 s. Si sale "no es lo bastante precisa", Administración → Cómo se ficha → **Precisión del GPS** → Flexible (100 m) y se prueba de nuevo. Si falla, el registro de la API dice la precisión medida ("Fichaje rechazado de …").
- [ ] El logo se ve **redondo** en el login y en la vista previa de Apariencia, también con uno subido por el administrador.
- [ ] Cada fichaje se ve como una fila en la hoja `Fichajes`.

## D. Cómo se ficha (administrador)
- [ ] Administración → "Cómo se ficha": cambiar Ubicación a **Ninguna** y Foto a **Obligatoria**, guardar.
- [ ] En el próximo fichaje no pide GPS y pide la foto; el aviso de consentimiento aparece solo la primera vez.
- [ ] Si el empleado no acepta el aviso, no se le saca la foto y no ficha.
- [ ] En la planilla, el fichaje tiene "Sin ubicación" como lugar y el nombre del archivo de la foto en la columna `O`.
- [ ] Dice cuántos días se guardan las fotos (90).

## E. Supervisor
- [ ] Hoy / Presentes / Historial muestran los fichajes; el filtro por empleado funciona.
- [ ] Tocar un fichaje con foto abre la foto.
- [ ] Los fichajes sin ubicación no rompen el mapa.

## F. Kiosco (un teléfono por sucursal)
- [ ] Por defecto el kiosco está **apagado**: en Usuarios no existe el rol Kiosco. Una cuenta de sucursal ya creada ve "El modo kiosco no está activado…".
- [ ] Administración → Cómo se ficha → Modo → **Kiosco** → Guardar: dice que ahora hay que crear una cuenta por sucursal, y el rol Kiosco aparece en Usuarios.
- [ ] Crear "Sucursal Centro" con el rol **Kiosco**; entrar con esa cuenta abre la pantalla de kiosco.
- [ ] La lista muestra a los empleados con **Entrada** o **Salida**; la cuenta de la sucursal **no** aparece.
- [ ] Tocar un nombre, PIN correcto (+ foto si corresponde): registra y cambia a Salida.
- [ ] PIN incorrecto: avisa y **no** cierra la sesión del teléfono; 3 errores bloquean a ese empleado.
- [ ] Cerrar y abrir la app: sigue en el kiosco sin pedir login.
- [ ] El fichaje queda en la planilla a nombre del empleado y con "Sucursal Centro" como lugar.

## G. Liquidación de horas
- [ ] Cargar el valor hora de cada empleado y un feriado.
- [ ] Con fichajes de entrada y salida, "Calcular" muestra horas e importe.
- [ ] Un fichaje incompleto (entrada sin salida) impide cerrar y dice cuál.
- [ ] Corregir el fichaje desde "Corregir fichajes" y poder cerrar el período **incluyendo hoy**.
- [ ] Marcar como pagada y compartir el CSV.

## H. Apariencia
- [ ] Cambiar nombre y colores, "Ver cómo queda", guardar: todos los teléfonos lo ven al volver a abrir la app.
- [ ] Subir un logo; "Volver a los originales" lo quita.

## I. Servidor y datos
- [ ] Reiniciar la API: sigue conectada y las fotos siguen ahí.
- [ ] Hay una copia de seguridad de la carpeta `datos`.
- [ ] La API **no arranca en producción** con el `Jwt__Secret` de ejemplo (comprobado).

## I2. Mensajes y estilo (ver [GUIA_DE_ESTILO.md](GUIA_DE_ESTILO.md))
- [ ] Con el teléfono en **modo avión**, cualquier pantalla muestra "No pudimos conectarnos. Revisá tu internet y probá de nuevo." (nunca un texto en inglés o con "Exception").
- [ ] Guardar algo (Apariencia, Cómo se ficha, valor hora, feriados) muestra un **snackbar verde** de unos 3 s, sin botón "Aceptar".
- [ ] Un error al guardar muestra un **snackbar rojo** con el motivo.
- [ ] Cerrar período, marcar como pagada, dar de baja, eliminar rol o feriado piden confirmación con el **verbo** en el botón ("Dar de baja", "Eliminar feriado") y dicen la consecuencia.
- [ ] Un PIN incorrecto en el kiosco dice "Ese PIN no coincide. Probá de nuevo.".
- [ ] Si el kiosco no puede cargar la lista (por ejemplo, el modo está apagado), aparece una **pantalla de error** con el botón "Reintentar", no una línea de texto.
- [ ] Si la planilla conectada se borró, "Conectar Google Sheets" muestra el aviso en un **banner rojo**.

## J. En el teléfono real
Ver [TELEFONO_REAL.md](TELEFONO_REAL.md): cámara, GPS al aire libre, huella, modo avión y rotación.
