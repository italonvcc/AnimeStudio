param(
    [Parameter(Mandatory)][string]$BatchRoot,
    [Parameter(Mandatory)][string]$BoyBaseManifest,
    [Parameter(Mandatory)][string]$GirlBaseManifest,
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (Test-Path -LiteralPath $OutputRoot) { throw "Choose a new output root: $OutputRoot" }
$output=[System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $output | Out-Null
$batch=Get-Content -LiteralPath (Join-Path $BatchRoot 'batch-report.json') -Raw|ConvertFrom-Json
$bases=[ordered]@{
    Boy=Get-Content -LiteralPath $BoyBaseManifest -Raw|ConvertFrom-Json
    Girl=Get-Content -LiteralPath $GirlBaseManifest -Raw|ConvertFrom-Json
}
$map=(Join-Path $RepositoryRoot 'docs/Genshin/Maps/genshin-7.1.map')+'|'+
    (Join-Path $RepositoryRoot 'docs/Genshin/Maps/manekin-scene-index/scene-index.json')
$exe=Join-Path $RepositoryRoot 'dist/net10.0-windows/AnimeStudio.CLI.exe'
$records=[System.Collections.Generic.List[object]]::new()
foreach ($body in @('Boy','Girl')) {
    $base=$bases[$body]
    $baseMeshPaths=@($base.meshes|ForEach-Object {$_.Path.Substring($base.root.Name.Length+1)})
    foreach ($setId in $batch.selectedSets.$body) {
        $parts=@()
        foreach ($entry in @($batch.records|Where-Object {$_.body -eq $body -and $_.setId -eq $setId})) {
            $part=Get-Content -LiteralPath (Join-Path $BatchRoot ($entry.output+'/manifest.json')) -Raw|ConvertFrom-Json
            [string[]]$remove=@()
            if ($body -eq 'Boy') {
                $remove=[string[]]@($baseMeshPaths|Where-Object {$_.StartsWith($entry.slot+'_S0017',[StringComparison]::Ordinal)})
            }
            $parts+= [ordered]@{slot=$entry.slot;root=[ordered]@{name=$part.root.Name;
                pathID=$part.root.PathID;type=$part.root.Type;source=$part.root.Source};
                removeMeshes=$remove}
        }
        $request=[ordered]@{base=[ordered]@{name=$base.root.Name;pathID=$base.root.PathID;
            type=$base.root.Type;source=$base.root.Source};parts=$parts}
        $folder=Join-Path $output "$body/S$setId"
        New-Item -ItemType Directory -Path (Split-Path -Parent $folder) -Force|Out-Null
        $requestPath=Join-Path $output "$body/S$setId-request.json"
        $request|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $requestPath
        $log=& $exe --genshin-assemble $map $requestPath $folder 2>&1|Out-String
        $status=if ($LASTEXITCODE -ne 0) {'failed'} else {'assembled'}
        $manifestPath=Join-Path $folder 'manifest.json'
        $unresolved=$null;$bindingUnresolved=$null;$meshCount=$null;$addedBoneFrames=$null
        if ($status -eq 'assembled' -and (Test-Path -LiteralPath $manifestPath)) {
            $manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
            $unresolved=@($manifest.unresolved).Count
            $bindingUnresolved=@($manifest.materialBindings|Where-Object status -ne 'resolved').Count
            $meshCount=@($manifest.meshes).Count
            $addedBoneFrames=0
            foreach ($assembledPart in $manifest.assembly) { $addedBoneFrames += [int]$assembledPart.result.addedBoneFrames }
            if ($unresolved -gt 0 -or $bindingUnresolved -gt 0) {$status='payload-unresolved'}
        }
        $records.Add([ordered]@{body=$body;setId=$setId;status=$status;
            request=[System.IO.Path]::GetRelativePath($output,$requestPath).Replace('\','/');
            output=if(Test-Path -LiteralPath $manifestPath){[System.IO.Path]::GetRelativePath($output,$folder).Replace('\','/')}else{$null};
            partSlots=@($parts|ForEach-Object slot);meshCount=$meshCount;
            unresolved=$unresolved;bindingUnresolved=$bindingUnresolved;
            addedBoneFrames=$addedBoneFrames;log=$log.Trim()})
        [ordered]@{schemaVersion=1;sourceBatch=[System.IO.Path]::GetFullPath($BatchRoot);
            sourceBatchReportSha256=(Get-FileHash -LiteralPath (Join-Path $BatchRoot 'batch-report.json') -Algorithm SHA256).Hash;
            purpose='Structural source assembly probe; no native outfit preset or visibility semantics certified';
            records=@($records)}|ConvertTo-Json -Depth 15|Set-Content -LiteralPath (Join-Path $output 'assembly-probe-report.json')
        Write-Host "$body S$setId $status"
    }
}
