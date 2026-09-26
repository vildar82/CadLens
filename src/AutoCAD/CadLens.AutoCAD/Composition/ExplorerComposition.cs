using CadLens.Lenses;
using CadLens.UI;
using Microsoft.Extensions.DependencyInjection;

namespace CadLens.AutoCAD;

internal static class ExplorerComposition
{
    internal static ServiceProvider Build(Action<IServiceCollection> registerHost)
    {
        var services = new ServiceCollection();
        services.AddScoped<ILayersProvider, LayersLensProvider>();
        services.AddScoped<LayersViewModel>();
        services.AddScoped<ILens, LayersLens>();
        services.AddScoped<ExplorerViewModel>();
        services.AddScoped<ExplorerWindow>();

        registerHost(services);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
