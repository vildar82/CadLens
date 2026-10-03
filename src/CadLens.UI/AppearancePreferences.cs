using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CadLens.UI;

/// <summary>Per-user appearance choices, independent of drawings and lens sessions.</summary>
public sealed class AppearancePreferences : ObservableObject
{
    private readonly string _settingsPath;
    private string _theme = "Follow AutoCAD";
    private string _palette = "Quiet";
    private string _accent = "Mint";

    /// <summary>Loads saved choices; missing or damaged settings use the defaults.</summary>
    /// <param name="settingsPath">Optional isolated settings file, primarily for managed tests.</param>
    public AppearancePreferences(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CadLens",
            "appearance.json");
        Load();
    }

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
        try
        {
            if (!File.Exists(_settingsPath))
                return;

            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath));

            if (settings is null)
                return;

            _theme = Themes.Contains(settings.Theme) ? settings.Theme : _theme;
            _palette = Palettes.Contains(settings.Palette) ? settings.Palette : _palette;
            _accent = Accents.Contains(settings.Accent) ? settings.Accent : _accent;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Trace.TraceWarning(
                "CAD Lens appearance settings could not be read: {0}",
                exception.Message);
        }
    }

    private void Save()
    {
        var temporaryPath = _settingsPath + $".{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_settingsPath))!);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new Settings(Theme, Palette, Accent)));
            File.Move(temporaryPath, _settingsPath, true);
            SaveFailed = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning(
                "CAD Lens appearance settings could not be saved: {0}",
                exception.Message);
            SaveFailed = true;
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.TraceWarning(
                    "CAD Lens temporary preferences could not be removed: {0}",
                    exception.Message);
            }
        }
    }

    private sealed record Settings(string Theme, string Palette, string Accent);
}
