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
$toolProj = Join-Path $root 'src\Htxb.RecipeTool\Htxb.RecipeTool.csproj'
$tool     = Join-Path $root 'src\Htxb.RecipeTool\bin\Release\net8.0\Htxb.RecipeTool.exe'

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
    dotnet build $toolProj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed: Htxb.RecipeTool' }
    Write-Host ''
}

if (-not (Test-Path -LiteralPath $exe)) {
    throw "test runner not built: $exe (run without -SkipBuild)"
}

# The machine recipe export, from the same two sources as the delivery.
$zadzSub = Join-Path $delivery '2108 Recipe 261031'
if (-not $Zadz) {
    $liveZ = Join-Path $bidfgen $zadzSub
    $pinZ  = Join-Path $bidfgen ('handover\fixtures\' + $zadzSub)
    if (Test-Path -LiteralPath $liveZ) { $Zadz = $liveZ }
    elseif (Test-Path -LiteralPath $pinZ) { $Zadz = $pinZ }
}

$arguments = @()
if ($Vectors) { $arguments += @('--vectors', $Vectors) }
if ($Samples) { $arguments += @('--samples', $Samples) }
if ($Tables)  { $arguments += @('--tables',  $Tables) }
if ($Zadz)    { $arguments += @('--zadz', $Zadz) }

& $exe @arguments
$code = $LASTEXITCODE

# -- parity with the Python tool (its reference until it is retired) --------
$py = Get-Command py -ErrorAction SilentlyContinue
if ($Zadz -and $Samples -and $py -and (Test-Path -LiteralPath $tool) -and (Test-Path -LiteralPath (Join-Path $bidfgen 'bidfgen\cli.py'))) {
    Write-Host ''
    Write-Host '== parity: Python recipe-diff vs C# diff =='
    $same = 0; $different = 0
    foreach ($f in Get-ChildItem $Zadz -Filter *.zadz | Sort-Object Name) {
        $book = Get-ChildItem $Samples -Recurse -Filter ($f.BaseName + '.xlsx') | Select-Object -First 1
        if (-not $book) { continue }
        Push-Location $bidfgen
        $pyJson = (& py -3.13 -m bidfgen.cli recipe-diff $book.FullName $f.FullName --json 2>&1 | Out-String)
        Pop-Location
        $csJson = (& $tool diff $book.FullName $f.FullName --json 2>&1 | Out-String)
        try {
            $a = ($pyJson | ConvertFrom-Json | ConvertTo-Json -Depth 10 -Compress)
            $b = ($csJson | ConvertFrom-Json | ConvertTo-Json -Depth 10 -Compress)
            if ($a -eq $b) { $same++ } else { $different++; Write-Host ('  DIFF ' + $f.BaseName) }
        } catch { $different++; Write-Host ('  FAIL ' + $f.BaseName + ' (unreadable JSON)') }
    }
    Write-Host ('  parity: ' + $same + ' same, ' + $different + ' different')
    if ($different -gt 0) { $code = 1 }
} else {
    Write-Host ''
    Write-Host 'SKIP  parity: Python tool, C# tool or machine recipes not available'
}
exit $code
