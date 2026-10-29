namespace FichajeApp.Mobile.Services;

// Aviso que se le muestra a cada empleado la primera vez que ficha con foto en un teléfono: qué se hace con su foto y por
// cuánto tiempo. Si no está de acuerdo, no se le saca la foto. Queda recordado en el teléfono, por empleado.
public static class AvisoFoto
{
    public static async Task<bool> ConfirmarAsync(int empleadoId, int retencionDias)
    {
        var clave = $"aviso_foto_{empleadoId}";
        try { if (Preferences.Default.Get(clave, false)) return true; }
        catch { /* sin preferencias: se pregunta igual */ }

        var empresa = Application.Current?.Resources.TryGetValue("NombreCliente", out var nombre) == true
            ? nombre?.ToString() : null;
        var plazo = retencionDias > 0 ? $" Se borra sola a los {retencionDias} días." : string.Empty;

        var acepta = await Dialogos.Confirmar("Foto al fichar",
            $"Al fichar se saca una foto de tu cara. {(string.IsNullOrWhiteSpace(empresa) ? "La empresa" : empresa)} la usa solo para " +
            $"comprobar quién fichó y únicamente la puede ver un supervisor.{plazo}\n\n¿Estás de acuerdo?",
            "Sí, estoy de acuerdo");

        if (acepta)
        {
            try { Preferences.Default.Set(clave, true); }
            catch { /* se vuelve a preguntar la próxima vez */ }
        }
        return acepta;
    }
}
