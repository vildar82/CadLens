using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Runtime.InteropServices;

namespace CadLens.UI;

/// <summary>App-local resources and language preference; never changes host or thread cultures.</summary>
// ReSharper disable once PartialTypeWithSinglePart -- LibraryImport generates another part for modern targets.
public sealed partial class UiText : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources = new("CadLens.UI.Localization.Strings", typeof(UiText).Assembly);
    private readonly SettingsService _settings;
    private readonly Func<string> _windowsLanguage;

    /// <summary>Language shared by this CAD Lens process.</summary>
    public static UiText Current { get; } = new(SettingsService.Current);

    /// <summary>Loads a preference from its file, falling back to Windows when absent or invalid.</summary>
    /// <param name="settings">Per-user JSON settings service.</param>
    /// <param name="windowsLanguage">Windows display-language reader, replaceable in managed checks.</param>
    public UiText(SettingsService settings, Func<string>? windowsLanguage = null)
    {
        _settings = settings;
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
        Preference = IsValidPreference(preference) ? preference : LanguagePreference.Windows;
        Culture = ResolveCulture();
        PreferenceError = "";

        if (persist)
            SavePreference();

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private static bool IsValidPreference(LanguagePreference preference) =>
#if NETFRAMEWORK
        Enum.IsDefined(typeof(LanguagePreference), preference);
#else
        Enum.IsDefined(preference);
#endif

    private LanguagePreference ReadPreference()
    {
        var value = _settings.Load<string>("language.json");
        return Enum.TryParse<LanguagePreference>(value, out var preference) && IsValidPreference(preference)
            ? preference
            : LanguagePreference.Windows;
    }

    private void SavePreference()
    {
        if (!_settings.Save("language.json", Preference.ToString()))
            PreferenceError = Get("Language changed for this session. The preference could not be saved.");
    }

    private CultureInfo ResolveCulture()
    {
        var russian = Preference == LanguagePreference.Russian ||
                      (Preference == LanguagePreference.Windows &&
                       _windowsLanguage().Split('-')[0].Equals("ru", StringComparison.OrdinalIgnoreCase));

        return CultureInfo.GetCultureInfo(russian ? "ru" : "en");
    }

    private static string ReadWindowsLanguage()
    {
        const uint languageName = 8;
        uint length = 0;

        if (!GetUserPreferredUILanguages(languageName, out _, IntPtr.Zero, ref length) || length == 0)
            return "en";

        var buffer = Marshal.AllocHGlobal(checked((int) length * sizeof(char)));

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

#if NETFRAMEWORK
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserPreferredUILanguages(
        uint flags,
        out uint languages,
        IntPtr buffer,
        ref uint length);
#else
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetUserPreferredUILanguages(
        uint flags,
        out uint languages,
        IntPtr buffer,
        ref uint length);
#endif
}