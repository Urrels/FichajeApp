# Prueba completa, de punta a punta

Hasta ahora casi todo se probó con tests automáticos y compilando, **no con una planilla real y un teléfono**. Esta lista sirve para
recorrerlo todo una vez antes de dárselo a un cliente. Marcá cada punto; si algo falla, anotá el mensaje exacto de la pantalla.

Usá una **planilla de Google nueva y vacía** y los datos de prueba (administrador `admin@prueba.com`, PIN `1234`).
Para empezar de cero: `appsettings.Development.json` con `"SpreadsheetId": ""` y `"Setup": { "Clave": "prueba123" }`,
borrar la carpeta `src/FichajeApp.Api/datos` y reiniciar la API.

## A. Conectar la planilla (como lo haría un cliente)
- [ ] La app, al abrir, te lleva sola a "Conectar Google Sheets" (o con el botón del login).
- [ ] La pantalla muestra el correo de la cuenta de servicio para compartir la planilla; "Copiar correo" funciona.
- [ ] Con la planilla **sin compartir**: el mensaje dice con qué correo hay que compartirla.
- [ ] Con un código de activación incorrecto: dice que no es correcto.
- [ ] Con todo bien: se guarda y aparecen en la planilla las pestañas `Empleados`, `Fichajes`, `Sitios`, `Roles`, `Permisos`, `Parametros`, etc.
- [ ] Entrás con el administrador que cargaste.
- [ ] Desde Administración → "Conexión con Google Sheets" se puede volver a abrir (solo el administrador).
- [ ] Sin sesión de administrador, intentar reconectar con el código **ya no funciona** ("La planilla ya está conectada…").

- [ ] (Teléfono) 5 toques al logo del login cambian la dirección del servidor; una dirección mala se rechaza y una caída avisa "Guardada, pero no responde"; vacía vuelve a la original.

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

## J. En el teléfono real
Ver [TELEFONO_REAL.md](TELEFONO_REAL.md): cámara, GPS al aire libre, huella, modo avión y rotación.
