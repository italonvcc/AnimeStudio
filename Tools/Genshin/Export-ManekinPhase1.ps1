param(
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$root = [System.IO.Path]::GetFullPath($RepositoryRoot)
$indexPath = Join-Path $root 'docs/Genshin/Maps/manekin-scene-index/scene-index.json'
$map = (Join-Path $root 'docs/Genshin/Maps/genshin-7.1.map') + '|' + $indexPath
$exe = Join-Path $root 'dist/net10.0-windows/AnimeStudio.CLI.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Build the canonical net10 distribution first: $exe" }
if (Test-Path -LiteralPath $OutputRoot) { throw "Choose a new output root: $OutputRoot" }
$output = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $output | Out-Null
$index = Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json
$selection = & (Join-Path $PSScriptRoot 'Get-ManekinSetSelection.ps1') -IndexPath $indexPath
$sets = $selection.selectedSets
$records = [System.Collections.Generic.List[object]]::new()
$seed = $selection.seed
foreach ($sex in $sets.Keys) {
    foreach ($setId in $sets[$sex]) {
        $pattern = '^Beyd_Avatar_' + $sex + '_([A-Za-z]+)_S' + $setId + '$'
        $entries = @($index.AssetEntries | Where-Object { $_.Type -eq 'GameObject' -and $_.Name -match $pattern } | Sort-Object Name)
        $duplicates = @($entries | Group-Object Name | Where-Object Count -ne 1)
        if ($duplicates.Count -gt 0) { throw "Ambiguous source roots for $sex S$setId" }
        foreach ($required in 'Hair','Top','Bottom','Shoe') {
            if (@($entries | Where-Object Name -eq "Beyd_Avatar_${sex}_${required}_S$setId").Count -ne 1)
                { throw "Missing required $sex S$setId $required root" }
        }
        foreach ($entry in $entries) {
            $slot = [regex]::Match($entry.Name, $pattern).Groups[1].Value
            $relative = "$sex/Parts/$slot/S$setId-$($entry.PathID)"
            $destination = Join-Path $output $relative
            $parent = Split-Path -Parent $destination
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
            $log = & $exe --genshin-prefab $map ($entry.Name + '@' + $entry.PathID) $destination 2>&1 | Out-String
            $status = if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath (Join-Path $destination 'manifest.json'))) { 'exported' } else { 'failed' }
            $records.Add([ordered]@{
                body = $sex; setId = $setId; slot = $slot; source = [ordered]@{
                    name = $entry.Name; type = $entry.Type; pathId = "$($entry.PathID)";
                    container = "$($entry.Container)"; file = $entry.Source; offset = $entry.Offset
                }; output = $relative.Replace('\','/'); status = $status; log = $log.Trim()
            })
            $report = [ordered]@{ schemaVersion = 1; seed = $seed; selectedSets = $sets;
                selectionIndexSha256 = $selection.indexSha256; candidateSets = $selection.candidates;
                selectionAlgorithm = $selection.algorithm;
                mapPaths = @($map.Split('|')); exporterExeSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash;
                records = $records }
            $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $output 'batch-report.json')
            Write-Host "$sex S$setId $slot $status"
        }
    }
}
