# TTI HTXB Suite - build Htxb.RecipeTool (the customer recipe audit tool)
# Usage:  powershell -ExecutionPolicy Bypass -File build-recipetool.ps1
# Output: dist-recipetool\TTI_RecipeTool.exe
#
# Deliberately OUTSIDE build.ps1 and outside dist\:
#
#   * build.ps1 wipes dist\ on every run, so anything parked there by hand
#     disappears without a trace;
#   * RecipeTool is a development and audit tool. It has no launcher card and no
#     entry in .data2\manifest.json, so it must not appear in the shipped set
#     that build.ps1 publishes.
#
# Same publish model as the rest of the suite: framework-dependent single file,
# no bundled runtime, so the EXE can be copied to a plant PC next to the .NET 8
# runtime that is already there.

param(
    [switch]$NoPublish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$proj = Join-Path $root 'src\Htxb.RecipeTool\Htxb.RecipeTool.csproj'
$out  = Join-Path $root 'dist-recipetool'

if (-not (Test-Path -LiteralPath $proj)) { throw "project not found: $proj" }

Write-Host '== Build Htxb.RecipeTool (Release) =='
dotnet build $proj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed: Htxb.RecipeTool' }

if ($NoPublish) {
    Write-Host '== Publish skipped (-NoPublish) =='
    exit 0
}

if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }

Write-Host '== Publish (single-file, framework-dependent) =='
dotnet publish $proj -c Release -o $out --nologo `
    -p:PublishSingleFile=true -p:SelfContained=false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed: Htxb.RecipeTool' }

# Rename to the distribution convention (TTI_<Tool>.exe), as build.ps1 does.
$built = Get-ChildItem -LiteralPath $out -Filter 'Htxb.RecipeTool.exe' -File
if (-not $built) { throw "publish produced no Htxb.RecipeTool.exe in $out" }
Move-Item $built.FullName (Join-Path $out 'TTI_RecipeTool.exe') -Force

# A stray .pdb or .xml next to the EXE is noise in a tool an operator copies around.
Get-ChildItem -LiteralPath $out -File |
    Where-Object { $_.Extension -in '.pdb', '.xml' } |
    Remove-Item -Force

$exe = Join-Path $out 'TTI_RecipeTool.exe'
$info = (Get-Item -LiteralPath $exe).VersionInfo

Write-Host ''
Write-Host ("{0,-24} {1,8:N2} MB  FileVersion {2}" -f $info.FileName, ((Get-Item $exe).Length / 1MB), $info.FileVersion)
Write-Host "Output: $out"
Write-Host ''
Write-Host 'Next: .\test-recipetool.ps1'
