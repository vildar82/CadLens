using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CadLens.UI;

/// <summary>App-local resources and language preference; never changes host or thread cultures.</summary>
public sealed partial class UiText : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources = new("CadLens.UI.Localization.Strings", typeof(UiText).Assembly);
    private readonly string _preferencePath;
    private readonly Func<string> _windowsLanguage;

    /// <summary>Language shared by this CAD Lens process.</summary>
    public static UiText Current { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CadLens", "language.json"));

    /// <summary>Loads a preference from its file, falling back to Windows when absent or invalid.</summary>
    /// <param name="preferencePath">Per-user JSON preference file.</param>
    /// <param name="windowsLanguage">Windows display-language reader, replaceable in managed checks.</param>
    public UiText(string preferencePath, Func<string>? windowsLanguage = null)
    {
        _preferencePath = preferencePath;
        _windowsLanguage = windowsLanguage ?? ReadWindowsLanguage;
        Preference = ReadPreference();
        Culture = ResolveCulture();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Persisted mode, including the Windows-following mode.</summary>
    public LanguagePreference Preference { get; private set; }

    /// <summary>Culture used only for CAD Lens resources and display formatting.</summary>
    public CultureInfo Culture { get; private set; }

    /// <summary>Compact language indicator.</summary>
    public string LanguageCode => Culture.TwoLetterISOLanguageName.ToUpperInvariant();

    /// <summary>Whether Windows chooses the app language.</summary>
    public bool IsWindowsLanguage => Preference == LanguagePreference.Windows;

    /// <summary>Whether English was explicitly chosen.</summary>
    public bool IsEnglishLanguage => Preference == LanguagePreference.English;

    /// <summary>Whether Russian was explicitly chosen.</summary>
    public bool IsRussianLanguage => Preference == LanguagePreference.Russian;

    /// <summary>Visible explanation if a selected preference could not be saved.</summary>
    public string PreferenceError { get; private set; } = "";

    /// <summary>Looks up an app-owned phrase, with the supplied English phrase as fallback.</summary>
    /// <param name="english">English resource key and fallback.</param>
    public string Get(string english) => Resources.GetString(english, Culture) ?? english;

    /// <summary>Applies a choice immediately and optionally saves it for the next session.</summary>
    /// <param name="preference">Windows, English, or Russian.</param>
    /// <param name="persist">Whether to save the user choice.</param>
    public void Select(LanguagePreference preference, bool persist = true)
    {
        Preference = Enum.IsDefined(preference) ? preference : LanguagePreference.Windows;
        Culture = ResolveCulture();
        PreferenceError = "";

        if (persist)
            SavePreference();

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private LanguagePreference ReadPreference()
    {
        try
        {
            var value = JsonSerializer.Deserialize<string>(File.ReadAllText(_preferencePath));
            return Enum.TryParse<LanguagePreference>(value, out var preference) && Enum.IsDefined(preference)
                ? preference
                : LanguagePreference.Windows;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return LanguagePreference.Windows;
        }
    }

    private void SavePreference()
    {
        var temporary = _preferencePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_preferencePath))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(Preference.ToString()));
            File.Move(temporary, _preferencePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            PreferenceError = Get("Language changed for this session. The preference could not be saved.");
            System.Diagnostics.Trace.TraceWarning("Unable to save CAD Lens language: {0}", exception.Message);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.TraceWarning("Unable to remove temporary language preference: {0}", exception.Message);
            }
        }
    }

    private CultureInfo ResolveCulture()
    {
        var russian = Preference == LanguagePreference.Russian ||
            (Preference == LanguagePreference.Windows && _windowsLanguage().Split('-')[0].Equals("ru", StringComparison.OrdinalIgnoreCase));

        return CultureInfo.GetCultureInfo(russian ? "ru" : "en");
    }

    private static string ReadWindowsLanguage()
    {
        const uint languageName = 8;
        uint length = 0;

        if (!GetUserPreferredUILanguages(languageName, out _, IntPtr.Zero, ref length) || length == 0)
            return "en";

        var buffer = Marshal.AllocHGlobal(checked((int)length * sizeof(char)));

        try
        {
            return GetUserPreferredUILanguages(languageName, out _, buffer, ref length)
                ? Marshal.PtrToStringUni(buffer) ?? "en"
                : "en";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetUserPreferredUILanguages(uint flags, out uint languages, IntPtr buffer, ref uint length);
}