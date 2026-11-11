# Prueba completa, de punta a punta

Hasta ahora casi todo se probó con tests automáticos y compilando, **no con una base real y un teléfono**. Esta lista sirve para
recorrerlo todo una vez antes de dárselo a un cliente. Marcá cada punto; si algo falla, anotá el mensaje exacto de la pantalla.

Usá una **base de datos nueva y vacía** y los datos de prueba (administrador `admin@prueba.com`, PIN `1234`, definidos con
`Setup__AdminNombre`, `Setup__AdminEmail` y `Setup__AdminPin`).

## A. Primer arranque
- [ ] Con la base vacía, al arrancar la API se crean las tablas y el administrador. `GET /api/configuracion/estado` responde `configurada: true`.
- [ ] Entrás con el administrador y ves **su panel** (Supervisor/Administración), no la pantalla del kiosco.
- [ ] En el login **no** aparece ningún botón para conectar nada, y Administración no tiene ninguna opción de conexión.
- [ ] (Teléfono) 5 toques al logo del login cambian la dirección del servidor; una dirección mala se rechaza y una caída avisa "no responde".
- [ ] (Teléfono en modo oscuro) los textos se leen bien: la app siempre se ve clara.

## B. Usuarios y roles
- [ ] Administración → Usuarios: crear dos empleados y un supervisor; el empleado entra y ve solo "Fichar".
- [ ] El supervisor ve el panel de supervisor; el empleado no ve Administración.
- [ ] 3 PIN incorrectos frenan la cuenta 15 minutos (ni el PIN correcto entra) y el mensaje es el mismo que con un mail inexistente; pasado ese tiempo vuelve a entrar sola.
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
- [ ] El fichaje queda con "Sin ubicación" como lugar y con el nombre del archivo de la foto.
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
- [ ] PIN incorrecto: avisa y **no** cierra la sesión del teléfono; 3 errores frenan a ese empleado 15 minutos.
- [ ] Cerrar y abrir la app: sigue en el kiosco sin pedir login.
- [ ] El fichaje queda a nombre del empleado y con "Sucursal Centro" como lugar.

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

## J. En el teléfono real
Ver [TELEFONO_REAL.md](TELEFONO_REAL.md): cámara, GPS al aire libre, huella, modo avión y rotación.

## M. Pantallas pulidas
- [ ] Login con PIN mal: aparece un banner rojo (no texto suelto) y "Listo" del teclado entra.
- [ ] Administración muestra tarjetas agrupadas (Personas, Asistencia y pagos, Configuración, Mi cuenta) y solo las que el rol permite; los títulos de grupo sin opciones no aparecen.
- [ ] Cómo se ficha: cada tarjeta muestra el valor actual y al tocarla se cambia.
- [ ] Fichar: tarjeta verde con "Entrada/Salida registrada" y hora, vibración corta, se oculta a los segundos. Un error (sin GPS, sin foto) sale en banner rojo.
- [ ] Kiosco: tras fichar, tarjeta verde con nombre y hora; buscar un nombre inexistente muestra "No encontramos ese nombre".
- [ ] Listas vacías (Bitácora, Fichajes a corregir, Liquidación, Roles, Usuarios) muestran un cartel con qué hacer en lugar de una pantalla en blanco.
## N. Feriados oficiales
- [ ] Liquidación → Configuración → Feriados → **Actualizar feriados oficiales**: dice cuántos se agregaron y la lista pasa a mostrar los del año actual y el siguiente. Repetirlo dice que ya estaban al día.
- [ ] Los puentes y no laborables figuran como `(opcional)`. Una persona que trabaja un puente cobra sin recargo; en Parámetros → *Recargo en puentes* se puede activar.
- [ ] Un feriado cargado a mano con la misma fecha que uno oficial conserva su nombre después de actualizar.
- [ ] Con el servidor sin internet, *Actualizar* muestra el aviso de conexión y no cambia nada.

## O. Informes de liquidaciones cerradas
- [ ] Liquidación → **Cerradas** → elegir un período: aparecen *Descargar PDF* y *Descargar CSV (Excel)*.
- [ ] El PDF se abre en el teléfono y muestra el resumen por empleado, el total y el detalle por día. El CSV se abre en Excel con las columnas separadas.
- [ ] Con muchos empleados el PDF sigue en una segunda página y cada página dice *Página X de N*.

## P. Foto de perfil
- [ ] En Fichar, tocar la foto redonda de arriba a la derecha: *Sacar una foto nueva* abre la cámara frontal y la foto queda en el encabezado. *Quitar mi foto* vuelve a las iniciales.
- [ ] Administración → Usuarios → una persona → *Foto de perfil*: el administrador le saca o le quita la foto.
- [ ] La foto se ve en la lista de Usuarios y en la lista del kiosco; en otro teléfono aparece sin hacer nada (se baja sola).
- [ ] Cambiar la foto desde un teléfono y abrir la lista en otro: a lo sumo en un minuto se ve la nueva.

