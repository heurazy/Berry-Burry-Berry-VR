# Builds the mod and the one-file installer: dist\BerryVR-Setup.exe
#
#   powershell -ExecutionPolicy Bypass -File build.ps1 [-GameDir <game folder>]
#
# Needs: .NET SDK (dotnet), Visual Studio Build Tools with C++ (for the native
# submit bridge) and an installed copy of the game (its assemblies are
# referenced at compile time, never redistributed).

param(
    [string]$GameDir = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$exeName = "BerryBerryBerryBerryBerryBerryBerryBerryBerry.exe"

function Find-Game {
    $steam = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { return $null }
    $libs = @($steam.Replace('/', '\'))
    $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
        foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s*"([^"]+)"')) {
            $libs += $m.Groups[1].Value.Replace('\\', '\')
        }
    }
    foreach ($lib in $libs) {
        $dir = Join-Path $lib "steamapps\common\BerryBerryBerry"
        if (Test-Path (Join-Path $dir $exeName)) { return $dir }
    }
    return $null
}

if (-not $GameDir) { $GameDir = Find-Game }
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir $exeName))) {
    throw "Game not found. Pass -GameDir <folder containing $exeName>."
}
Write-Host "Game: $GameDir"

$out = Join-Path $root "build"
$payload = Join-Path $out "payload"
$dist = Join-Path $root "dist"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $payload, $dist | Out-Null

# 1. Managed plugin
Write-Host "Building BerryVR.dll..."
dotnet build (Join-Path $root "BerryVR\BerryVR.csproj") -c Release -p:GameDir="$GameDir" -o (Join-Path $out "plugin") --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

# 2. Native render-thread submit bridge
Write-Host "Building BerryVRRender.dll..."
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "Visual Studio C++ build tools not found" }
$nativeOut = Join-Path $out "native"
New-Item -ItemType Directory -Force $nativeOut | Out-Null
$src = Join-Path $root "BerryVR\native\BerryVRRender.cpp"
cmd /c "`"$vs\VC\Auxiliary\Build\vcvars64.bat`" >nul && cd /d `"$nativeOut`" && cl /nologo /LD /O2 /EHsc `"$src`" /Fe:BerryVRRender.dll >nul"
if ($LASTEXITCODE -ne 0) { throw "native build failed" }

# 3. Payload laid out exactly like the game folder
Write-Host "Assembling payload..."
Expand-Archive (Join-Path $root "third_party\bepinex\BepInEx_win_x64_5.4.23.2.zip") $payload -Force
Copy-Item (Join-Path $root "third_party\openvr\openvr_api.dll") $payload
$plugin = Join-Path $payload "BepInEx\plugins\BerryVR"
New-Item -ItemType Directory -Force $plugin | Out-Null
Copy-Item (Join-Path $out "plugin\BerryVR.dll") $plugin
Copy-Item (Join-Path $nativeOut "BerryVRRender.dll") $plugin
Copy-Item (Join-Path $root "BerryVR\*.json") $plugin
Copy-Item (Join-Path $root "BerryVR\README_CONTROLES.txt") $plugin
$licenses = Join-Path $plugin "licenses"
New-Item -ItemType Directory -Force $licenses | Out-Null
Copy-Item (Join-Path $root "LICENSE") (Join-Path $licenses "BerryVR-LICENSE.txt")
Copy-Item (Join-Path $root "third_party\bepinex\LICENSE") (Join-Path $licenses "BepInEx-LICENSE.txt")
Copy-Item (Join-Path $root "third_party\openvr\LICENSE") (Join-Path $licenses "OpenVR-LICENSE.txt")

$zip = Join-Path $out "payload.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)

# 4. One-file installer, compiled with the C# compiler that ships with Windows
Write-Host "Building installer..."
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$setup = Join-Path $dist "BerryVR-Setup.exe"
& $csc /nologo /target:exe /platform:anycpu /optimize+ /out:"$setup" `
    /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll `
    /resource:"$zip",payload.zip (Join-Path $root "installer\Installer.cs")
if ($LASTEXITCODE -ne 0) { throw "installer build failed" }

Write-Host "Done: $setup"
