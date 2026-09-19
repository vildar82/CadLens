using CadLens.Core;
using CadLens.Lenses;
using CadLens.UI;
using Microsoft.Extensions.DependencyInjection;

namespace CadLens.AutoCAD;

internal static class ExplorerComposition
{
    internal static ServiceProvider Build(Action<IServiceCollection> registerHost)
    {
        var services = new ServiceCollection();
        services.AddScoped<NavigationState>();
        services.AddScoped<ILensProvider, LayersLensProvider>();
        services.AddScoped<VerificationViewModel>();
        services.AddScoped<VerificationWindow>();

        registerHost(services);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}