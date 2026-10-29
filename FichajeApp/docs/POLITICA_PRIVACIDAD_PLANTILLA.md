# Política de privacidad: plantilla

> **Esto es una plantilla, no asesoramiento legal.** Play Store exige una política de privacidad en una dirección web pública.
> Completá los `[CORCHETES]`, revisala con un abogado de tu país (en Argentina rige la Ley 25.326 de Protección de los Datos
> Personales; si tu cliente está en otro país, rige la suya) y publicala en una página web (por ejemplo una página de GitHub Pages
> o de Google Sites). Una política por cada cliente: el responsable de los datos es **la empresa del cliente**.

---

## Política de privacidad de [NOMBRE DE LA APP]

Última actualización: [FECHA]

**Responsable de los datos:** [EMPRESA DEL CLIENTE], [DOMICILIO], contacto: [EMAIL].
**Proveedor de la aplicación:** [TU NOMBRE O EMPRESA], contacto: [TU EMAIL].

### Qué datos se recopilan
- **Datos de la persona empleada:** nombre y apellido, email y un PIN (se guarda cifrado, nunca en texto).
- **Fichajes:** fecha y hora de cada entrada y salida.
- **Ubicación precisa** (GPS) al momento de fichar, si la empresa lo activó. Solo cuando la persona toca "Fichar"; la app no
  sigue la ubicación en segundo plano.
- **Foto de la cara** al fichar, si la empresa lo activó.
- **Huella o PIN del teléfono:** se usa solo para confirmar que quien ficha es la persona; la app no recibe ni guarda la huella.

### Para qué se usan
Registrar la asistencia, verificar quién fichó, y calcular las horas trabajadas y el pago. No se usan para publicidad ni se venden.

### Quién puede ver los datos
Los supervisores y administradores de [EMPRESA DEL CLIENTE]. Las **fotos** las ve únicamente un supervisor.

### Dónde se guardan y por cuánto tiempo
- Fichajes, datos de empleados y liquidaciones: en una **planilla de Google Sheets** de la empresa y en el servidor de la aplicación.
- Fotos: en el servidor de la aplicación, y **se borran automáticamente a los [90] días**.
- Los datos se transmiten cifrados (HTTPS).
- Los datos de la planilla se conservan mientras dure la relación laboral y el plazo que exija la ley.

### Derechos
Podés pedir a [EMPRESA DEL CLIENTE], a [EMAIL], acceder a tus datos, corregirlos o que se supriman cuando corresponda.
[Si en tu país hay una autoridad de control, mencionala. En Argentina: Agencia de Acceso a la Información Pública.]

### Menores
La aplicación es para personal adulto de la empresa.

### Cambios
Si cambia esta política, se publicará la nueva versión en esta página.

---

## Qué responder en Play Console → "Seguridad de los datos"

| Pregunta | Respuesta sugerida |
|---|---|
| ¿Recopila datos? | Sí |
| ¿Ubicación aproximada / precisa? | **Ubicación precisa**: recopilada, no compartida con terceros, opcional según el cliente. Finalidad: funcionalidad de la app |
| Nombre, email | Recopilados; finalidad: funcionalidad y administración de la cuenta |
| Fotos | **Fotos**: recopiladas (si el cliente las activa); finalidad: funcionalidad de la app |
| Información de empleo / otros datos | Registros de asistencia: funcionalidad |
| ¿Se cifran en tránsito? | Sí (HTTPS) |
| ¿Se pueden pedir que se borren? | Sí (contactando a la empresa) |
| ¿Se comparten con terceros? | No (el almacenamiento en Google Sheets es del propio cliente) |

Permisos que la app declara: **ubicación precisa en primer plano**, **cámara** (opcional), **biometría**. No usa ubicación en segundo plano.
