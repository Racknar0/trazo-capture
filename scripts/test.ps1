param([string]$OutputDirectory=(Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\windows\tests'))
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$application=Join-Path $project 'src\Trazo.Windows'
$testDirectory=Join-Path $project 'tests\Trazo.Windows.Tests'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$release=[IO.Path]::GetFullPath($OutputDirectory)
$null=New-Item -ItemType Directory -Force -Path $release
$output=Join-Path $release 'EngineTests.exe'
$sources=@(Get-ChildItem -LiteralPath $application -Filter '*.cs' -File -Recurse | Where-Object {$_.FullName -notmatch '[\\/](bin|obj)[\\/]'} | Sort-Object FullName | Select-Object -ExpandProperty FullName)
$sources+=@(Get-ChildItem -LiteralPath $testDirectory -Filter '*Tests.cs' -File | Sort-Object FullName | Select-Object -ExpandProperty FullName)
& $compiler /nologo /target:exe /main:Trazo.EngineTests /debug:full /optimize- ('/out:'+$output) /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll @sources
if($LASTEXITCODE -ne 0){throw 'Error al compilar las pruebas.'}
& $output
if($LASTEXITCODE -ne 0){throw 'Fallaron las pruebas.'}
