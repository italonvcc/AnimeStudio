param(
    [string]$IndexPath = (Resolve-Path (Join-Path $PSScriptRoot '../../docs/Genshin/Maps/manekin-scene-index/scene-index.json')).Path,
    [int]$Seed = 20260929,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$index = Get-Content -LiteralPath $IndexPath -Raw | ConvertFrom-Json
$rng = [Random]::new($Seed)
$candidates = [ordered]@{}
$selected = [ordered]@{}
foreach ($sex in @('Boy','Girl')) {
    $pattern = '^Beyd_Avatar_' + $sex + '_([A-Za-z]+)_S([0-9]{4})$'
    $roots = @($index.AssetEntries | Where-Object { $_.Type -eq 'GameObject' -and $_.Name -match $pattern })
    $eligible = @($roots | Group-Object { [regex]::Match($_.Name, 'S([0-9]{4})$').Groups[1].Value } |
        Where-Object {
            $names = @($_.Group.Name)
            $setId = $_.Name
            foreach ($slot in @('Hair','Top','Bottom','Shoe')) {
                if (@($names | Where-Object { $_ -eq "Beyd_Avatar_${sex}_${slot}_S$setId" }).Count -ne 1) {
                    return $false
                }
            }
            return $true
        } | Sort-Object Name)
    $ids = @($eligible | ForEach-Object Name)
    if ($ids.Count -lt 5) { throw "Fewer than five source-backed $sex sets" }
    $candidates[$sex] = $ids
    # One RNG is deliberately shared across both bodies. Sort-Object evaluates
    # the random key once per candidate in candidate order, then takes five.
    $selected[$sex] = @($ids | Sort-Object { $rng.Next() } | Select-Object -First 5)
}
$report = [ordered]@{
    schemaVersion = 1
    seed = $Seed
    algorithm = 'Sort GameObject sets by four-digit ID; require one Hair, Top, Bottom, Shoe root; Boy then Girl, shared .NET Random(seed), Sort-Object { rng.Next() }, first five'
    indexPath = [System.IO.Path]::GetFullPath($IndexPath)
    indexSha256 = (Get-FileHash -LiteralPath $IndexPath -Algorithm SHA256).Hash
    candidates = $candidates
    selectedSets = $selected
}
if ($OutputPath) {
    if (Test-Path -LiteralPath $OutputPath) { throw "Choose a new output path: $OutputPath" }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath
}
$report
