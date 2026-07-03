# Throw Up Mod Build Script
# Compiles the C# mod code using dotnet or standard csc compiler

$csc = "roslyn\tasks\netcore\bincore\csc.dll"
if (-not (Test-Path $csc)) {
    $csc = "csc"
}
$netDir = "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.25"
$gameDir = "X:\SteamLibrary\steamapps\common\Schedule I"

$refFiles = @()
if (Test-Path $netDir) {
    Get-ChildItem -Path $netDir -Filter "*.dll" | ForEach-Object {
        $name = $_.Name
        if ($name -notmatch "Native" -and $name -notmatch "jit" -and $name -notmatch "gc" -and $name -notmatch "msquic" -and $name -notmatch "coreclr" -and $name -notmatch "hostpolicy" -and $name -notmatch "mscordbi" -and $name -notmatch "mscordaccore" -and $name -notmatch "mscorrc" -and $name -notmatch "clretwrc") {
            $refFiles += $_.FullName
        }
    }
}

$refFiles += "$gameDir\MelonLoader\net6\MelonLoader.dll"
$refFiles += "$gameDir\MelonLoader\net6\0Harmony.dll"
$refFiles += "$gameDir\MelonLoader\net6\Il2CppInterop.Runtime.dll"
$refFiles += "$gameDir\MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll"
$refFiles += "$gameDir\MelonLoader\Il2CppAssemblies\UnityEngine.CoreModule.dll"
$refFiles += "$gameDir\MelonLoader\Il2CppAssemblies\UnityEngine.PhysicsModule.dll"
$refFiles += "$gameDir\MelonLoader\Il2CppAssemblies\UnityEngine.InputLegacyModule.dll"
$refFiles += "$gameDir\MelonLoader\Il2CppAssemblies\Il2CppFishNet.Runtime.dll"
$refFiles += "$gameDir\MelonLoader\Il2CppAssemblies\Il2Cppmscorlib.dll"
$refFiles += "$gameDir\MelonLoader\Il2CppAssemblies\Il2CppScheduleOne.Core.dll"

$refArgs = $refFiles | ForEach-Object { "/r:`"$_`"" }

Write-Host "Compiling InfinitePaintMod.cs..."
if ($csc -eq "csc") {
    csc /target:library /out:ThrowUpMod.dll $refArgs InfinitePaintMod.cs
} else {
    dotnet $csc /target:library /out:ThrowUpMod.dll $refArgs InfinitePaintMod.cs
}

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build Succeeded! Output: ThrowUpMod.dll" -ForegroundColor Green
} else {
    Write-Host "Build Failed!" -ForegroundColor Red
}
