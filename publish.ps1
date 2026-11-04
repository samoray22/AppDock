<#
.SYNOPSIS
    Publishes AppDock as a single-file portable executable.
.DESCRIPTION
    Creates a clean, standalone AppDock.exe (under 2 MB) in bin\Publish\Tiny\
#>
[CmdletBinding()]
param(
    [ValidateSet('Tiny', 'SelfContained')]
    [string]$Type = 'Tiny'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

[xml]$props = Get-Content "$root\Directory.Build.props"
$version = $props.Project.PropertyGroup.Version
if (-not $version) { $version = "3.0.0" }

if ($Type -eq 'Tiny') {
    Write-Host "Publishing Tiny Single-File Portable (~2 MB)..." -ForegroundColor Cyan
    dotnet publish "$root\src\AppBox.App\AppBox.App.csproj" /p:PublishProfile=TinyPortable -nodeReuse:false /m:1 -p:EmitAssetsLogMessages=false
    
    # Remove any extra pdb or helper files leaving strictly AppDock.exe
    $pubDir = "$root\src\AppBox.App\bin\Publish\Tiny"
    Get-ChildItem -Path $pubDir -Exclude "AppDock.exe" | Remove-Item -Force -ErrorAction SilentlyContinue
    
    $exe = Get-Item "$pubDir\AppDock.exe"
    $outDir = "$root\publish"
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    Get-Process -Name "AppDock*", "AppBox*" -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$outDir\*" } | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
    Copy-Item -Path $exe.FullName -Destination "$outDir\AppDock_v$version.exe" -Force
    Copy-Item -Path $exe.FullName -Destination "$outDir\AppDock.exe" -Force
    $sizeMb = [math]::Round($exe.Length / 1MB, 2)
    Write-Host "Success! Created standalone: $outDir\AppDock_v$version.exe ($sizeMb MB)" -ForegroundColor Green
}
elseif ($Type -eq 'SelfContained') {
    Write-Host "Publishing Self-Contained Single-File Executable..." -ForegroundColor Cyan
    dotnet publish "$root\src\AppBox.App\AppBox.App.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -nodeReuse:false /m:1 -o "$root\src\AppBox.App\bin\Publish\SelfContained" -p:EmitAssetsLogMessages=false
    
    $pubDir = "$root\src\AppBox.App\bin\Publish\SelfContained"
    Get-ChildItem -Path $pubDir -Exclude "AppDock.exe" | Remove-Item -Force -ErrorAction SilentlyContinue
    $exe = Get-Item "$pubDir\AppDock.exe"
    $outDir = "$root\publish"
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    Get-Process -Name "AppDock*", "AppBox*" -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$outDir\*" } | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
    Copy-Item -Path $exe.FullName -Destination "$outDir\AppDock_v$version-SelfContained.exe" -Force
    $sizeMb = [math]::Round($exe.Length / 1MB, 2)
    Write-Host "Success! Created self-contained standalone: $outDir\AppDock_v$version-SelfContained.exe ($sizeMb MB)" -ForegroundColor Green
}
