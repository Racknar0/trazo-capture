param(
    [string]$BinaryDirectory=(Join-Path (Split-Path -Parent $PSScriptRoot) 'dist'),
    [string]$CompilerPath=$env:INNO_SETUP_COMPILER,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version='0.2.0',
    [switch]$QaBuild
)

$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$binary=[IO.Path]::GetFullPath($BinaryDirectory)
$output=Join-Path $project 'dist'
$null=New-Item -ItemType Directory -Path $output -Force

if([string]::IsNullOrWhiteSpace($CompilerPath)){
    $candidates=@()
    foreach($root in @($env:ProgramFiles,${env:ProgramFiles(x86)},(Join-Path $env:LOCALAPPDATA 'Programs'))){
        if($root){$candidates+=Join-Path $root 'Inno Setup 7\ISCC.exe';$candidates+=Join-Path $root 'Inno Setup 6\ISCC.exe'}
    }
    $command=Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if($command){$candidates=@($command.Source)+$candidates}
    $CompilerPath=$candidates | Where-Object {Test-Path -LiteralPath $_ -PathType Leaf} | Select-Object -First 1
}
if([string]::IsNullOrWhiteSpace($CompilerPath) -or -not(Test-Path -LiteralPath $CompilerPath -PathType Leaf)){
    throw 'No se encontró Inno Setup. Instálalo desde https://jrsoftware.org/isdl.php o indica -CompilerPath con la ruta a ISCC.exe.'
}
$CompilerPath=[IO.Path]::GetFullPath($CompilerPath)
$exe=Join-Path $binary 'Trazo.exe'
if(-not(Test-Path -LiteralPath $exe -PathType Leaf)){throw 'Primero compila Trazo con build.ps1.'}
$actual=[Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
if($actual -ne $Version){throw ('El ejecutable tiene la versión '+$actual+'; se esperaba '+$Version+'.')}

$base='Trazo-v'+$Version+'-setup'
$arguments=@('/Qp',('/DAppVersion='+$Version),('/DBinaryDirectory='+$binary),('/O'+$output))
if($QaBuild){
    $base+='-qa'
    $arguments+=('/DAppIdOverride=Trazo.QA.'+[Guid]::NewGuid().ToString('N'))
    $arguments+='/DAppMutexOverride='
}
$arguments+=('/DOutputBaseName='+$base)
$arguments+=(Join-Path $PSScriptRoot 'Trazo.iss')
& $CompilerPath @arguments
if($LASTEXITCODE -ne 0){throw 'No se pudo crear el instalador de Trazo.'}
$installer=Join-Path $output ($base+'.exe')
if(-not(Test-Path -LiteralPath $installer -PathType Leaf)){throw 'El compilador no generó el instalador esperado.'}
Write-Output ('Instalador: '+$installer)
Write-Output ('SHA256: '+(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash)
