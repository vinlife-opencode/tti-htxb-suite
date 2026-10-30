# TTI HTXB Suite - one-command build + publish + data2 copy
# Usage:  powershell -ExecutionPolicy Bypass -File build.ps1
# Output: dist\  (only per-tool EXEs at root + shared data2\)
#
# Publish model: framework-dependent SINGLE-FILE (SelfContained=false, no bundled
# runtime). Each EXE embeds the shared host + WebView2 managed/native assemblies,
# so dist\ root holds nothing but the EXEs and the shared data2 folder.
# Native libs (WebView2Loader.dll) self-extract to %TEMP%\.net on first run.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

# tool source dir   ->  distribution exe name (must match manifest.json)
$tools = @(
    @{ Proj = 'Htxb.LotExtractor';      Exe = 'TTI_LotExtractor.exe' }
    @{ Proj = 'Htxb.TempUniformity';    Exe = 'TTI_TempUniformity.exe' }
    @{ Proj = 'Htxb.BiDFAnalysis';      Exe = 'TTI_BiDFAnalysis.exe' }
    @{ Proj = 'Htxb.RecipeTranslator';  Exe = 'TTI_RecipeTranslator.exe' }
    @{ Proj = 'Htxb.RcpStdTable';       Exe = 'TTI_RcpStdTable.exe' }
    @{ Proj = 'Htxb.P2DataAnalysis';    Exe = 'TTI_P2DataAnalysis.exe' }
    @{ Proj = 'Htxb.P2DataAnalysisTime'; Exe = 'TTI_P2DataAnalysisTime.exe' }
    @{ Proj = 'Htxb.Launcher';          Exe = 'TTI_Utils_Launcher.exe' }
    @{ Proj = 'Htxb.BarcodeGenerator';  Exe = 'TTI_BarcodeGenerator.exe' }
)

$dist = Join-Path $root 'dist'
# Preserve user-added DUT package images across the dist wipe: anything in
# dist\data2\assets\dut_package is user data at the deployment site (custom
# *.png for different packages) and must never be lost on republish. Back it
# up before wiping, restore after the fresh .data2 copy (repo files win).
$dutBackup = $null
$prevDut = Join-Path $dist 'data2\assets\dut_package'
if (Test-Path $prevDut) {
    $dutBackup = Join-Path ([System.IO.Path]::GetTempPath()) ('tti_dut_backup_' + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dutBackup -Force | Out-Null
    Copy-Item (Join-Path $prevDut '*') $dutBackup -Recurse -Force -ErrorAction SilentlyContinue
}
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null

Write-Host '== Build =='
# NOTE: builds each project directly instead of via TTI_HtxbSuite.slnx, because
# the .slnx format needs SDK 9.0.200+/10 while some machines only have the
# .NET 8 SDK. Per-project build works on both (tool projects reference Htxb.Host).
foreach ($t in $tools) {
    $proj = Join-Path $root (Join-Path 'src' "$($t.Proj)\$($t.Proj).csproj")
    dotnet build $proj -c Release
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $($t.Proj)" }
}

foreach ($t in $tools) {
    $proj = Join-Path $root (Join-Path 'src' "$($t.Proj)\$($t.Proj).csproj")
    $publishDir = Join-Path $dist $t.Proj
    Write-Host "== Publish $($t.Proj) (single-file, framework-dependent) =="
    dotnet publish $proj -c Release -o $publishDir `
        -p:PublishSingleFile=true -p:SelfContained=false -p:IncludeNativeLibrariesForSelfExtract=true
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($t.Proj)" }

    # move just the single EXE to dist root under the distribution name
    Get-ChildItem -LiteralPath $publishDir -Filter '*.exe' |
        Move-Item -Destination (Join-Path $dist $t.Exe) -Force
    Remove-Item $publishDir -Recurse -Force
}

Write-Host '== data2 copy (merge) =='
$srcData = Join-Path $root '.data2'
$dstData = Join-Path $dist 'data2'
if (Test-Path $srcData) {
    New-Item -ItemType Directory -Path $dstData -Force | Out-Null
    Copy-Item (Join-Path $srcData '*') $dstData -Recurse -Force
}

if ($dutBackup -ne $null -and (Test-Path $dutBackup)) {
    $dstDut = Join-Path $dist 'data2\assets\dut_package'
    New-Item -ItemType Directory -Path $dstDut -Force | Out-Null
    Get-ChildItem -LiteralPath $dutBackup -File | ForEach-Object {
        $dest = Join-Path $dstDut $_.Name
        if (-not (Test-Path $dest)) { Copy-Item $_.FullName $dest -Force }
    }
    Remove-Item $dutBackup -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host '== configuration copy =='
$srcCfg = Join-Path $root 'configuration'
$dstCfg = Join-Path $dist 'configuration'
if (Test-Path $srcCfg) {
    New-Item -ItemType Directory -Path $dstCfg -Force | Out-Null
    Copy-Item (Join-Path $srcCfg '*') $dstCfg -Recurse -Force
}

Write-Host '== Trim extras =='
Get-ChildItem -LiteralPath $dist -File | Where-Object {
    $_.Extension -eq '.pdb' -or $_.Extension -eq '.xml'
} | Remove-Item -Force

Write-Host '== Size check (target < 5 MB per EXE) =='
$fail = $false
Get-ChildItem -LiteralPath $dist -Filter '*.exe' | ForEach-Object {
    $mb = [math]::Round($_.Length / 1MB, 2)
    $ok = if ($_.Length -lt 5MB) { 'OK' } else { 'TOO BIG'; $script:fail = $true }
    Write-Host ("{0,-28} {1,8} MB  {2}" -f $_.Name, $mb, $ok)
}
if ($fail) { throw 'One or more EXEs exceed the 5 MB size target' }

Write-Host '== Done =='
Write-Host "Output: $dist"
Write-Host "Layout: only EXEs + shared data2 at dist root"