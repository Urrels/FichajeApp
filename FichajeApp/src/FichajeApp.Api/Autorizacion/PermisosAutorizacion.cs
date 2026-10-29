using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace FichajeApp.Api.Autorizacion;

// Autorización por permiso (no por rol): el token lleva los permisos del usuario como claims "permiso"
// y cada endpoint declara el que necesita con [RequierePermiso("...")]. Es el equivalente de
// SessionManager.TienePermiso de ingenieria_software, pero sin estado en el servidor.
public static class ClaimsPermisos
{
    public const string Permiso = "permiso";
    public const string Prefijo = "permiso:";
}

public class PermisoRequirement : IAuthorizationRequirement
{
    public string Permiso { get; }
    public PermisoRequirement(string permiso) => Permiso = permiso;
}

public class PermisoHandler : AuthorizationHandler<PermisoRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermisoRequirement requirement)
    {
        if (context.User.HasClaim(ClaimsPermisos.Permiso, requirement.Permiso))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

// Crea las políticas "permiso:<nombre>" a pedido, sin registrar una por cada permiso del catálogo
public class PermisoPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public PermisoPolicyProvider(IOptions<AuthorizationOptions> opciones) : base(opciones) { }

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(ClaimsPermisos.Prefijo, StringComparison.Ordinal))
        {
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermisoRequirement(policyName[ClaimsPermisos.Prefijo.Length..]))
                .Build();
        }
        return await base.GetPolicyAsync(policyName);
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class RequierePermisoAttribute : AuthorizeAttribute
{
    public RequierePermisoAttribute(string permiso) => Policy = ClaimsPermisos.Prefijo + permiso;
}