## Q. Lugares conocidos (solo con base SQL)
- [ ] Administración → *Lugares conocidos* → *Agregar lugar* → «Estoy en el lugar ahora»: pide el nombre y el radio y el lugar aparece en la lista con sus coordenadas.
- [ ] Tocar un lugar: cambiar el nombre, cambiar el radio, mover a mi ubicación actual, ver en el mapa y eliminar (pide confirmación).
- [ ] Con «Ubicación libre» activada, fichar dentro del radio queda con el nombre del lugar y sin pedir motivo; fichar lejos pide el motivo.
- [ ] Al eliminar un lugar, fichar ahí vuelve a pedir el motivo enseguida (sin esperar).
- [ ] Un nombre repetido, un radio fuera de 10 a 5000 m o una ubicación inválida se rechazan con un mensaje claro.

## R. Seguridad de roles y sesiones
- [ ] Un usuario con un rol que solo tenga «Administrar usuarios» no puede asignarse el rol Administrador, ni crear un usuario Administrador: aparece «No podés dar ni quitar el rol…».
- [ ] Ese mismo usuario sí puede crear usuarios y asignar roles que tengan solo permisos que él tiene (por ejemplo Empleado).
- [ ] En Administración → Roles, quien edita un rol no puede sumarle permisos que él no tiene (sí puede quitar).
- [ ] Si el administrador le cambia el rol a alguien (o le quita un permiso a su rol), la sesión abierta de esa persona se cierra en su próximo pedido y tiene que volver a entrar.
- [ ] Dar de baja o bloquear a alguien le corta la sesión enseguida (sin esperar un minuto).

## T. Selector de colores y avisos de error
- [ ] Administración → Apariencia → Colores: tocar Principal, Texto o Acento marca cuál se edita (borde oscuro).
- [ ] Arrastrar el dedo por el cuadro (más pálido o intenso, más claro o oscuro) y por la barra de tono: el código `#RRGGBB`, la muestra y **toda la app** (encabezado y barra de estado) cambian mientras se arrastra, y la página no se desplaza.
- [ ] Tocar un color sugerido lo aplica y mueve el cuadro y la barra.
- [ ] Escribir un código a mano (con o sin `#`) también actualiza todo.
- [ ] Cambiar el color principal ajusta solo el texto (blanco u oscuro) para que se lea. Elegir un texto a mano lo deja fijo y aparece «Que el texto se ajuste solo», que lo devuelve a automático.
- [ ] Con un texto que no se lee sobre el principal aparece el cartel rojo y Guardar queda apagado.
- [ ] Salir de la pantalla sin guardar devuelve la app a los colores guardados; Guardar los deja para todos.
- [ ] Los carteles de error rojos (login, Fichar, Kiosco, Configuración, Apariencia) tienen el texto **centrado**, con la cruz a la izquierda.

## S. Ubicación siempre lista
- [ ] Al abrir **Fichar** por primera vez, el teléfono pide el permiso de ubicación enseguida (no recién al tocar el botón). Elegir «Mientras la app está en uso».
- [ ] Con la pantalla de Fichar abierta unos segundos, tocar el botón: la ubicación no hace esperar (con buena señal, instantáneo; adentro de un edificio, un par de segundos).
- [ ] Mientras Fichar está a la vista aparece el ícono de ubicación arriba; al ir a otra pantalla o a la pantalla de inicio del teléfono, desaparece (la app no rastrea en segundo plano).
- [ ] Volver a la app desde segundo plano retoma la ubicación sola.
- [ ] Dejar la pantalla de Fichar abierta más de 10 minutos y tocar el botón igual ficha (con una espera corta si hace falta).
- [ ] Con el GPS del teléfono apagado, fichar muestra «El GPS está apagado…».

## Recorrido durante la jornada (opcional)

- [ ] Administración → Cómo se ficha → **Recorrido**: Activado → Guardar. Viene **Desactivado** de fábrica.
- [ ] Con un empleado: al fichar la **entrada** aparece el aviso «Registro de tu recorrido». «Cancelar» no activa nada y vuelve a preguntar la próxima entrada; «Acepto» pide el permiso de ubicación (y el de notificaciones en Android 13+).
- [ ] Con la jornada abierta aparece la **notificación fija** «Registrando tu recorrido». Pasar la app a segundo plano y cerrarla: la notificación sigue.
- [ ] Pasados unos minutos, Supervisor → Historial → elegir la persona → **Ver recorrido** muestra puntos unidos por una línea.
- [ ] Al fichar la **salida** la notificación desaparece y no se guardan más puntos.
- [ ] Cerrar sesión con la jornada abierta también detiene el registro.
- [ ] Con el recorrido **Desactivado** nunca aparece el aviso ni la notificación.
- [ ] Reabrir la app con la jornada abierta retoma el registro (sin volver a pedir aceptación).
