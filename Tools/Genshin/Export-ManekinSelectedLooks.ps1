param(
    [Parameter(Mandatory)][string]$BatchRoot,
    [Parameter(Mandatory)][string]$CandidateGridPath,
    [Parameter(Mandatory)][string]$BoyBaseManifest,
    [Parameter(Mandatory)][string]$GirlBaseManifest,
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
    [ValidateSet('Boy','Girl')][string]$OnlyBody,
    [string]$OnlySetId,
    [ValidateRange(0,4)][int]$HairRotation=0
)

$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
if(Test-Path -LiteralPath $OutputRoot){throw "Choose a new output root: $OutputRoot"}
$output=[IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $output|Out-Null
$batch=Get-Content -LiteralPath (Join-Path $BatchRoot 'batch-report.json') -Raw|ConvertFrom-Json
$grid=Get-Content -LiteralPath $CandidateGridPath -Raw|ConvertFrom-Json
$index=Get-Content -LiteralPath (Join-Path $RepositoryRoot 'docs/Genshin/Maps/manekin-scene-index/scene-index.json') -Raw|ConvertFrom-Json
$map=(Join-Path $RepositoryRoot 'docs/Genshin/Maps/genshin-7.1.map')+'|'+
    (Join-Path $RepositoryRoot 'docs/Genshin/Maps/manekin-scene-index/scene-index.json')
$distDir=Join-Path $RepositoryRoot 'dist/net10.0-windows'
$binDir=Join-Path $distDir 'bin'
$exe=Join-Path $distDir 'AnimeStudio.CLI.exe'
$bin=[ordered]@{}
foreach($name in @('AnimeStudio.CLI.exe','AnimeStudio.CLI.dll','AnimeStudio.dll',
    'AnimeStudio.Utility.dll','AnimeStudio.FBXNative.dll','AnimeStudio.FBXWrapper.dll',
    'Texture2DDecoderNative.dll','Texture2DDecoderWrapper.dll')){
    $path=if($name -eq 'AnimeStudio.CLI.exe'){$exe}else{Join-Path $binDir $name}
    $bin[$name]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}
$bases=@{
    Boy=Get-Content -LiteralPath $BoyBaseManifest -Raw|ConvertFrom-Json
    Girl=Get-Content -LiteralPath $GirlBaseManifest -Raw|ConvertFrom-Json
}
function ExactCell([string]$body,[string]$category){
    $matches=@($grid.cells|Where-Object {$_.body -eq $body -and $_.category -eq $category})
    if($matches.Count -ne 1 -or @($matches[0].candidates).Count -ne 5){throw "Missing five exact candidates: $body $category"}
    return $matches[0]
}
function Part([string]$slot,$source,[string[]]$remove){
    return [ordered]@{slot=$slot;root=[ordered]@{name=$source.name;pathID="$($source.pathId)";
        type='GameObject';source=$source.source};removeMeshes=@($remove)}
}
$femaleFaces=@($index.AssetEntries|Where-Object {$_.Type -eq 'GameObject' -and $_.Name -eq 'Beyd_Avatar_Girl_Face_P0051'})
if($femaleFaces.Count -ne 1){throw 'Girl Face P0051 source root missing or ambiguous'}
$faceSource=[ordered]@{name=$femaleFaces[0].Name;pathId="$($femaleFaces[0].PathID)";
    source=$femaleFaces[0].Source}
$records=[Collections.Generic.List[object]]::new()
foreach($body in @('Boy','Girl')){
    if($OnlyBody -and $body -ne $OnlyBody){continue}
    $base=$bases[$body]
    $basePaths=@($base.meshes|ForEach-Object {$_.Path.Substring($base.root.Name.Length+1)})
    $brows=ExactCell $body 'Eye Brow'
    $pupils=ExactCell $body 'Eye / Pupil'
    $sets=@($batch.selectedSets.$body)
    if($sets.Count -ne 5){throw "Expected five $body S groups"}
    for($i=0;$i -lt 5;$i++){
        $setId=$sets[$i]
        $hairSetId=$sets[($i+$HairRotation)%5]
        if($OnlySetId -and $setId -ne $OnlySetId){continue}
        $brow=$brows.candidates[$i]
        $pupil=$pupils.candidates[$i]
        $parts=[Collections.Generic.List[object]]::new()
        if($body -eq 'Girl'){
            $parts.Add((Part 'Face' $faceSource @()))
            $parts.Add((Part 'Pupil' ([ordered]@{name=$pupil.label;pathId=$pupil.sourceRef.pathId;
                source=$pupil.sourceRef.source}) @()))
        }
        $setParts=@($batch.records|Where-Object {$_.body -eq $body -and $_.setId -eq $setId})
        if($setParts.Count -lt 4 -or @($setParts|Where-Object status -ne 'exported').Count -ne 0){
            throw "Incomplete source S group $body S$setId"
        }
        foreach($entry in $setParts){
            $selectedEntry=$entry
            if($entry.slot -eq 'Hair' -and $HairRotation -ne 0){
                $matchingHair=@($batch.records|Where-Object {
                    $_.body -eq $body -and $_.setId -eq $hairSetId -and $_.slot -eq 'Hair' -and $_.status -eq 'exported'
                })
                if($matchingHair.Count -ne 1){throw "Missing exact rotated Hair source: $body S$hairSetId"}
                $selectedEntry=$matchingHair[0]
            }
            [string[]]$remove=@()
            if($body -eq 'Boy'){
                $remove=[string[]]@($basePaths|Where-Object {$_.StartsWith($entry.slot+'_S0017',[StringComparison]::Ordinal)})
            }
            $parts.Add((Part $entry.slot ([ordered]@{name=$selectedEntry.source.name;
                pathId=$selectedEntry.source.pathId;source=$selectedEntry.source.file}) $remove))
        }
        if($body -eq 'Boy'){
            [string[]]$pupilRemove=@($basePaths|Where-Object {$_.StartsWith('Pupil_',[StringComparison]::Ordinal)})
            [string[]]$browRemove=@($basePaths|Where-Object {$_.StartsWith('Eyebrow_',[StringComparison]::Ordinal)})
            if($pupilRemove.Count -ne 1 -or $browRemove.Count -ne 1){throw 'Expected one Boy base pupil and eyebrow renderer'}
            $parts.Add((Part 'Pupil' ([ordered]@{name=$pupil.label;pathId=$pupil.sourceRef.pathId;
                source=$pupil.sourceRef.source}) $pupilRemove))
            $parts.Add((Part 'Eyebrow' ([ordered]@{name=$brow.label;pathId=$brow.sourceRef.pathId;
                source=$brow.sourceRef.source}) $browRemove))
        }else{
            $parts.Add((Part 'Eyebrow' ([ordered]@{name=$brow.label;pathId=$brow.sourceRef.pathId;
                source=$brow.sourceRef.source}) @()))
        }
        $request=[ordered]@{base=[ordered]@{name=$base.root.Name;pathID="$($base.root.PathID)";
            type=$base.root.Type;source=$base.root.Source};parts=@($parts)}
        $relative=if($HairRotation -eq 0){"$body/S$setId"}else{"$body/S$setId-hair-S$hairSetId"}
        $folder=Join-Path $output $relative
        $requestPath=Join-Path $output "$relative-request.json"
        New-Item -ItemType Directory -Path (Split-Path -Parent $requestPath) -Force|Out-Null
        $request|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $requestPath
        $log=@(& $exe --genshin-assemble $map $requestPath $folder 2>&1)
        $status=if($LASTEXITCODE -eq 0){'assembled'}else{'failed'}
        $meshes=$null;$bindings=$null;$overrides=$null;$native=$null
        if($status -eq 'assembled' -and (Test-Path -LiteralPath (Join-Path $folder 'manifest.json'))){
            $manifest=Get-Content -LiteralPath (Join-Path $folder 'manifest.json') -Raw|ConvertFrom-Json
            $meshes=@($manifest.meshes).Count;$bindings=@($manifest.materialBindings).Count
            $overrides=@($manifest.assembly|ForEach-Object {$_.result.exclusiveRestOverrides}|Where-Object {$_}).Count
            $native=@(Get-ChildItem -LiteralPath $folder -Filter *.astexture).Count
            if(@($manifest.unresolved).Count -ne 0 -or @($manifest.nativeTextureFailures).Count -ne 0 -or
                @($manifest.materialBindings|Where-Object status -ne 'resolved').Count -ne 0){$status='payload-unresolved'}
        }
        $hairSourceEntry=@($batch.records|Where-Object {
            $_.body -eq $body -and $_.setId -eq $hairSetId -and $_.slot -eq 'Hair'
        })
        if($hairSourceEntry.Count -ne 1){throw "Missing Hair source record: $body S$hairSetId"}
        $records.Add([ordered]@{body=$body;setId=$setId;index=$i;hairRotation=$HairRotation;
            hairSourceSetId=$hairSetId;
            hairSource=[ordered]@{name=$hairSourceEntry[0].source.name;
                pathId=$hairSourceEntry[0].source.pathId;source=$hairSourceEntry[0].source.file};
            brow=[ordered]@{label=$brow.label;sourceRef=$brow.sourceRef};
            pupil=[ordered]@{label=$pupil.label;sourceRef=$pupil.sourceRef};
            status=$status;request=[IO.Path]::GetRelativePath($output,$requestPath).Replace('\','/');
            output=$relative;meshes=$meshes;bindings=$bindings;nativeFiles=$native;
            exclusiveRestOverrides=$overrides;log=($log|Out-String).Trim()})
        $report=[ordered]@{schemaVersion=1;kind='manekin-ten-candidate-head-look-oracles';
            hairRotation=$HairRotation;
            combinationKind=if($HairRotation -eq 0){'selected S group'}else{'custom multipart Hair rotation; no native preset claim'};
            status='source geometry only; no native look preset, generated skin, selection registry, animation pivot or visual fidelity certification';
            sourceBatch=[IO.Path]::GetFullPath($BatchRoot);
            sourceBatchReportSha256=(Get-FileHash -LiteralPath (Join-Path $BatchRoot 'batch-report.json') -Algorithm SHA256).Hash;
            candidateGrid=[IO.Path]::GetFullPath($CandidateGridPath);
            candidateGridSha256=(Get-FileHash -LiteralPath $CandidateGridPath -Algorithm SHA256).Hash;
            sourceMapSha256=(Get-FileHash -LiteralPath (Join-Path $RepositoryRoot 'docs/Genshin/Maps/genshin-7.1.map') -Algorithm SHA256).Hash;
            sceneIndexSha256=(Get-FileHash -LiteralPath (Join-Path $RepositoryRoot 'docs/Genshin/Maps/manekin-scene-index/scene-index.json') -Algorithm SHA256).Hash;
            binaryFilesSha256=$bin;records=@($records)}
        $report|ConvertTo-Json -Depth 14|Set-Content -LiteralPath (Join-Path $output 'look-assembly-report.json')
        Write-Host "$body S$setId Hair S$hairSetId brow $($brow.label) pupil $($pupil.label): $status"
    }
}
