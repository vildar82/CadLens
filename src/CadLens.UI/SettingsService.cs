using System.Diagnostics;
using System.IO;
using System.Text.Json;
#if NETFRAMEWORK
using System.ComponentModel;
using System.Runtime.InteropServices;
#endif

namespace CadLens.UI;

/// <summary>Reads and atomically replaces independent per-user JSON settings files.</summary>
public sealed class SettingsService
{
    private readonly string _directory;

    /// <summary>Uses the per-user CAD Lens directory unless an isolated directory is provided.</summary>
    /// <param name="directory">Optional settings directory, primarily for managed tests.</param>
    public SettingsService(string? directory = null) =>
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CadLens");

    /// <summary>Shared per-user settings directory for this CAD Lens process.</summary>
    public static SettingsService Current { get; } = new();

    /// <summary>Reads saved settings; missing, unreadable, or malformed files return the default value.</summary>
    /// <typeparam name="T">JSON settings type.</typeparam>
    /// <param name="fileName">Settings file name within the CAD Lens directory.</param>
    public T? Load<T>(string fileName)
    {
        var path = Path.Combine(_directory, fileName);

        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.TraceWarning("CAD Lens settings {0} could not be read: {1}", fileName, exception.Message);
            return default;
        }
    }

    /// <summary>Saves through a temporary file; failed writes preserve any previous settings.</summary>
    /// <typeparam name="T">JSON settings type.</typeparam>
    /// <param name="fileName">Settings file name within the CAD Lens directory.</param>
    /// <param name="value">Settings to serialize.</param>
    public bool Save<T>(string fileName, T value)
    {
        var path = Path.Combine(_directory, fileName);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(temporary, JsonSerializer.Serialize(value));
            ReplaceSettings(temporary, path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("CAD Lens settings {0} could not be saved: {1}", fileName, exception.Message);
            return false;
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("CAD Lens temporary settings could not be removed: {0}", exception.Message);
            }
        }
    }

    private static void ReplaceSettings(string temporary, string path)
    {
#if NETFRAMEWORK
        const uint replaceExisting = 1;

        if (!MoveFileEx(temporary, path, replaceExisting))
            throw new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
#else
        File.Move(temporary, path, overwrite: true);
#endif
    }

#if NETFRAMEWORK
    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string source, string destination, uint flags);
#endif
}