using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FichajeApp.Api.Servicios;

// Envío de mails por SMTP (Gmail, Brevo, el correo del hosting, etc.). Se configura en la sección "Correo" de la
// configuración (en Azure: Configuración → Variables de entorno, con nombres como Correo__Host). Sin Host, no se envía nada.
public class OpcionesCorreo
{
    public const string Seccion = "Correo";

    public string Host { get; set; } = "";
    public int Puerto { get; set; } = 587;
    public string Usuario { get; set; } = "";
    public string Clave { get; set; } = "";
    // Dirección que aparece como remitente; si queda vacía se usa el Usuario
    public string Remitente { get; set; } = "";
    public string NombreRemitente { get; set; } = "HoraTrack";
    // Nombre de la app en los textos de los mails
    public string NombreApp { get; set; } = "HoraTrack";

    public bool Configurado => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Usuario);
}

public interface IEnviadorDeCorreo
{
    // false si no hay servidor de correo configurado
    bool Configurado { get; }

    // true si el mail salió; false si no se pudo (nunca lanza: un mail que falla no tiene que romper el alta ni la recuperación)
    Task<bool> EnviarAsync(string para, string asunto, string html, string texto);
}

public class SmtpEnviador : IEnviadorDeCorreo
{
    public bool Configurado => true;

    private readonly OpcionesCorreo _o;
    private readonly ILogger<SmtpEnviador> _log;

    public SmtpEnviador(IOptions<OpcionesCorreo> opciones, ILogger<SmtpEnviador> log)
    {
        _o = opciones.Value;
        _log = log;
    }

    public async Task<bool> EnviarAsync(string para, string asunto, string html, string texto)
    {
        try
        {
            var mensaje = new MimeMessage();
            mensaje.From.Add(new MailboxAddress(_o.NombreRemitente, string.IsNullOrWhiteSpace(_o.Remitente) ? _o.Usuario : _o.Remitente));
            mensaje.To.Add(MailboxAddress.Parse(para));
            mensaje.Subject = asunto;
            mensaje.Body = new BodyBuilder { HtmlBody = html, TextBody = texto }.ToMessageBody();

            using var cliente = new SmtpClient { Timeout = 10_000 };
            // 465 = TLS directo; 587 = STARTTLS
            await cliente.ConnectAsync(_o.Host, _o.Puerto, _o.Puerto == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls);
            await cliente.AuthenticateAsync(_o.Usuario, _o.Clave);
            await cliente.SendAsync(mensaje);
            await cliente.DisconnectAsync(true);
            return true;
        }
        catch (Exception ex)
        {
            // No se registra la dirección ni el contenido: son datos de una persona
            _log.LogWarning("No se pudo enviar un mail: {Tipo}", ex.GetType().Name);
            return false;
        }
    }
}

// Sin servidor de correo configurado: la app funciona igual, solo que no salen mails
public class EnviadorNulo : IEnviadorDeCorreo
{
    public bool Configurado => false;

    public Task<bool> EnviarAsync(string para, string asunto, string html, string texto) => Task.FromResult(false);
}

public static class PlantillasCorreo
{
    private static string Marco(string app, string cuerpoHtml) => $@"<!doctype html>
<html><body style=""margin:0;padding:24px;background:#FBF6F0;font-family:Arial,Helvetica,sans-serif;color:#2B2B2B"">
<div style=""max-width:480px;margin:0 auto;background:#FFFFFF;border:1px solid #E8DDD0;border-radius:12px;overflow:hidden"">
<div style=""background:#B5542B;color:#FFFFFF;padding:20px 24px;font-size:20px;font-weight:bold"">{WebUtility.HtmlEncode(app)}</div>
<div style=""padding:24px;font-size:15px;line-height:1.5"">{cuerpoHtml}</div>
</div></body></html>";

    public static (string Asunto, string Html, string Texto) Bienvenida(string app, string nombre)
    {
        var n = WebUtility.HtmlEncode(nombre);
        var html = Marco(app, $@"<p>¡Hola {n}!</p>
<p>Tu empresa te dio de alta en <b>{WebUtility.HtmlEncode(app)}</b>, la app para registrar tu entrada y tu salida desde el celular.</p>
<p>Para empezar:</p>
<ol><li>Instalá la app desde Google Play.</li><li>Ingresá con este email y el PIN que te dio tu empresa.</li><li>La primera vez te vamos a pedir una foto de tu cara.</li></ol>
<p style=""color:#6B6B6B;font-size:13px"">Por seguridad, este mail no incluye tu PIN. Si no esperabas este mail, ignoralo.</p>");
        var texto = $"¡Hola {nombre}!\n\nTu empresa te dio de alta en {app}, la app para registrar tu entrada y tu salida desde el celular.\n\n" +
                    "1) Instalá la app desde Google Play.\n2) Ingresá con este email y el PIN que te dio tu empresa.\n3) La primera vez te vamos a pedir una foto de tu cara.\n\n" +
                    "Por seguridad, este mail no incluye tu PIN. Si no esperabas este mail, ignoralo.";
        return ($"Bienvenido/a a {app}", html, texto);
    }

    public static (string Asunto, string Html, string Texto) CodigoDeRecuperacion(string app, string nombre, string codigo, int minutos)
    {
        var n = WebUtility.HtmlEncode(nombre);
        var html = Marco(app, $@"<p>Hola {n},</p>
<p>Pediste cambiar tu PIN. Tu código es:</p>
<p style=""font-size:32px;letter-spacing:8px;font-weight:bold;color:#B5542B;margin:16px 0"">{codigo}</p>
<p>Ingresalo en la app junto con tu PIN nuevo. Vale <b>{minutos} minutos</b>.</p>
<p style=""color:#6B6B6B;font-size:13px"">Si no lo pediste vos, ignorá este mail: tu PIN actual sigue funcionando.</p>");
        var texto = $"Hola {nombre},\n\nPediste cambiar tu PIN. Tu código es: {codigo}\n\nIngresalo en la app junto con tu PIN nuevo. Vale {minutos} minutos.\n\n" +
                    "Si no lo pediste vos, ignorá este mail: tu PIN actual sigue funcionando.";
        return ($"Tu código para cambiar el PIN de {app}", html, texto);
    }
}
