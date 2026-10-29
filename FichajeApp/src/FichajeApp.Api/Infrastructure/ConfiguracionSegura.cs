namespace FichajeApp.Api.Infrastructure;

// Comprobaciones al arrancar: una API en producción con la clave de ejemplo permitiría fabricar tokens de cualquier usuario.
public static class ConfiguracionSegura
{
    public const string SecretoDeEjemplo = "una-clave-secreta-larga-y-random-de-al-menos-32-caracteres";
    public const int LargoMinimo = 32;

    // En desarrollo y pruebas se permite la de ejemplo; en producción hay que definir la propia (variable Jwt__Secret)
    public static void ExigirSecretoJwt(IConfiguration config, bool esDesarrollo)
    {
        var secreto = config["Jwt:Secret"];

        if (string.IsNullOrWhiteSpace(secreto))
            throw new InvalidOperationException("Falta Jwt:Secret. Definí la variable de entorno Jwt__Secret con 32 o más caracteres al azar.");

        if (secreto.Length < LargoMinimo)
            throw new InvalidOperationException($"Jwt:Secret es muy corto ({secreto.Length}). Usá {LargoMinimo} o más caracteres al azar.");

        if (!esDesarrollo && secreto == SecretoDeEjemplo)
            throw new InvalidOperationException(
                "Jwt:Secret tiene el valor de ejemplo de appsettings.json. Definí la variable de entorno Jwt__Secret con una clave propia " +
                "(32 o más caracteres al azar) antes de publicar la API.");
    }
}
