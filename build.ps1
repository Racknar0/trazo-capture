param([string]$OutputDirectory=(Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not(Test-Path -LiteralPath $compiler)){$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
if(-not(Test-Path -LiteralPath $compiler)){throw 'Se necesita .NET Framework 4.8 para compilar Trazo.'}
$sources=@('AssemblyInfo.cs','Program.cs','Native.cs','MainForm.cs','ShortcutSettings.cs','ShortcutSettingsForm.cs','Annotation.cs','OverlayForm.cs','EditorFeatures.cs','ProjectStore.cs','ImageEffects.cs','WindowDetection.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
$release=[IO.Path]::GetFullPath($OutputDirectory)
$null=New-Item -ItemType Directory -Force -Path $release
$arguments=@('/nologo','/target:winexe','/optimize+','/platform:anycpu',('/win32manifest:'+ (Join-Path $PSScriptRoot 'app.manifest')),('/out:'+(Join-Path $release 'Trazo.exe')),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Xml.dll')+$sources
& $compiler @arguments
if($LASTEXITCODE -ne 0){throw 'No se pudo compilar Trazo.'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Trazo.exe.config') -Destination (Join-Path $release 'Trazo.exe.config') -Force
Write-Output ('Listo: '+(Join-Path $release 'Trazo.exe'))
