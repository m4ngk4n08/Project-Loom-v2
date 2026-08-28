using Microsoft.Extensions.DependencyInjection;

namespace Loom.Security;

public static class SecurityServiceExtensions
{
    /// <summary>Manual registration - no assembly scanning (AOT). Throws at startup if
    /// the key or users file is missing: fail closed, in every environment.</summary>
    public static IServiceCollection AddLoomSecurity(this IServiceCollection services)
    {
        var key = KeyMaterial.LoadSigningKey(KeyMaterial.ResolveKeyFile());
        var users = UserStore.Load(KeyMaterial.ResolveUsersFile());

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(users);
        services.AddSingleton(new JwtValidator(key, TimeProvider.System));
        services.AddSingleton(new JwtIssuer(key, TimeProvider.System));
        services.AddSingleton(new LoginThrottle(TimeProvider.System));
        return services;
    }
}
