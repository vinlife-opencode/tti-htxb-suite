# Build the Windows installer (Inno Setup 6) for the TTI HTXB Tools & Utilities suite.
#
# Modeled on ..\_Proj_BidfGen2\build-installer.ps1: same layout (payload = dist\,
# ISCC build, version taken from the manifest), minus the Full/Patch pair - this
# suite ships ~16 MB of single-file EXEs, so one setup covers first install,
# upgrade and recovery.
#
# Requires: Inno Setup 6  ("winget install JRSoftware.InnoSetup")
#           build.ps1 first, so dist\ holds the current EXEs and data2 copy.
#
#   .\build-installer.ps1                     build installer\out\TTI_Utilities_Setup_<ver>.exe
#   .\build-installer.ps1 -Reinstall          uninstall the installed copy, then install silently
#   .\build-installer.ps1 -Reinstall -InstallDir "D:\Apps\_TTI_Utilities"

[CmdletBinding()]
param(
    [string] $InstallDir = 'C:\TTI_PostDataProcessor\_TTI_Utilities',
    [switch] $Reinstall,
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $root

# Inno keys the uninstall entry by AppId, not by the display name: the version is
# part of AppName ("TTI Tools & Utilities V6.11.012"), so a name pattern is a trap.
# Match the registry key first, then fall back to a loose name match.
$appIdKey = '{B1C7A4E2-5D38-4F06-9A7B-3E2C8D1F4A60}_is1'
$appNamePattern = '*Tools & Utilities*'
$appExe = 'TTI_Utils_Launcher.exe'
$suiteExes = @(
    'TTI_Utils_Launcher.exe', 'TTI_BiDFAnalysis.exe', 'TTI_LotExtractor.exe',
    'TTI_TempUniformity.exe', 'TTI_RecipeTranslator.exe', 'TTI_RcpStdTable.exe',
    'TTI_P2DataAnalysis.exe', 'TTI_P2DataAnalysisTime.exe', 'TTI_BarcodeGenerator.exe'
)

# ---- toolchain ---------------------------------------------------------------
$candidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
)
$iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "ISCC.exe (Inno Setup 6) not found. Install it with:  winget install JRSoftware.InnoSetup"
}

# ---- payload ----------------------------------------------------------------
$dist = Join-Path $root 'dist'
if (-not (Test-Path -LiteralPath $dist)) { throw "dist\ is missing - run build.ps1 first." }
if (-not $SkipBuild) {
    Write-Host 'Publishing the suite (build.ps1)...'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'build.ps1 failed' }
}

$exes = @(Get-ChildItem -LiteralPath $dist -Filter *.exe)
if ($exes.Count -ne 9) {
    throw "dist\ holds $($exes.Count) EXE(s), expected 9 - run build.ps1 again."
}
foreach ($need in @('TTI_Utils_Launcher.exe', 'data2\manifest.json', 'configuration\tools.ini')) {    if (-not (Test-Path -LiteralPath (Join-Path $dist $need))) {
        throw "dist\$need is missing - run build.ps1 again."
    }
}

