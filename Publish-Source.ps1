$ErrorActionPreference='Stop'
$destination=Join-Path $PSScriptRoot 'dist/NeonSideScreenMonitor-source'
if(Test-Path $destination){throw '导出目录已存在，请先重命名旧目录。'}
New-Item -ItemType Directory -Force (Join-Path $destination 'assets') | Out-Null
$files=@('Monitor.cs','Weather.cs','Pubg.cs','CompanionAnimation.cs','PetRules.cs','PetVideo.cs','CodexQuota.cs','CodexActivity.cs','PetSubtitles.cs','MemoryCleaner.cs','Bindings.cs','CpuSensorBridge.cs','build.ps1','PrepareSourceAssets.ps1','README.md','LICENSE','THIRD_PARTY.md','.gitignore','GitHub发布步骤.md','Publish-Source.ps1')
foreach($file in $files){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $destination $file)}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets/pet-subtitles.json') -Destination (Join-Path $destination 'assets/pet-subtitles.json')
Compress-Archive -LiteralPath $destination -DestinationPath (Join-Path $PSScriptRoot 'dist/NeonSideScreenMonitor-source.zip')
Write-Output $destination
