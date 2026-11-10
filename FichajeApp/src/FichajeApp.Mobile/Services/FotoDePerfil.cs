namespace FichajeApp.Mobile.Services;

// Foto de perfil con control de cara: si no se ve una cara real, se avisa y se deja volver a intentar.
// Una foto cualquiera (un paisaje, un logo, una foto de otra persona de lejos) rompería el reconocimiento posterior.
public static class FotoDePerfil
{
    // La foto en base64, o null si la persona canceló. Lanza PermissionException si negó la cámara.
    public static async Task<string?> TomarValidadaAsync()
    {
        while (true)
        {
            var foto = await FotoFichaje.TomarAsync();
            if (foto is null) return null;

            var caras = await DetectorDeCara.DetectarAsync(foto);
            // Si el detector no pudo correr (falla del teléfono) no se bloquea a nadie: se acepta la foto
            if (caras is null) return foto;

            var veredicto = ReglasDeCara.Evaluar(caras);
            if (veredicto == VeredictoCara.Valida) return foto;

            if (!await Dialogos.Confirmar("Esta foto no sirve", ReglasDeCara.Mensaje(veredicto), "Sacar otra"))
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
                await Dialogos.Aviso("No se pudo sacar la foto", TextosDeError.DeExcepcion(ex));
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
