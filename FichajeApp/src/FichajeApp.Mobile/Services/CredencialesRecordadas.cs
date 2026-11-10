namespace FichajeApp.Mobile.Services;

// "Recordar mis datos": el email y el PIN se guardan en SecureStorage (cifrado con el Keystore de Android)
// SOLO si la persona lo pide, y solo se leen después de confirmar con la huella o el bloqueo del teléfono.
// Cerrar sesión no los borra (para volver a entrar con la huella); se borran al destildar la opción,
// cuando el servidor rechaza los datos, o al desinstalar la app.
public static class CredencialesRecordadas
{
    private const string EmailKey = "recordar_email";
    private const string PinKey = "recordar_pin";

    public static async Task<bool> ExisteAsync()
    {
        try { return !string.IsNullOrEmpty(await SecureStorage.Default.GetAsync(PinKey)); }
        catch { return false; }   // el almacén seguro puede fallar si cambió el bloqueo del teléfono
    }

    public static async Task<string?> EmailAsync()
    {
        try { return await SecureStorage.Default.GetAsync(EmailKey); }
        catch { return null; }
    }

    public static async Task<(string Email, string Pin)?> ObtenerAsync()
    {
        try
        {
            var email = await SecureStorage.Default.GetAsync(EmailKey);
            var pin = await SecureStorage.Default.GetAsync(PinKey);
            return string.IsNullOrEmpty(email) || string.IsNullOrEmpty(pin) ? null : (email, pin);
        }
        catch { return null; }
    }

    public static async Task GuardarAsync(string email, string pin)
    {
        try
        {
            await SecureStorage.Default.SetAsync(EmailKey, email);
            await SecureStorage.Default.SetAsync(PinKey, pin);
        }
        catch { /* si el teléfono no puede guardarlo, simplemente no se recuerda */ }
    }

    // Después de cambiar el PIN: si había datos recordados, se actualizan para que la huella siga funcionando
    public static async Task ActualizarPinAsync(string pin)
    {
        if (await ExisteAsync()) { try { await SecureStorage.Default.SetAsync(PinKey, pin); } catch { } }
    }

    public static void Borrar()
    {
        try { SecureStorage.Default.Remove(EmailKey); SecureStorage.Default.Remove(PinKey); } catch { }
    }
}
