param(
    [string]$GameDir = (Resolve-Path "$PSScriptRoot\..\..").Path,
    [string]$OutDir  = (Join-Path (Resolve-Path "$PSScriptRoot\..").Path "lib\ref")
)

$ErrorActionPreference = "Stop"

$cecilPath = Join-Path $GameDir "BepInEx\core\Mono.Cecil.dll"
if (-not (Test-Path $cecilPath)) {
    throw "Mono.Cecil.dll not found at $cecilPath. Install BepInEx in the game directory first."
}

Add-Type -Path $cecilPath
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$managedDir = Join-Path $GameDir "ReEntry_Data\Managed"
$bepCoreDir = Join-Path $GameDir "BepInEx\core"

$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory($managedDir)
$resolver.AddSearchDirectory($bepCoreDir)

$readerParams = New-Object Mono.Cecil.ReaderParameters
$readerParams.AssemblyResolver = $resolver
$readerParams.ReadSymbols = $false

$targets = @(
    (Join-Path $managedDir "Assembly-CSharp.dll"),
    (Join-Path $managedDir "UnityEngine.dll"),
    (Join-Path $managedDir "UnityEngine.CoreModule.dll"),
    (Join-Path $managedDir "UnityEngine.UI.dll"),
    (Join-Path $managedDir "UnityEngine.UIModule.dll"),
    (Join-Path $managedDir "UnityEngine.PhysicsModule.dll"),
    (Join-Path $bepCoreDir "BepInEx.dll"),
    (Join-Path $bepCoreDir "0Harmony.dll")
)

function Strip-Type([Mono.Cecil.TypeDefinition]$type) {
    foreach ($method in $type.Methods) {
        if ($method.HasBody) {
            $body = $method.Body
            $body.ExceptionHandlers.Clear()
            $body.Variables.Clear()
            $body.Instructions.Clear()
            $body.InitLocals = $false
            $il = $body.GetILProcessor()
            $il.Emit([Mono.Cecil.Cil.OpCodes]::Ldnull)
            $il.Emit([Mono.Cecil.Cil.OpCodes]::Throw)
        }
    }
    foreach ($nested in $type.NestedTypes) {
        Strip-Type $nested
    }
}

foreach ($src in $targets) {
    if (-not (Test-Path $src)) {
        Write-Warning "Skipping missing file: $src"
        continue
    }
    $name = Split-Path $src -Leaf
    $dest = Join-Path $OutDir $name
    Write-Host "Stripping $name -> $dest ..."
    $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($src, $readerParams)
    foreach ($mod in $asm.Modules) {
        foreach ($t in $mod.Types) {
            Strip-Type $t
        }
    }
    $asm.Write($dest)
    $asm.Dispose()
    $sizeKb = [Math]::Round((Get-Item $dest).Length / 1KB, 1)
    Write-Host "  OK ($sizeKb KB)"
}

Write-Host "Done! Stripped metadata-only reference assemblies written to $OutDir"
