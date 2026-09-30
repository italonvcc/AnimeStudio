[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Catalog,
    [Parameter(Mandatory)][string[]] $ExportRoots,
    [Parameter(Mandatory)][string] $Report,
    [switch] $VerifyHashes
)
$ErrorActionPreference = 'Stop'
$catalogData = Get-Content -LiteralPath $Catalog -Raw | ConvertFrom-Json
$expected = @($catalogData.catalog.Families.Key | Sort-Object)
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$entries = [Collections.Generic.List[object]]::new()
$meshCoverage = @{}
$rootless = [Collections.Generic.List[object]]::new()
$hashCount = 0
$fileCount = 0
function Resolve-Contained([string]$root, [string]$relative) {
    $path = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Report path escapes export root: $relative"
    }
    return $path
}
function Mesh-Key($mesh) {
    return [IO.Path]::GetFullPath($mesh.Source).ToUpperInvariant() + '|' + $mesh.Offset + '|' + $mesh.PathID
}
foreach ($rootInput in $ExportRoots) {
    $root = [IO.Path]::GetFullPath($rootInput).TrimEnd('\','/')
    $ledger = Get-Content -LiteralPath (Join-Path $root 'weapon-export.json') -Raw | ConvertFrom-Json
    if ($ledger.status -in @('Running','Cancelled') -or $ledger.total -ne $ledger.accounted -or $ledger.accounted -ne @($ledger.entries).Count) {
        throw "Incomplete ledger: $root"
    }
    if ($ledger.Fingerprint -ne $catalogData.sourceFingerprint) { throw "Source fingerprint mismatch: $root" }
    foreach ($entry in $ledger.entries) {
        if (-not $seen.Add($entry.Key)) { throw "Repeated catalog key: $($entry.Key)" }
        if (-not $entry.manifest) { throw "Uninspectable entry: $($entry.Key): $($entry.error)" }
        $manifestPath = Resolve-Contained $root $entry.manifest
        $weapon = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($weapon.Key -ne $entry.Key -or $weapon.status -ne $entry.status) { throw "Entry identity/status mismatch: $manifestPath" }
        $familyRoot = Split-Path $manifestPath
        foreach ($file in $weapon.files) {
            $path = Resolve-Contained $familyRoot $file.path
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing output: $path" }
            $fileCount++
            if ($VerifyHashes) {
                if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw "Hash mismatch: $path" }
                $hashCount++
            }
        }
        foreach ($covered in $weapon.export.coveredBy) {
            $null = Get-Item -LiteralPath (Resolve-Contained $root $covered.originalReport)
        }
        foreach ($variant in $weapon.export.variants) {
            if (-not $variant.stages.model.manifest) { continue }
            $modelPath = Resolve-Contained $familyRoot $variant.stages.model.manifest
            $model = Get-Content -LiteralPath $modelPath -Raw | ConvertFrom-Json
            foreach ($mesh in $model.meshes) {
                if ($mesh.sourceMesh) { $meshCoverage[(Mesh-Key $mesh.sourceMesh)] = $modelPath }
            }
        }
        if (@($weapon.source.roots).Count -eq 0 -and $weapon.source.Meshes) {
            $rootless.Add([PSCustomObject]@{ key=$entry.Key; meshes=$weapon.source.Meshes })
        }
        $entries.Add([PSCustomObject]@{ key=$entry.Key; name=$entry.Name; status=$entry.status;
            manifest=$manifestPath; rooted=(@($weapon.source.roots).Count -gt 0);
            errors=@($weapon.export.errors); modelCount=@($weapon.export.variants.stages.model | Where-Object path).Count })
    }
    foreach ($file in $ledger.sharedFiles) {
        $path = Resolve-Contained $root $file.path
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw "Shared file hash mismatch: $path" }
    }
}
if (@(Compare-Object $expected @($seen | Sort-Object)).Count -ne 0) { throw 'Export keys differ from the full catalog' }
$result = [ordered]@{ status='AllCatalogEntriesAccounted'; catalogCount=$expected.Count; accounted=$seen.Count;
    sourceFingerprint=$catalogData.sourceFingerprint; filesChecked=$fileCount; fileHashesChecked=$hashCount;
    statuses=@($entries | Group-Object status | ForEach-Object { @{ status=$_.Name; count=$_.Count } });
    failed=@($entries | Where-Object status -eq Failed);
    crossShardExactMeshCoverage=@(foreach ($family in $rootless) {
        $links=@(foreach ($mesh in $family.meshes) { $key=Mesh-Key $mesh; if ($meshCoverage.ContainsKey($key)) { $meshCoverage[$key] } })
        if ($links.Count -eq @($family.meshes).Count) { @{ key=$family.key; modelManifests=$links } }
    });
    note='Independent shards do not share cross-record coverage aliases. File/accounting validation does not establish Unity import or playback.' }
$result | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $Report -Encoding utf8
Write-Output "PASS all $($seen.Count) catalog entries accounted; $fileCount files exist, $hashCount hashes verified. See $Report for explicit failures."
