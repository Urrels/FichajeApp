using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;

namespace FichajeApp.Mobile.Services;

public class BiometricService
{
    public async Task<(bool exito, string mensaje)> AutenticarAsync()
    {
        var disponible = await CrossFingerprint.Current.IsAvailableAsync();
        if (!disponible)
            return (false, "Biometría no disponible en este dispositivo");

        var request = new AuthenticationRequestConfiguration(
            "Confirmar fichaje",
            "Verificá tu identidad para registrar el fichaje")
        {
            CancelTitle = "Cancelar",
            FallbackTitle = "Usar PIN"
        };

        var resultado = await CrossFingerprint.Current.AuthenticateAsync(request);

        return resultado.Authenticated
            ? (true, "Autenticado correctamente")
            : (false, $"Autenticación fallida: {resultado.Status}");
    }
}
