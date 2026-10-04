using System.IO;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Checks shared JSON persistence, compatibility, and recovery from filesystem failures.</summary>
public sealed class SettingsServiceTests
{
    /// <summary>A save creates the directory, and replacement leaves a complete JSON document.</summary>
    [Fact]
    public void SaveCreatesDirectoryAndReplacesPreviousSettings()
    {
        using var file = new SettingsFile();
        var directory = Path.Combine(file.Directory, "nested");
        var settings = new SettingsService(directory);
        Assert.Null(settings.Load<int[]>("values.json"));
        Assert.True(settings.Save<int[]>("values.json", [1, 2, 3]));
        Assert.True(settings.Save<int[]>("values.json", [4]));
        var restored = Assert.IsType<int[]>(new SettingsService(directory).Load<int[]>("values.json"));
        Assert.Equal([4], restored);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    /// <summary>Concurrent settings writers can create or replace the same file without losing a complete write.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentSavesKeepCompleteSettings(bool exists)
    {
        using var file = new SettingsFile();

        if (exists)
            Assert.True(file.Service.Save<int[]>("values.json", [0]));

        using var start = new Barrier(2);
        var first = Enumerable.Repeat(1, 4096).ToArray();
        var second = Enumerable.Repeat(2, 4096).ToArray();
        var writes = new[] {first, second}.Select(values => Task.Run(() =>
        {
            start.SignalAndWait();
            return new SettingsService(file.Directory).Save("values.json", values);
        })).ToArray();

        var results = await Task.WhenAll(writes);
        Assert.Contains(true, results);
        var restored = Assert.IsType<int[]>(file.Service.Load<int[]>("values.json"));
        Assert.True(
            results[0] && restored.SequenceEqual(first) ||
            results[1] && restored.SequenceEqual(second));
        Assert.Empty(Directory.GetFiles(file.Directory, "*.tmp"));
    }

    /// <summary>The existing appearance object and language JSON string remain unchanged.</summary>
    [Fact]
    public void AppearanceAndLanguageKeepExistingFormatsAndSeparateFiles()
    {
        using var file = new SettingsFile();
        var appearance = new AppearancePreferences(file.Service) {Theme = "Light", Palette = "Paper", Accent = "Amber"};
        var language = new UiText(file.Service, () => "en-US");
        language.Select(LanguagePreference.Russian);
        Assert.Equal("{\"Theme\":\"Light\",\"Palette\":\"Paper\",\"Accent\":\"Amber\"}", File.ReadAllText(file.Path));
        var languagePath = Path.Combine(file.Directory, "language.json");
        Assert.Equal("\"Russian\"", File.ReadAllText(languagePath));
        appearance.Reset();
        Assert.Equal(LanguagePreference.Russian, new UiText(file.Service, () => "en-US").Preference);
        Assert.Equal("\"Russian\"", File.ReadAllText(languagePath));
    }

    /// <summary>Missing files return the default for reference and value types.</summary>
    [Fact]
    public void MissingSettingsReturnDefault()
    {
        using var file = new SettingsFile();
        Assert.Null(file.Service.Load<string>("missing.json"));
        Assert.Equal(0, file.Service.Load<int>("missing.json"));
    }

    /// <summary>Damaged JSON and values of the wrong shape do not escape into callers.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("{broken}")]
    [InlineData("true")]
    [InlineData("null")]
    public void InvalidSettingsReturnDefault(string json)
    {
        using var file = new SettingsFile();
        File.WriteAllText(file.Path, json);
        Assert.Null(file.Service.Load<string>("appearance.json"));
    }

    /// <summary>A locked settings file falls back to the default without interrupting startup.</summary>
    [Fact]
    public void ReadFailureReturnsDefault()
    {
        using var file = new SettingsFile();
        Assert.True(file.Service.Save("appearance.json", "original"));

        using (File.Open(file.Path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Null(file.Service.Load<string>("appearance.json"));
    }

    /// <summary>A failed atomic replacement preserves the previous valid file and removes its temporary file.</summary>
    [Fact]
    public void FailedReplacementPreservesPreviousSettings()
    {
        using var file = new SettingsFile();
        Assert.True(file.Service.Save("appearance.json", "original"));

        using (File.Open(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(file.Service.Save("appearance.json", "replacement"));
            Assert.Equal("original", file.Service.Load<string>("appearance.json"));
            Assert.Empty(Directory.GetFiles(file.Directory, "*.tmp"));
        }
    }

    /// <summary>A directory at the destination reports failure and leaves no temporary files behind.</summary>
    [Fact]
    public void BlockedDestinationLeavesNoTemporaryFiles()
    {
        using var file = new SettingsFile();
        Directory.CreateDirectory(file.Path);
        Assert.False(file.Service.Save("appearance.json", "replacement"));
        Assert.Empty(Directory.GetFiles(file.Directory, "*.tmp"));
    }
}