using CadLens.Lenses;
using CadLens.UI;
using Microsoft.Extensions.DependencyInjection;

namespace CadLens.AutoCAD;

internal static class ExplorerComposition
{
    internal static ServiceProvider Build(Action<IServiceCollection> registerHost)
    {
        var services = new ServiceCollection();
        services.AddSingleton(SettingsService.Current);
        services.AddScoped<IDrawingLensProvider, DrawingLensProvider>();
        services.AddScoped<ILens>(provider => new ObjectExplorerLens(
            provider.GetRequiredService<IObjectExplorerActions>(),
            DrawingGrouping.Layers,
            provider.GetRequiredService<SettingsService>()));
        services.AddScoped<ILens>(provider => new ObjectExplorerLens(
            provider.GetRequiredService<IObjectExplorerActions>(),
            DrawingGrouping.ObjectTypes,
            provider.GetRequiredService<SettingsService>()));
        services.AddScoped<ExplorerViewModel>();
        services.AddScoped<AppearancePreferences>();
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
