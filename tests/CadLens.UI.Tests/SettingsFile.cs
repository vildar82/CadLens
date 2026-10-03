namespace CadLens.UI.Tests;

internal sealed class SettingsFile : IDisposable
{
    internal string Directory { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "CadLens.Settings.Tests",
        Guid.NewGuid().ToString("N"));

    internal string Path => System.IO.Path.Combine(Directory, "appearance.json");

    internal SettingsService Service { get; }

    internal SettingsFile()
    {
        System.IO.Directory.CreateDirectory(Directory);
        Service = new SettingsService(Directory);
    }

    public void Dispose() => System.IO.Directory.Delete(Directory, true);
}