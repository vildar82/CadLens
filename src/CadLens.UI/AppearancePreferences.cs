using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CadLens.UI;

/// <summary>Per-user appearance choices, independent of drawings and lens sessions.</summary>
public sealed class AppearancePreferences : ObservableObject
{
    private string _theme = "Follow AutoCAD";
    private string _palette = "Quiet";
    private string _accent = "Mint";

    /// <summary>Loads saved choices; missing or damaged settings use the defaults.</summary>
    /// <param name="settings">Optional isolated settings service, primarily for managed tests.</param>
    public AppearancePreferences(SettingsService? settings = null)
    {
        SettingsStore = settings ?? SettingsService.Current;
        Load();
    }

    internal SettingsService SettingsStore { get; }

    /// <summary>Available base themes.</summary>
    public ImmutableArray<string> Themes { get; } = ["Follow AutoCAD", "Light", "Dark"];

    /// <summary>Available surface palettes.</summary>
    public ImmutableArray<string> Palettes { get; } = ["Quiet", "Graphite", "Paper"];

    /// <summary>Available accents with separate light and dark variants.</summary>
    public ImmutableArray<string> Accents { get; } = ["Mint", "Blue", "Violet", "Amber"];

    /// <summary>The base theme; Follow AutoCAD uses the host's COLORTHEME setting.</summary>
    public string Theme
    {
        get => _theme;
        set
        {
            if (Themes.Contains(value) && SetProperty(ref _theme, value))
                Save();
        }
    }

    /// <summary>The surface palette.</summary>
    public string Palette
    {
        get => _palette;
        set
        {
            if (Palettes.Contains(value) && SetProperty(ref _palette, value))
                Save();
        }
    }

    /// <summary>The accent color.</summary>
    public string Accent
    {
        get => _accent;
        set
        {
            if (Accents.Contains(value) && SetProperty(ref _accent, value))
                Save();
        }
    }

    /// <summary>Explains when choices can only be kept for the current session.</summary>
    public bool SaveFailed
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Restores Follow AutoCAD, Quiet surfaces, and the Mint accent.</summary>
    public void Reset()
    {
        _theme = "Follow AutoCAD";
        _palette = "Quiet";
        _accent = "Mint";
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(Palette));
        OnPropertyChanged(nameof(Accent));
        Save();
    }

    private void Load()
    {
        var settings = SettingsStore.Load<Settings>("appearance.json");

        if (settings is null)
            return;

        _theme = Themes.Contains(settings.Theme) ? settings.Theme : _theme;
        _palette = Palettes.Contains(settings.Palette) ? settings.Palette : _palette;
        _accent = Accents.Contains(settings.Accent) ? settings.Accent : _accent;
    }

    private void Save() => SaveFailed = !SettingsStore.Save("appearance.json", new Settings(Theme, Palette, Accent));

    private sealed record Settings(string Theme, string Palette, string Accent);
}
