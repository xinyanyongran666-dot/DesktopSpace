$ErrorActionPreference='Stop'
$projectPath=Split-Path -Parent $MyInvocation.MyCommand.Path
$frameworkPath='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wpfPath=Join-Path $frameworkPath 'WPF'
$binPath=Join-Path $projectPath 'bin'
New-Item -ItemType Directory -Path $binPath -Force|Out-Null
$compilerArgs=@('/nologo','/target:winexe','/platform:x64','/optimize+',('/win32manifest:'+(Join-Path $projectPath 'src\app.manifest')),('/out:'+(Join-Path $binPath 'DesktopSpace.exe')),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Web.Extensions.dll','/reference:System.Xaml.dll',('/reference:'+ (Join-Path $wpfPath 'WindowsBase.dll')))
foreach($assembly in @('PresentationCore.dll','PresentationFramework.dll','UIAutomationClient.dll','UIAutomationTypes.dll')){$compilerArgs+='/reference:'+(Join-Path $wpfPath $assembly)}
$compilerArgs+=Get-ChildItem -LiteralPath (Join-Path $projectPath 'src') -Filter '*.cs' | ForEach-Object {$_.FullName}
& (Join-Path $frameworkPath 'csc.exe') @compilerArgs
if($LASTEXITCODE -ne 0){throw 'DesktopSpace build failed'}
& (Join-Path $frameworkPath 'csc.exe') /nologo /target:winexe /platform:x64 ('/out:'+(Join-Path $binPath 'LaunchDrawer.exe')) (Join-Path $projectPath 'LaunchDrawer.cs')
if($LASTEXITCODE -ne 0){throw 'DesktopSpace drawer entry build failed'}
Copy-Item -LiteralPath (Join-Path $projectPath 'assets\tray-expand.png') -Destination (Join-Path $binPath 'tray-expand.png') -Force
Write-Output (Join-Path $binPath 'DesktopSpace.exe')
