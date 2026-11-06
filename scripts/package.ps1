param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version='0.2.0',
    [switch]$SkipBuild,
    [string]$BinaryDirectory=(Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\windows\app')
)

$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
if(-not $SkipBuild){& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $BinaryDirectory}

$release=[IO.Path]::GetFullPath((Join-Path $projectRoot 'dist\windows'))
$null=New-Item -ItemType Directory -Path $release -Force
$package=Join-Path $release ('Trazo-v'+$Version+'-windows.zip')
$temporary=Join-Path $release ('.package-'+[Guid]::NewGuid().ToString('N')+'.tmp')
$backup=Join-Path $release ('.previous-package-'+[Guid]::NewGuid().ToString('N')+'.tmp')
$entries=@(
    @{Source=(Join-Path $BinaryDirectory 'Trazo.exe');Name='Trazo.exe'},
    @{Source=(Join-Path $BinaryDirectory 'Trazo.exe.config');Name='Trazo.exe.config'},
    @{Source=(Join-Path $projectRoot 'README.md');Name='README.md'},
    @{Source=(Join-Path $projectRoot 'LICENSE');Name='LICENSE'},
    @{Source=(Join-Path $projectRoot 'CONTRIBUTING.md');Name='CONTRIBUTING.md'},
    @{Source=(Join-Path $projectRoot 'docs\preview.png');Name='docs/preview.png'},
    @{Source=(Join-Path $projectRoot 'docs\dashboard.png');Name='docs/dashboard.png'},
    @{Source=(Join-Path $projectRoot 'docs\settings.png');Name='docs/settings.png'}
)

foreach($entry in $entries){
    if(-not(Test-Path -LiteralPath $entry.Source -PathType Leaf)){
        throw ('No se encontró un archivo del paquete: '+$entry.Source)
    }
}
$binaryVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $BinaryDirectory 'Trazo.exe')).ProductVersion
if($binaryVersion -ne $Version){throw ('El ejecutable tiene la versión '+$binaryVersion+'. Compila la versión '+$Version+' antes de empaquetar.')}

Add-Type -AssemblyName System.IO.Compression
$file=$null
$zip=$null
try{
    $file=[IO.FileStream]::new($temporary,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    $zip=[IO.Compression.ZipArchive]::new($file,[IO.Compression.ZipArchiveMode]::Create,$true)
    foreach($entry in $entries){
        $item=$zip.CreateEntry($entry.Name,[IO.Compression.CompressionLevel]::Optimal)
        $sourceStream=$null
        $entryStream=$null
        try{
            $sourceStream=[IO.File]::OpenRead($entry.Source)
            $entryStream=$item.Open()
            $sourceStream.CopyTo($entryStream)
        }
        finally{
            if($entryStream){$entryStream.Dispose()}
            if($sourceStream){$sourceStream.Dispose()}
        }
    }
    $zip.Dispose();$zip=$null
    $file.Dispose();$file=$null
    if(Test-Path -LiteralPath $package){[IO.File]::Replace($temporary,$package,$backup)}
    else{[IO.File]::Move($temporary,$package)}
    Write-Output ('Paquete: '+$package)
}
finally{
    if($zip){$zip.Dispose()}
    if($file){$file.Dispose()}
    if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Force}
    if(Test-Path -LiteralPath $backup){Remove-Item -LiteralPath $backup -Force}
}
