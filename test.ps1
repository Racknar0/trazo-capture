$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output=Join-Path $PSScriptRoot 'tests\EngineTests.exe'
$sources=@('Annotation.cs','OverlayForm.cs','EditorFeatures.cs','ProjectStore.cs','ImageEffects.cs','WindowDetection.cs','ShortcutSettings.cs','ShortcutSettingsForm.cs','Native.cs','MainForm.cs','SettingsForm.cs','ClosePromptForm.cs','CapturePreviewForm.cs','AppSettings.cs','CaptureHistoryStore.cs','StartupRegistration.cs','tests\EngineTests.cs','tests\FeatureTests.cs','tests\EditorIntegrationTests.cs','tests\ShortcutTests.cs','tests\HistorySettingsTests.cs','tests\HistoryExportTests.cs','tests\UiSettingsTests.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
& $compiler /nologo /target:exe /debug:full /optimize- ('/out:'+$output) /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll @sources
if($LASTEXITCODE -ne 0){throw 'Error al compilar las pruebas.'}
& $output
if($LASTEXITCODE -ne 0){throw 'Fallaron las pruebas.'}