# ---- version: the launcher entry in the manifest is the suite version --------
$manifestRepo = Get-Content -LiteralPath (Join-Path $root '.data2\manifest.json') -Raw | ConvertFrom-Json
$manifestDist = Get-Content -LiteralPath (Join-Path $dist 'data2\manifest.json') -Raw | ConvertFrom-Json
$ver = [string]$manifestRepo.tools.launcher.version
if ([string]$manifestDist.tools.launcher.version -ne $ver) {
    throw ("dist\data2\manifest.json is stale ({0}) - the repo says {1}; run build.ps1 again." -f `
        $manifestDist.tools.launcher.version, $ver)
}
if ($ver -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw "unexpected version string: '$ver'" }

# Windows VersionInfo needs the 4-part number as-is (6.11.11.0).
$appNum = $ver
# Display form V<year>.<month>.<day><run>: 6.11.11.0 -> V6.11.011
$p = $ver.Split('.')
$ddr = [string]$p[2]
while ($ddr.Length -lt 3) { $ddr = '0' + $ddr }
$appVer = 'V{0}.{1}.{2}' -f [int]$p[0], ([string]$p[1]).PadLeft(2, '0'), $ddr
$outName = "TTI_Utilities_Setup_$appVer"

Write-Host "Suite version: $appVer   (VersionInfo $appNum)"
Write-Host "Payload      : $($exes.Count) EXEs + data2 + configuration"
Write-Host "Output       : installer\out\$outName.exe"

# ---- build -------------------------------------------------------------------
$outDir = Join-Path $root 'installer\out'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
& $iscc "/DAppVer=$appVer" "/DAppNum=$appNum" "/DOutName=$outName" `
        (Join-Path $root 'installer\installer.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

$setup = Join-Path $outDir "$outName.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw "expected $setup was not produced" }
Write-Host ("Built: {0}  ({1:N1} MB)" -f $setup, ((Get-Item -LiteralPath $setup).Length / 1MB))

if (-not $Reinstall) {
    Write-Host ''
    Write-Host 'Done. Install with:'
    Write-Host "  `"$setup`" /DIR=`"$InstallDir`""
    exit 0
}

# ---- reinstall: uninstall the installed copy, then install silently ----------
# The launcher is resident by design (it auto-hides into a corner tab) and was
# started by the previous install's [Run] entry, so its EXE is locked while the
# reinstall runs. A locked file inside a folder blocks Move-Item with "Access is
# denied", which is what silently killed the V6.11.012 run between the move and
# the install step. Stop every suite process first, then act.
function Stop-SuiteProcesses {
    foreach ($name in $suiteExes) {
        $procs = @(Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($name)) -ErrorAction SilentlyContinue)
        foreach ($p in $procs) {
            Write-Host "  stopping $name (pid $($p.Id))"
            $p.Kill()
            $p.WaitForExit(5000) | Out-Null
        }
    }
    Start-Sleep -Milliseconds 400   # let the OS release the handles
}

function Get-InstalledEntry {
    foreach ($hive in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
                        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
                        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')) {
        if (-not (Test-Path $hive)) { continue }
        Get-ChildItem $hive -ErrorAction SilentlyContinue | ForEach-Object {
            $p = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
            $isOurs = ($_.PSChildName -eq $appIdKey) -or ($p.DisplayName -like $appNamePattern)
            if ($isOurs -and $p.UninstallString) {
                [pscustomobject]@{ Name = $p.DisplayName; Version = $p.DisplayVersion
                                   Location = $p.InstallLocation; Uninstall = $p.UninstallString }
            }
        }
    }
}

Write-Host ''
Write-Host '== Uninstall step =='
Stop-SuiteProcesses
$installed = @(Get-InstalledEntry)
if ($installed.Count -gt 0) {
    foreach ($e in $installed) {
        $loc = if ($e.Location) { $e.Location.TrimEnd('\') } else { $InstallDir }
        $unins = Join-Path $loc 'unins000.exe'
        if (Test-Path -LiteralPath $unins) {
            Write-Host "Uninstalling: $($e.Name) at $loc"
            $proc = Start-Process -FilePath $unins `
                -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru
            Write-Host "  exit code: $($proc.ExitCode)"
        } else {
            Write-Warning "Uninstaller not found for '$($e.Name)' ($loc) - skipping."
        }
    }
} else {
    Write-Host 'Nothing registered in Apps & features.'
}

# A folder produced by a hand-copied dist\ has no uninstaller at all. Move it
# aside instead of deleting it: it may hold configuration\ (LastDir=) and
# custom data2\assets\dut_package images.
if (Test-Path -LiteralPath $InstallDir) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $aside = "$InstallDir.bak-$stamp"
    Write-Host "A folder is still at $InstallDir - moving it aside:"
    Write-Host "  $aside"
    try {
        Move-Item -LiteralPath $InstallDir -Destination $aside -ErrorAction Stop
    } catch {
        throw ("could not move $InstallDir aside ({0}). Close every TTI tool window " -f $_.Exception.Message +
               "and run this again.")
    }
}

Write-Host ''
Write-Host '== Install step =='
$installArgs = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/DIR=`"$InstallDir`"",'/LOG')
$proc = Start-Process -FilePath $setup -ArgumentList $installArgs -Wait -PassThru
if ($proc.ExitCode -ne 0) { throw "installer failed with exit code $($proc.ExitCode)" }

# ---- verify ------------------------------------------------------------------
$exe = Join-Path $InstallDir $appExe
if (-not (Test-Path -LiteralPath $exe)) { throw "install finished but $exe is missing" }
$installedVersion = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
Write-Host ''
Write-Host 'Installed:'
Write-Host "  folder  : $InstallDir"
Write-Host "  launcher: $appExe  ($installedVersion)"
Get-ChildItem -LiteralPath $InstallDir -Filter *.exe | Where-Object { $_.Name -ne 'unins000.exe' } | ForEach-Object {
    Write-Host ("    {0,-30} {1}" -f $_.Name, $_.VersionInfo.FileVersion)
}
if ($installedVersion -ne $appNum) {
    throw "installed launcher reports $installedVersion, expected $appNum - the upgrade did not land."
}
Write-Host ''
Write-Host 'Done.'
