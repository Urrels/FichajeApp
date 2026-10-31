# Guía de estilo: pantallas y mensajes

Reglas cortas para que toda la app se vea y suene igual. Los valores viven en `Resources/Styles/Tema.xaml` y los textos en
`Services/TextosDeError.cs`. Si una regla no está acá, copiá lo que ya hace una pantalla parecida.

## Espacios
Solo estos valores (múltiplos de 4): **4, 8, 12, 16, 24, 32** (`Esp1` a `Esp8`).
Margen de pantalla 16 a 24. Dentro de un grupo, 8; entre grupos, 24. Zonas táctiles de 48 como mínimo.

## Jerarquía
- Un solo botón principal por pantalla (el de la marca). Lo secundario usa `BotonSecundario`; lo destructivo, `BotonPeligro`.
- Tres niveles de texto: **título** (`TextoTitulo`), cuerpo (el predeterminado) y **aclaración** (`TextoAclaracion`).
  Para destacar, cambiá el tamaño o el peso, no los dos.
- Grupos de campos o datos: dentro de una tarjeta (`Style="{StaticResource Tarjeta}"`), no repetir bordes y colores a mano.

## Color
60 % fondo neutro, 30 % tarjetas blancas, 10 % acento de la marca. El color de la marca solo en acciones y estados activos.
Verde, ámbar y rojo **solo** para éxito, aviso y error. Nunca se comunica algo solo con el color: también va el texto o un ícono.

## Qué componente usar para avisar

| Situación | Componente |
|---|---|
| La pantalla no puede cumplir su función (sin conexión al abrir, servidor caído, planilla desconectada) | `EstadoDeError` (pantalla completa, con "Reintentar") |
| Dato mal escrito | Mensaje en línea bajo el campo |
| Problema recuperable que se queda a la vista | `BannerInline` (Info / Aviso / Error / Éxito) |
| Salió bien | `IFeedback.ExitoAsync` (snackbar de 3 s, sin botón) |
| Falló una acción chica | `IFeedback.ErrorAsync(texto, reintentar)` (snackbar con "Reintentar") |
| Hay que decidir, o es irreversible | `IFeedback.ConfirmarAsync` (modal) |

Confirmaciones: el título dice qué se va a hacer, el texto dice la consecuencia y si se puede deshacer, y el botón lleva el
verbo ("Dar de baja", "Eliminar feriado"), nunca "Sí" ni "Aceptar". Si es reversible, mejor ejecutar y ofrecer "Deshacer".

## Cómo se escribe
1. Qué pasó + qué puede hacer. Sin culpar a la persona.
2. Voseo ("probá", "revisá", "entrá"), frases de hasta dos líneas.
3. **Nunca** mostrar el texto técnico de una excepción ni códigos: se traduce con `TextosDeError.DeExcepcion` o `Para(codigo, mensaje)`.
4. Botones con verbo: "Guardar", "Reintentar", "Fichar entrada".

| En vez de | Escribir |
|---|---|
| Error de conexión: Unable to resolve host… | No pudimos conectarnos. Revisá tu internet y probá de nuevo. |
| PIN incorrecto | Ese PIN no coincide. Probá de nuevo. |
| Sesión expirada, iniciá sesión de nuevo | Por seguridad cerramos tu sesión. Entrá de nuevo para seguir. |
| Entrada registrada correctamente | ¡Listo! Entrada registrada a las 08:02. |
| Esto no se puede deshacer. [Marcar] | ¿Marcar como pagada? Una vez marcada, ya no se puede volver atrás. [Marcar como pagada] |

## Para agregar un mensaje nuevo
1. ¿Es un error que viene del servidor con `codigo`? Agregá el caso en `TextosDeError.Para`.
2. ¿Es de la app? Agregá una constante en `TextosDeError` y un caso en `TextosDeErrorTests` (los tests revisan que sea corto, termine
   en punto y no tenga jerga).
3. Mostralo con `IFeedback` o con `EstadoDeError`/`BannerInline`, no con `DisplayAlert` suelto.

## Pantallas pulidas (PR B)

- **Menús**: las opciones de Administración y de "Cómo se ficha" son tarjetas `OpcionDeMenu` (título, descripción, valor actual y flecha), agrupadas por tema con `EncabezadoSeccion`. No se apilan botones iguales.
- **Login**: el error se muestra en un `BannerInline` rojo; "Listo" en el teclado del PIN entra.
- **Fichaje y kiosco**: al registrar se ve una tarjeta verde grande con el tipo y la hora (en kiosco, también el nombre) y una vibración corta; se oculta sola. Los errores van en banner rojo y el avance ("Obteniendo ubicación…") en texto común.
- **Listas**: toda lista lleva `EstadoVacio` como `EmptyView` con un título y qué hacer.