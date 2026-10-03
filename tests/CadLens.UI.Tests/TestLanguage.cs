using System.Runtime.CompilerServices;

namespace CadLens.UI.Tests;

internal static class TestLanguage
{
    [ModuleInitializer]
    internal static void Initialize() => UiText.Current.Select(LanguagePreference.English, persist: false);
}