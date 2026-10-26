#Requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$sourceRoot=$PSScriptRoot
$project=Join-Path $sourceRoot 'src/VPet/VPet-Simulator.Windows/VPet-Simulator.Windows.csproj'
$output=Join-Path $sourceRoot "build/Nanally/$Configuration"
if(Get-Process -Name Nanally.DesktopPet -ErrorAction SilentlyContinue){throw '请先正常退出娜娜莉桌宠，再进行构建。'}
$null=New-Item -ItemType Directory -Path $output -Force
& dotnet build $project --configuration $Configuration -p:Platform=x64 "-p:OutDir=$output/" -p:DebugType=None -p:DebugSymbols=false -p:GenerateDocumentationFile=false --nologo
if($LASTEXITCODE -ne 0){throw '源码构建失败。'}
# The project also declares these as Content, so a direct dotnet build works.
# Explicit deployment here makes the source-level build contract easy to inspect.
foreach($package in '0000_core','Nanally'){
    $resources=Join-Path $sourceRoot "resources/mod/$package"
    foreach($file in Get-ChildItem -LiteralPath $resources -File -Recurse){
        $relative=[IO.Path]::GetRelativePath($resources,$file.FullName)
        $target=Join-Path (Join-Path $output "mod/$package") $relative
        $null=New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
}
Copy-Item -LiteralPath (Join-Path $sourceRoot 'src/VPet/VPet-Simulator.Windows/Nanally.ico') -Destination (Join-Path $output 'Nanally.ico') -Force
$exe=Join-Path $output 'Nanally.DesktopPet.exe'
if(-not(Test-Path -LiteralPath $exe)){throw '构建结束后未找到程序。'}
"构建完成：$exe"
"用户数据目录：$env:LOCALAPPDATA\NanallyDesktopPet"
