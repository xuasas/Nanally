#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$sourceRoot=$PSScriptRoot
$project=Join-Path $sourceRoot 'src/VPet/VPet-Simulator.Windows/VPet-Simulator.Windows.csproj'
$output=Join-Path $sourceRoot 'dist/Nanally/win-x64'
if(Get-Process -Name Nanally.DesktopPet -ErrorAction SilentlyContinue){throw '请先正常退出娜娜莉桌宠，再进行发布构建。'}
& dotnet publish $project --configuration Release --runtime win-x64 --self-contained true -p:Platform=x64 `
    -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -p:GenerateDocumentationFile=false --output $output --nologo
if($LASTEXITCODE -ne 0){throw '自包含发布失败。'}
Copy-Item -LiteralPath (Join-Path $sourceRoot 'src/VPet/VPet-Simulator.Windows/Nanally.ico') -Destination (Join-Path $output 'Nanally.ico') -Force
$exe=Join-Path $output 'Nanally.DesktopPet.exe'
if(-not(Test-Path -LiteralPath $exe)){throw '发布结束后未找到程序。'}
"Windows x64自包含发布完成：$exe"
'分发时请保留整个输出目录；运行此发布版本不需要安装SDK。'
