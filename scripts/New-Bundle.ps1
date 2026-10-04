$ErrorActionPreference = 'Stop'

$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repository 'src\AutoCAD\CadLens.AutoCAD\CadLens.AutoCAD.csproj'
$output = Join-Path $repository 'artifacts\bundle'
$bundle = Join-Path $output 'CadLens.bundle'
$contents = Join-Path $bundle 'Contents'
$zip = Join-Path $output 'CadLens.bundle.zip'

if (-not ([IO.Path]::GetFullPath($output).StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))) {
    throw 'Bundle output must be inside the repository.'
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

New-Item -ItemType Directory -Path $contents -Force | Out-Null

foreach ($framework in 'net47', 'net48', 'net8.0-windows', 'net10.0-windows') {
    $frameworkContents = Join-Path $contents $framework

    dotnet publish $project `
        --configuration Release `
        --framework $framework `
        --artifacts-path (Join-Path $output 'build') `
        --output $frameworkContents `
        --nologo `
        -p:DebugType=None

    if ($LASTEXITCODE -ne 0) {
        throw "Plugin publish failed for $framework."
    }

    Get-ChildItem -LiteralPath $frameworkContents -Filter '*.xml' | Remove-Item
}

Copy-Item -LiteralPath (Join-Path $repository 'docs\images\cadlens.ico') -Destination (Join-Path $contents 'CadLens.ico')

$version = dotnet msbuild $project -getProperty:Version -property:Configuration=Release

if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($version)) {
    throw 'Could not read the plugin version.'
}

$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0" AppVersion="$version" ProductCode="{F8CF54AC-12A7-4C35-BA1B-BF0AA4745431}" Name="CAD Lens" Description="Drawing exploration for AutoCAD" Icon="./Contents/CadLens.ico">
  <Components>
    <RuntimeRequirements OS="Win64" Platform="AutoCAD|Civil3D" SeriesMin="R23.0" SeriesMax="R23.1" />
    <ComponentEntry AppName="CadLens" ModuleName="./Contents/net47/CadLens.AutoCAD.dll" LoadOnCommandInvocation="True">
      <Commands GroupName="CadLens">
        <Command Global="CADLENS" Local="CADLENS" />
      </Commands>
    </ComponentEntry>
  </Components>
  <Components>
    <RuntimeRequirements OS="Win64" Platform="AutoCAD|Civil3D" SeriesMin="R24.0" SeriesMax="R24.3" />
    <ComponentEntry AppName="CadLens" ModuleName="./Contents/net48/CadLens.AutoCAD.dll" LoadOnCommandInvocation="True">
      <Commands GroupName="CadLens">
        <Command Global="CADLENS" Local="CADLENS" />
      </Commands>
    </ComponentEntry>
  </Components>
  <Components>
    <RuntimeRequirements OS="Win64" Platform="AutoCAD|Civil3D" SeriesMin="R25.0" SeriesMax="R25.1" />
    <ComponentEntry AppName="CadLens" ModuleName="./Contents/net8.0-windows/CadLens.AutoCAD.dll" LoadOnCommandInvocation="True">
      <Commands GroupName="CadLens">
        <Command Global="CADLENS" Local="CADLENS" />
      </Commands>
    </ComponentEntry>
  </Components>
  <Components>
    <RuntimeRequirements OS="Win64" Platform="AutoCAD|Civil3D" SeriesMin="R26.0" SeriesMax="R26.0" />
    <ComponentEntry AppName="CadLens" ModuleName="./Contents/net10.0-windows/CadLens.AutoCAD.dll" LoadOnCommandInvocation="True">
      <Commands GroupName="CadLens">
        <Command Global="CADLENS" Local="CADLENS" />
      </Commands>
    </ComponentEntry>
  </Components>
</ApplicationPackage>
"@

Set-Content -LiteralPath (Join-Path $bundle 'PackageContents.xml') -Value $manifest -Encoding utf8
Compress-Archive -Path $bundle -DestinationPath $zip

Write-Host "Created $zip"
