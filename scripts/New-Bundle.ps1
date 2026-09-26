$ErrorActionPreference = 'Stop'

$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
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

dotnet publish (Join-Path $repository 'src\AutoCAD\CadLens.AutoCAD\CadLens.AutoCAD.csproj') `
    --configuration Release `
    --artifacts-path (Join-Path $output 'build') `
    --output $contents `
    --nologo `
    -p:DebugType=None

if ($LASTEXITCODE -ne 0) {
    throw 'Plugin publish failed.'
}

Get-ChildItem -LiteralPath $contents -Filter '*.xml' | Remove-Item

$manifest = @'
<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0" AppVersion="1.0.0.0" ProductCode="{EE37011F-E21D-4DCE-B88C-C54E351D71BF}" Name="CAD Lens" Description="Drawing exploration for AutoCAD">
  <Components>
    <RuntimeRequirements OS="Win64" Platform="AutoCAD|Civil3D" SeriesMin="R25.0" />
    <ComponentEntry AppName="CadLens" ModuleName="./Contents/CadLens.AutoCAD.dll" LoadOnCommandInvocation="True">
      <Commands GroupName="CadLens">
        <Command Global="CADLENS" Local="CADLENS" />
      </Commands>
    </ComponentEntry>
  </Components>
</ApplicationPackage>
'@

Set-Content -LiteralPath (Join-Path $bundle 'PackageContents.xml') -Value $manifest -Encoding utf8
Compress-Archive -Path $bundle -DestinationPath $zip

Write-Host "Created $zip"