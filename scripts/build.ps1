param([string]$OutputDirectory=(Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\windows\app'))
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$application=Join-Path $project 'src\Trazo.Windows'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not(Test-Path -LiteralPath $compiler)){$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
if(-not(Test-Path -LiteralPath $compiler)){throw 'Se necesita .NET Framework 4.8 para compilar Trazo.'}
$sources=@(Get-ChildItem -LiteralPath $application -Filter '*.cs' -File -Recurse | Where-Object {$_.FullName -notmatch '[\\/](bin|obj)[\\/]'} | Sort-Object FullName | Select-Object -ExpandProperty FullName)
$release=[IO.Path]::GetFullPath($OutputDirectory)
$null=New-Item -ItemType Directory -Force -Path $release
$arguments=@('/nologo','/target:winexe','/optimize+','/platform:anycpu',('/win32manifest:'+ (Join-Path $application 'Properties\app.manifest')),('/win32icon:'+(Join-Path $project 'packaging\windows\Trazo.ico')),('/out:'+(Join-Path $release 'Trazo.exe')),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Xml.dll')+$sources
& $compiler @arguments
if($LASTEXITCODE -ne 0){throw 'No se pudo compilar Trazo.'}
Copy-Item -LiteralPath (Join-Path $application 'Trazo.exe.config') -Destination (Join-Path $release 'Trazo.exe.config') -Force
Write-Output ('Listo: '+(Join-Path $release 'Trazo.exe'))
