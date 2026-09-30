param([switch]$SkipSensorBridge)
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\PrepareSourceAssets.ps1"
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/win32icon:$PSScriptRoot\assets\robot-app.ico" "/resource:$PSScriptRoot\assets\robot-app.ico,robot-app.ico" /out:"$PSScriptRoot\副屏监控.exe" "/resource:$PSScriptRoot\assets\reference.png,reference.png" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Management.dll /reference:System.Xml.Linq.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll "$PSScriptRoot\Monitor.cs" "$PSScriptRoot\Weather.cs" "$PSScriptRoot\WeatherLocationForm.cs" "$PSScriptRoot\Pubg.cs" "$PSScriptRoot\CompanionAnimation.cs" "$PSScriptRoot\PetRules.cs" "$PSScriptRoot\PetVideo.cs" "$PSScriptRoot\CodexQuota.cs" "$PSScriptRoot\CodexActivity.cs" "$PSScriptRoot\PetSubtitles.cs" "$PSScriptRoot\MemoryCleaner.cs" "$PSScriptRoot\Bindings.cs" "$PSScriptRoot\MusicSessions.cs" "$PSScriptRoot\StartupManager.cs"
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
if (-not $SkipSensorBridge -and (Test-Path "$PSScriptRoot\sensors\LibreHardwareMonitor\LibreHardwareMonitorLib.dll")) {
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /out:"$PSScriptRoot\sensors\LibreHardwareMonitor\CpuSensorBridge.exe" /reference:"$PSScriptRoot\sensors\LibreHardwareMonitor\LibreHardwareMonitorLib.dll" /reference:System.Xml.Linq.dll "$PSScriptRoot\CpuSensorBridge.cs"
    if ($LASTEXITCODE -ne 0) { throw 'CPU 采集器编译失败' }
}
$framework=Split-Path $compiler
& $compiler /nologo /target:exe /out:"$PSScriptRoot\MediaSessionBridgeHost.exe" /r:C:\Windows\System32\WinMetadata\Windows.Media.winmd /r:C:\Windows\System32\WinMetadata\Windows.Foundation.winmd /r:"$framework\System.Runtime.dll" /r:"$framework\System.Runtime.InteropServices.WindowsRuntime.dll" "$PSScriptRoot\MediaSessionBridge.cs"
if($LASTEXITCODE -ne 0){throw '媒体信息组件编译失败'}
Write-Output '编译成功：副屏监控.exe'

