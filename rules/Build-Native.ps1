#requires -Version 5.1
<#
.SYNOPSIS
    Builds src/SysSuite.Native into SysSuite.Native.dll for Windows x64.

.DESCRIPTION
    Drives cl.exe directly with an explicitly constructed INCLUDE/LIB
    environment instead of going through CMake or vcvarsall.bat.

    Rationale:
      * CMake generator detection fails on some Visual Studio installations
        (observed on VS 18 2026: "No CMAKE_CXX_COMPILER could be found"),
        even when cl.exe is present.
      * vcvarsall.bat shells out to reg.exe, which is unavailable in
        restricted/sandboxed environments.

    Therefore this script locates the MSVC toolset and Windows SDK on disk
    and sets INCLUDE/LIB itself. Same result, far fewer environment
    assumptions. CMakeLists.txt remains for IDE/Visual Studio consumers.

.PARAMETER Configuration
    Debug or Release. Defaults to Release.

.PARAMETER Deploy
    After a successful build, copy the produced DLL into every test and UI
    output directory so the C# FFI smoke tests execute for real instead of
    skipping (a missing native module makes them skip by design).

.PARAMETER VsPath
    Optional explicit Visual Studio installation path. When omitted the
    script discovers it via vswhere.exe, then falls back to well-known
    install locations.

.EXAMPLE
    pwsh ./rules/Build-Native.ps1 -Configuration Release -Deploy
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $Deploy,

    [string] $VsPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$nativeDir = Join-Path $repoRoot 'src/SysSuite.Native'
$outDir = Join-Path $repoRoot "artifacts/native/$Configuration"
$objDir = Join-Path $repoRoot "obj/native/$Configuration"

function Write-Step([string] $Message) {
    Write-Host "[native] $Message" -ForegroundColor Cyan
}

# --- 1. Locate Visual Studio -------------------------------------------------
if ([string]::IsNullOrWhiteSpace($VsPath)) {
    $vswhereCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio/Installer/vswhere.exe')
    ) | Where-Object { $_ -and (Test-Path $_) }

    $vswhere = $vswhereCandidates | Select-Object -First 1
    if ($vswhere) {
        Write-Step "Discovering Visual Studio via vswhere"
        $detected = & $vswhere -latest -products * -property installationPath 2>$null
        if ($LASTEXITCODE -eq 0 -and $detected) {
            $VsPath = ($detected | Select-Object -First 1).Trim()
        }
    }

    if ([string]::IsNullOrWhiteSpace($VsPath)) {
        Write-Step 'vswhere unavailable; scanning standard install locations'
        $roots = @(
            (Join-Path $env:ProgramFiles 'Microsoft Visual Studio'),
            (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio')
        )
        foreach ($root in $roots) {
            if (-not (Test-Path $root)) { continue }
            $hit = Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue |
                Where-Object { Test-Path (Join-Path $_.FullName 'VC/Tools/MSVC') } |
                Sort-Object Name |
                Select-Object -Last 1
            if ($hit) {
                $VsPath = $hit.FullName
                break
            }
        }
    }
}

if ([string]::IsNullOrWhiteSpace($VsPath) -or -not (Test-Path $VsPath)) {
    throw "Unable to locate a Visual Studio installation with the MSVC toolset. Pass -VsPath explicitly."
}
Write-Step "Visual Studio: $VsPath"

# --- 2. Locate MSVC toolset (x64 host) ---------------------------------------
$msvcBase = Join-Path $VsPath 'VC/Tools/MSVC'
if (-not (Test-Path $msvcBase)) {
    throw "MSVC toolset not found under: $msvcBase"
}
$toolset = Get-ChildItem -Path $msvcBase -Directory | Sort-Object Name | Select-Object -Last 1
$clDir = Join-Path $toolset.FullName 'bin/Hostx64/x64'
$cl = Join-Path $clDir 'cl.exe'
if (-not (Test-Path $cl)) {
    throw "cl.exe not found at: $cl"
}
Write-Step "MSVC toolset: $($toolset.Name)"

# --- 3. Windows SDK (optional but preferred) ---------------------------------
$includePaths = @((Join-Path $toolset.FullName 'include'))
$libPaths = @((Join-Path $toolset.FullName 'lib/x64'))

$kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
if (Test-Path $kitsRoot) {
    $sdkIncludeRoot = Join-Path $kitsRoot 'Include'
    if (Test-Path $sdkIncludeRoot) {
        $sdk = Get-ChildItem -Path $sdkIncludeRoot -Directory |
            Where-Object { $_.Name -match '^\d+\.' } |
            Sort-Object Name |
            Select-Object -Last 1
        if ($sdk) {
            Write-Step "Windows SDK: $($sdk.Name)"
            foreach ($sub in @('ucrt', 'um', 'shared', 'winrt')) {
                $p = Join-Path $sdk.FullName $sub
                if (Test-Path $p) { $includePaths += $p }
            }
            foreach ($sub in @('ucrt', 'um')) {
                $p = Join-Path $kitsRoot "Lib/$($sdk.Name)/$sub/x64"
                if (Test-Path $p) { $libPaths += $p }
            }
        }
    }
}

$env:INCLUDE = $includePaths -join ';'
$env:LIB = $libPaths -join ';'
$env:PATH = "$clDir;$env:PATH"
foreach ($stale in @('VCINSTALLDIR', 'VCToolsInstallDir', 'WindowsSdkDir', 'WindowsSDKVersion', 'UCRTVersion', 'UniversalCRTSdkDir')) {
    Remove-Item "Env:$stale" -ErrorAction SilentlyContinue
}

# --- 4. Compile and link -----------------------------------------------------
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
New-Item -ItemType Directory -Path $objDir -Force | Out-Null

$dll = Join-Path $outDir 'SysSuite.Native.dll'
$lib = Join-Path $outDir 'SysSuite.Native.lib'

$common = @(
    '/nologo',
    '/std:c++20',
    '/W4',
    '/utf-8',
    '/EHsc',
    '/LD',
    '/GS',
    '/sdl',
    '/DSYSSUITE_NATIVE_EXPORTS',
    "/I$(Join-Path $nativeDir 'include')",
    "/Fo$objDir\",
    "/Fe$dll",
    (Join-Path $nativeDir 'src/native_api.cpp')
)

if ($Configuration -eq 'Debug') {
    $configFlags = @('/Od', '/Z7', '/MDd')
} else {
    $configFlags = @('/O2', '/MD', '/DNDEBUG')
}

$linkFlags = @('/link', '/MACHINE:X64', "/IMPLIB:$lib")

Write-Step "Compiling ($Configuration)"
$clArgs = $common + $configFlags + $linkFlags
& $cl @clArgs
if ($LASTEXITCODE -ne 0) {
    throw "cl.exe failed with exit code $LASTEXITCODE"
}
if (-not (Test-Path $dll)) {
    throw "Expected output not produced: $dll"
}
Write-Step "Built: $dll"

# --- 5. Optional deployment --------------------------------------------------
if (-not $Deploy) {
    return
}

$targets = @()
foreach ($base in @((Join-Path $repoRoot 'tests/SysSuite.Tests/bin'), (Join-Path $repoRoot 'src/SysSuite.UI/bin'))) {
    if (-not (Test-Path $base)) { continue }

    # Only plain TFM folders (bin/<Config>/net8.0[-windows]). Directories nested
    # under runtimes/ are RID-specific asset folders and must be excluded --
    # dropping a native DLL into runtimes/browser-wasm or runtimes/win/lib
    # (a managed-asset folder) is wrong and can confuse the host resolver.
    $tfmDirs = Get-ChildItem -Path $base -Recurse -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^net\d' -and $_.FullName -notmatch '[\\/]runtimes[\\/]' }

    foreach ($t in $tfmDirs) {
        $targets += $t.FullName
        # Optional RID folder for native assets; safe to create when absent.
        $targets += (Join-Path $t.FullName 'runtimes/win-x64/native')
    }
}

$targets = $targets | Select-Object -Unique
foreach ($t in $targets) {
    New-Item -ItemType Directory -Path $t -Force | Out-Null
    Copy-Item -Path $dll -Destination $t -Force
    Write-Step "Deployed -> $($t.Replace($repoRoot, ''))"
}

Write-Host '[native] Done.' -ForegroundColor Green
