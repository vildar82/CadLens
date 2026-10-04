param(
  [ValidateSet('net47', 'net48')]
  [string[]]$TargetFrameworks = @('net47', 'net48')
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -eq 'Core') {
  $scriptPath = $PSCommandPath.Replace("'", "''")
  $frameworks = ($TargetFrameworks | ForEach-Object { "'$_'" }) -join ', '
  & powershell.exe -NoProfile -NonInteractive -Command "& '$scriptPath' -TargetFrameworks @($frameworks)"

  if ($LASTEXITCODE -ne 0) {
    throw 'Legacy dependency checks failed.'
  }

  return
}

$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$contents = Join-Path $repository 'artifacts\bundle\CadLens.bundle\Contents'
$output = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts\autodesk-profile\legacy-binding-smoke'))

if (-not $output.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
  throw 'Smoke output must be inside the repository.'
}

$run = Join-Path $output ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null

$probeSource = @'
using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using CadLens.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

public sealed class Recipient : ObservableObject
{
    private string text;
    public string Text
    {
        get { return text; }
        set { SetProperty(ref text, value); }
    }
}

public sealed class Message
{
    public string Text { get; set; }
}

public static class Probe
{
    public static void Run(string operation, string payload)
    {
        switch (operation)
        {
            case "json":
                string json = JsonSerializer.Serialize(new Message { Text = "probe" }, (JsonSerializerOptions)null);
                Message restored = JsonSerializer.Deserialize<Message>(json, (JsonSerializerOptions)null);
                if (restored.Text != "probe")
                    throw new Exception("JSON roundtrip failed.");
                break;
            case "toolkit":
                CheckCommands();
                break;
            case "di":
                ServiceCollection services = new ServiceCollection();
                services.AddSingleton<Recipient>();
                using (ServiceProvider provider = services.BuildServiceProvider())
                {
                    provider.GetRequiredService<Recipient>().Text = "resolved";
                    provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                break;
            case "immutable":
                ImmutableArray<string> values = ImmutableArray.Create("a", "b");
                ImmutableDictionary<string, int> map = ImmutableDictionary<string, int>.Empty.Add("a", 1);
                if (values.Length != 2 || map["a"] != 1)
                    throw new Exception("Immutable collections failed.");
                break;
            case "settings":
                CheckSettings(Path.Combine(payload, "settings"));
                break;
        }
    }

    private static void CheckCommands()
    {
        Recipient recipient = new Recipient();
        int changed = 0;
        recipient.PropertyChanged += delegate { changed++; };
        recipient.Text = "start";
        new RelayCommand(delegate { recipient.Text = "relay"; }).Execute(null);
        AsyncRelayCommand command = new AsyncRelayCommand((Func<Task>)delegate
        {
            recipient.Text = "async";
            return Task.FromResult(0);
        });
        command.ExecuteAsync(null).GetAwaiter().GetResult();
        if (recipient.Text != "async" || changed != 3)
            throw new Exception("MVVM commands failed.");
    }

    private static void CheckSettings(string directory)
    {
        SettingsService settings = new SettingsService(directory);
        if (!settings.Save<Message>("probe.json", new Message { Text = "first" }) ||
            !settings.Save<Message>("probe.json", new Message { Text = "replaced" }))
            throw new Exception("Settings save or replacement failed.");
        Message restored = new SettingsService(directory).Load<Message>("probe.json");
        if (restored == null || restored.Text != "replaced" || Directory.GetFiles(directory, "*.tmp").Length != 0)
            throw new Exception("Settings read or cleanup failed.");
    }
}
'@

$hostSource = @'
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.7")]

public static class Host
{
    public static int Main(string[] args)
    {
        try
        {
            if (File.Exists(AppDomain.CurrentDomain.SetupInformation.ConfigurationFile))
                throw new Exception("The smoke host must run without an app.config.");
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs request)
            {
                Console.WriteLine("Binding: " + request.Name);
                return null;
            };
            Assembly plugin = Assembly.LoadFrom(Path.Combine(args[1], "CadLens.AutoCAD.dll"));
            RuntimeHelpers.RunModuleConstructor(plugin.ManifestModule.ModuleHandle);
            MethodInfo resolve = plugin.GetType("CadLens.AutoCAD.LegacyDependencies")
                .GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic);
            string name = "System.Memory, Version=4.0.1.2, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51";
            if (resolve.Invoke(null, new object[] { null, new ResolveEventArgs(name) }) != null ||
                resolve.Invoke(null, new object[] { null, new ResolveEventArgs(name, Assembly.GetExecutingAssembly()) }) != null)
                throw new Exception("A request outside the payload was redirected.");
            Assembly probe = Assembly.LoadFrom(Path.Combine(args[1], "Probe.dll"));
            probe.GetType("Probe").GetMethod("Run").Invoke(null, new object[] { args[0], args[1] });
            Console.WriteLine(args[0] + " PASS");
            return 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            return 1;
        }
    }
}
'@

$compiler = New-Object Microsoft.CSharp.CSharpCodeProvider

function Build-Probe {
  param(
    [string]$Source,
    [string]$Destination,
    [string[]]$References,
    [bool]$Executable
  )

  $parameters = New-Object System.CodeDom.Compiler.CompilerParameters
  $parameters.GenerateExecutable = $Executable
  $parameters.TreatWarningsAsErrors = $true
  $parameters.OutputAssembly = $Destination

  if (-not $Executable) {
    # The old compiler sees JSON's internal attribute polyfill and the redirects checked below.
    $parameters.CompilerOptions = '/nowarn:1685,1702'
  }

  foreach ($reference in $References) {
    [void]$parameters.ReferencedAssemblies.Add($reference)
  }

  $result = $compiler.CompileAssemblyFromSource($parameters, $Source)

  if ($result.Errors.HasErrors) {
    throw (($result.Errors | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine)
  }
}

$probeHost = Join-Path $run 'Host.exe'
Build-Probe $hostSource $probeHost @('System.dll', 'System.Core.dll') $true

foreach ($framework in $TargetFrameworks) {
  $payload = Join-Path $contents $framework
  $isolated = Join-Path $run $framework

  if (-not (Test-Path -LiteralPath (Join-Path $payload 'CadLens.AutoCAD.dll'))) {
    throw "Published $framework payload is missing. Run scripts/New-Bundle.ps1 first."
  }

  New-Item -ItemType Directory -Path $isolated -Force | Out-Null
  Get-ChildItem -LiteralPath $payload -File |
    Where-Object Extension -ne '.config' |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $isolated }

  $references = @('System.dll', 'System.Core.dll')
  foreach ($name in @(
    'CommunityToolkit.Mvvm.dll',
    'Microsoft.Extensions.DependencyInjection.dll',
    'Microsoft.Extensions.DependencyInjection.Abstractions.dll',
    'Microsoft.Bcl.AsyncInterfaces.dll',
    'System.Threading.Tasks.Extensions.dll',
    'System.Text.Json.dll',
    'System.Memory.dll',
    'System.Collections.Immutable.dll',
    'CadLens.UI.dll'
  )) {
    $references += Join-Path $isolated $name
  }

  $runtime = [Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
  $references += Join-Path $runtime 'netstandard.dll'

  Build-Probe $probeSource (Join-Path $isolated 'Probe.dll') $references $false

  foreach ($operation in 'json', 'toolkit', 'di', 'immutable', 'settings') {
    Write-Host "$framework $operation"
    & $probeHost $operation $isolated

    if ($LASTEXITCODE -ne 0) {
      throw "$framework $operation failed."
    }
  }
}

Write-Host 'Legacy bundle dependency checks passed.'