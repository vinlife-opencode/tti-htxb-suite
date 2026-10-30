# TTI HTXB Suite - run the Htxb.RecipeCore checks
# Usage:  powershell -ExecutionPolicy Bypass -File test-recipetool.ps1
#
# Exit code 0 = every check passed, 1 = at least one failed. That exit code is the
# only contract; BidfGen2's dev-check.ps1 calls this file and looks at nothing else.
#
# The checks split in two:
#
#   * rules and shared vectors - always run. normalise / SW BIN / duration /
#     alignment / cell references, asserted against docs\test-vectors\normalise.json
#     in the BidfGen2 checkout, the same file the Python suite asserts.
#
#   * the real 261028 delivery - pins the measured matrix (17 workbooks,
#     32/16/15 steps, 0/17 FITS). Two sources, the first that exists wins:
#
#        1. the live drop   BidfGen2\Samples\TTSZ Zhou Rong 261029 New Recipes\...
#        2. the pinned copy BidfGen2\handover\fixtures\samples\...  (same path,
#           byte-identical, restored by handover\restore-fixtures.ps1)
#
#     So a fresh clone runs the whole check instead of skipping it, and a
#     machine holding the real drop tests the real drop. Whichever is used is
#     printed, and the runner prints it again.
#
#     Neither exists -> SKIP, printed loudly, and the summary says so. A skip is
#     never counted as a pass.

param(
    [string]$Vectors = '',
    [string]$Samples = '',
    [string]$Tables  = '',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$proj = Join-Path $root 'tests\Htxb.RecipeCore.Tests\Htxb.RecipeCore.Tests.csproj'
$exe  = Join-Path $root 'tests\Htxb.RecipeCore.Tests\bin\Release\net8.0\Htxb.RecipeCore.Tests.exe'

if (-not (Test-Path -LiteralPath $proj)) { throw "test project not found: $proj" }

# The BidfGen2 checkout sits beside this one: D:\_TTI-Dev\_VSProj\..\_Proj_BidfGen2.
# TTI_BIDFGEN2_ROOT overrides it, for a machine that places the two repos elsewhere.
$bidfgen = $env:TTI_BIDFGEN2_ROOT
if (-not $bidfgen) {
    $bidfgen = Join-Path (Split-Path -Parent (Split-Path -Parent $root)) '_Proj_BidfGen2'
}
$delivery = 'Samples\TTSZ Zhou Rong 261029 New Recipes\261028 TTSZ Zhou Rong 17 Test Plan\Trio-tech_BI_Recipes'

if (-not $Samples) {
    $live    = Join-Path $bidfgen $delivery
    $fixture = Join-Path $bidfgen ('handover\fixtures\' + $delivery)
    if (Test-Path -LiteralPath $live) {
        $Samples = $live
    } elseif (Test-Path -LiteralPath $fixture) {
        $Samples = $fixture
        Write-Host '== Using the pinned delivery (the live Samples drop is not here) =='
        Write-Host "   $fixture"
        Write-Host ''
    }
}

if (-not $SkipBuild) {
    Write-Host '== Build test runner (Release) =='
    dotnet build $proj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed: Htxb.RecipeCore.Tests' }
    Write-Host ''
}

if (-not (Test-Path -LiteralPath $exe)) {
    throw "test runner not built: $exe (run without -SkipBuild)"
}

$arguments = @()
if ($Vectors) { $arguments += @('--vectors', $Vectors) }
if ($Samples) { $arguments += @('--samples', $Samples) }
if ($Tables)  { $arguments += @('--tables',  $Tables) }

& $exe @arguments
exit $LASTEXITCODE
