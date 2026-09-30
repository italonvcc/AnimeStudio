[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ExportRoot,
    [switch] $RequireAnimations,
    [switch] $GeometryOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath($ExportRoot).TrimEnd('\','/')
$reports = @(Get-ChildItem -LiteralPath $root -Filter '*.weapon.json' -File)
if ($reports.Count -ne 1) { throw 'Expected one individual weapon report at the export root.' }
$report = Get-Content -LiteralPath $reports[0].FullName -Raw | ConvertFrom-Json
$checks = 0
function Assert-Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw "FAIL: $message" }
    $script:checks++
    Write-Output "PASS: $message"
}
function Resolve-Output([string]$relative) {
    $full = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $full.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Output reference escapes weapon export: $relative"
    }
    return $full
}
Assert-Check ($report.playback.status -eq 'NotTested') 'Exporter does not claim Unity playback validation'
Assert-Check ($report.discovery.status -ne 'Verified') 'Unresolved catalog discovery remains explicit'
Assert-Check ($report.requestedOptions.Materials -eq (-not $GeometryOnly)) 'Material option is recorded accurately'
Assert-Check ($report.export.errors.Count -eq 0) 'No file export stage errors'
Assert-Check ($report.export.variants.Count -gt 0) 'At least one source variant was processed'
foreach ($file in $report.files) {
    $path = Resolve-Output $file.path
    Assert-Check (Test-Path -LiteralPath $path -PathType Leaf) "Manifest output exists: $($file.path)"
    Assert-Check ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $file.sha256) "Hash matches: $($file.path)"
}
foreach ($variant in $report.export.variants) {
    if ($variant.status -eq 'DuplicateSourceRoot') { continue }
    Assert-Check ($variant.status -in @('Complete','Partial')) 'Model variant produced usable files'
    $model = Get-Content -LiteralPath (Resolve-Output $variant.stages.model.manifest) -Raw | ConvertFrom-Json
    Assert-Check ($model.meshes.Count -gt 0) 'Model contains meshes'
    Assert-Check ($model.unresolved.Count -eq 0) 'Requested model dependencies resolve'
    Assert-Check ((Get-Item -LiteralPath (Resolve-Output $variant.stages.model.path)).Length -gt 0) 'FBX payload is nonempty'
    if ($GeometryOnly) {
        $variantRoot = Split-Path (Resolve-Output $variant.stages.model.manifest)
        $textures = @(Get-ChildItem -LiteralPath $variantRoot -File -Recurse | Where-Object Extension -in @('.png','.astexture'))
        Assert-Check ($textures.Count -eq 0) 'Geometry-only export does not write texture payloads'
    }
    else {
        Assert-Check ($model.nativeTextureFailures.Count -eq 0) 'Native textures exported without failure'
        Assert-Check (@($model.materialBindings | Where-Object status -ne 'resolved').Count -eq 0) 'All renderer material slots resolve'
    }
    if ($RequireAnimations) {
        Assert-Check ($model.sourceClips.Count -gt 0) 'Source-attached native animation clips exported'
        Assert-Check (-not $variant.stages.animations.unknownBindings) 'Animation serialization reports no unknown path/type markers'
        foreach ($clip in $model.sourceClips) {
            $variantRoot = Split-Path (Resolve-Output $variant.stages.model.manifest)
            $clipPath = [IO.Path]::GetFullPath((Join-Path $variantRoot $clip.file))
            Assert-Check ($clipPath.StartsWith($variantRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'Clip reference stays inside variant output'
            Assert-Check ((Test-Path -LiteralPath $clipPath -PathType Leaf) -and $clip.sampleRate -gt 0) "Native clip exists with source rate: $($clip.source.Name)"
        }
    }
}
Write-Output "Weapon source export validation: $checks/$checks passed. Unity import/playback not tested."
