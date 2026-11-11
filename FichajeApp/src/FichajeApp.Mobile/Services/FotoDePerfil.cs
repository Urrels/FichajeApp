namespace FichajeApp.Mobile.Services;

// Foto de perfil con control de cara: si no se ve una cara real, se avisa y se deja volver a intentar.
// Una foto cualquiera (un paisaje, un logo, una foto de otra persona de lejos) rompería el reconocimiento posterior.
public static class FotoDePerfil
{
    // La foto en base64, o null si la persona canceló. Lanza PermissionException si negó la cámara.
    // estricto (selfie del fichaje): si el detector no puede revisar la foto, NO se acepta. Sin eso se podía fichar
    // sacándole una foto a cualquier cosa.
    public static async Task<string?> TomarValidadaAsync(bool estricto = false)
    {
        while (true)
        {
            var foto = await FotoFichaje.TomarAsync();
            if (foto is null) return null;

            var caras = await DetectorDeCara.DetectarAsync(foto);
            if (caras is null)
            {
                // Falla del detector en el teléfono: la foto de perfil se acepta (no se bloquea a nadie), el fichaje no
                if (!estricto) return foto;
                if (!await Dialogos.Confirmar("No pudimos revisar la foto",
                        "No se pudo comprobar que se vea tu cara. Probá de nuevo con buena luz.\n\nDetalle para soporte: " + DetectorDeCara.Diagnostico,
                        "Sacar otra"))
                    return null;
                continue;
            }

            var veredicto = ReglasDeCara.Evaluar(caras);
            if (veredicto == VeredictoCara.Valida) return foto;

            // Al fichar, una foto rechazada también le llega al supervisor (puede ser alguien intentando engañar al control)
            if (estricto)
                _ = IPlatformApplication.Current?.Services.GetService<ApiService>()?.ReportarAlertaAsync("cara");

            // Al fichar se agrega el detalle de la revisión: si una cara real se rechaza, con eso se ve por qué
            var mensaje = ReglasDeCara.Mensaje(veredicto) + (estricto ? $"\n\nDetalle para soporte: {DetectorDeCara.Diagnostico}" : "");
            if (!await Dialogos.Confirmar("Esta foto no sirve", mensaje, "Sacar otra"))
                return null;
        }
    }

    // Después de iniciar sesión: quien no tiene foto de perfil tiene que sacarse una antes de usar la app.
    // Se omite en el kiosco (teléfono compartido) y si el teléfono no tiene cámara.
    public static async Task ExigirSiFaltaAsync(PerfilFotos perfiles, AuthService auth)
    {
        var sesion = SessionManager.Instance;
        if (sesion.EsKiosco || !FotoFichaje.Disponible) return;

        // null = no se pudo consultar (sin conexión): no se insiste ahora, se vuelve a chequear en el próximo ingreso
        if (await perfiles.TieneFotoAsync(sesion.EmpleadoId ?? 0) != false) return;

        await Dialogos.Aviso("Falta tu foto de perfil",
            "Para usar la app necesitamos una foto de tu cara. Se usa para identificarte en tus fichajes. " +
            "Mirá de frente a la cámara, con buena luz.");

        while (true)
        {
            string? foto;
            try { foto = await TomarValidadaAsync(); }
            catch (Exception ex)
            {
                // Negar el permiso de cámara no es "algo salió mal de nuestro lado": se le dice qué hacer
                var mensaje = ex is PermissionException ? TextosDeError.CamaraNoDisponible : TextosDeError.DeExcepcion(ex);
                await Dialogos.Aviso("No se pudo sacar la foto", mensaje);
                foto = null;
            }

            if (foto is not null)
            {
                var r = await perfiles.GuardarAsync(null, foto);
                if (r.Exito) return;
                await Dialogos.Aviso("No se pudo guardar la foto", r.Mensaje);
                continue;
            }

            // Canceló: la foto es obligatoria, pero nadie queda atrapado: puede salir de la sesión
            if (!await Dialogos.Confirmar("Foto obligatoria", "Sin tu foto no podés usar la app.", "Sacar la foto"))
            {
                auth.Logout();
                return;
            }
        }
    }
}
