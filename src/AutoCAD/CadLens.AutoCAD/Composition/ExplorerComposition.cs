using CadLens.Lenses;
using CadLens.UI;
using Microsoft.Extensions.DependencyInjection;

namespace CadLens.AutoCAD;

internal static class ExplorerComposition
{
    internal static ServiceProvider Build(Action<IServiceCollection> registerHost)
    {
        var services = new ServiceCollection();
        services.AddScoped<IDrawingLensProvider, DrawingLensProvider>();
        services.AddScoped<ILens>(provider => new ObjectExplorerLens(
            provider.GetRequiredService<IObjectExplorerActions>(),
            DrawingGrouping.Layers));
        services.AddScoped<ILens>(provider => new ObjectExplorerLens(
            provider.GetRequiredService<IObjectExplorerActions>(),
            DrawingGrouping.ObjectTypes));
        services.AddScoped<ExplorerViewModel>();
        services.AddScoped<ExplorerWindow>();

        registerHost(services);

        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
    }
}