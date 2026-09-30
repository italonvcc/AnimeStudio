param(
    [Parameter(Mandatory)][string]$BatchRoot,
    [Parameter(Mandatory)][string]$AssemblyRoot,
    [Parameter(Mandatory)][string]$SkinValuesPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)

$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputPath) { throw "Choose a new output path: $OutputPath" }
$batch=Get-Content -LiteralPath (Join-Path $BatchRoot 'batch-report.json') -Raw|ConvertFrom-Json
$assemblies=Get-Content -LiteralPath (Join-Path $AssemblyRoot 'assembly-probe-report.json') -Raw|ConvertFrom-Json
$scene=Get-Content -LiteralPath (Join-Path $RepositoryRoot 'docs/Genshin/Maps/manekin-scene-index/scene-index.json') -Raw|ConvertFrom-Json
$skin=Get-Content -LiteralPath $SkinValuesPath -Raw|ConvertFrom-Json
$map=Join-Path $RepositoryRoot 'docs/Genshin/Maps/genshin-7.1.map'
$cells=[Collections.Generic.List[object]]::new()
function CandidateId([string]$body,[string]$category,[string]$source,[string]$type,[string]$pathId) {
    $key=($body+'|'+$category+'|'+$source.Replace('\','/').ToLowerInvariant()+'|'+$type+'|'+$pathId)
    'gi-manekin-candidate-'+[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($key))).Substring(0,20).ToLowerInvariant()
}
function AddCell([string]$body,[string]$category,[object[]]$choices,[int]$sourceInventoryCount,[string]$gap) {
    if($choices.Count -ne 5 -or @($choices|ForEach-Object {$_['id']}|Sort-Object -Unique).Count -ne 5) {
        throw "Expected five distinct source candidates for $body/$category"
    }
    $cells.Add([ordered]@{body=$body;category=$category;target=5;
        sourceInventoryCount=$sourceInventoryCount;candidateCount=5;
        acceptedCount=0;status='unsupported';remainingProof=$gap;
        candidates=$choices})
}
function SourceCandidate($entry,[string]$body,[string]$category,[string]$reason) {
    $source=if($entry.PSObject.Properties.Name -contains 'Source'){$entry.Source}else{$entry.source}
    $pathId=if($entry.PSObject.Properties.Name -contains 'PathID'){$entry.PathID}else{$entry.pathId}
    [ordered]@{id=(CandidateId $body $category $source $entry.Type ([string]$pathId));
        label=$entry.Name;status='unsupported';reason=$reason;
        sourceRef=[ordered]@{source=$source;type=$entry.Type;pathId=[string]$pathId;
            container=$entry.Container;offset=$entry.Offset}}
}
function ChooseGeometry([string]$body,[string]$part,[string]$anchor) {
    $pattern="^Beyd_Avatar_${body}_${part}_P[0-9]{4}$"
    $all=@($scene.AssetEntries|Where-Object {$_.Type -eq 'GameObject' -and $_.Name -match $pattern -and
        (Test-Path -LiteralPath $_.Source)})
    $unique=@($all|Group-Object Name|ForEach-Object {
        $_.Group|Sort-Object @{Expression={if($_.Source -like '*12859995.blk'){0}else{1}}},Source,PathID|Select-Object -First 1
    }|Sort-Object Name)
    $selected=@()
    if($anchor) { $selected+=@($unique|Where-Object {$_.Name -eq "Beyd_Avatar_${body}_${part}_$anchor"}|Select-Object -First 1) }
    $selected+=@($unique|Where-Object {$_.Name -ne "Beyd_Avatar_${body}_${part}_$anchor"}|Select-Object -First (5-$selected.Count))
    [pscustomobject]@{all=$unique;selected=$selected}
}
foreach($body in @('Boy','Girl')) {
    $setIds=@($batch.selectedSets.$body)
    $hair=@(foreach($setId in $setIds) {
        $entry=@($batch.records|Where-Object {$_.body -eq $body -and $_.setId -eq $setId -and $_.slot -eq 'Hair'})
        if($entry.Count -ne 1) { throw "Missing exact hair part $body S$setId" }
        $m=Get-Content -LiteralPath (Join-Path $BatchRoot ($entry[0].output+'/manifest.json')) -Raw|ConvertFrom-Json
        [ordered]@{id=(CandidateId $body 'Hair' $m.root.Source $m.root.Type $m.root.PathID);
            label=$m.root.Name;status='unsupported';geometryStatus='exported';
            reason='Individual FBX exported; full appearance/animation choice rule not certified';
            sourceRef=[ordered]@{source=$m.root.Source;type=$m.root.Type;
                serializedFile=$m.root.SerializedFile;pathId=$m.root.PathID;
                container=$entry[0].source.container};
            partManifest=($entry[0].output+'/manifest.json').Replace('\','/')}
    })
    AddCell $body 'Hair' $hair $hair.Count 'Per-choice body, visibility and animation compatibility'
    foreach($item in @(@{category='Eye Brow';part='Eyebrow';anchor='P0141'},
        @{category='Eye / Pupil';part='Pupil';anchor='P0057'})) {
        $picked=ChooseGeometry $body $item.part $item.anchor
        $choices=@($picked.selected|ForEach-Object {
            SourceCandidate $_ $body $item.category 'Exact GameObject map entry; only one selected Girl source combination has a guarded geometry oracle'
        })
        AddCell $body $item.category $choices $picked.all.Count 'Exported mesh, rig compatibility, native selection and material binding for each candidate'
    }
    foreach($item in @(@{category='Eye Makeup';sourcePart='EyeShadow';anchor='P0045'},
        @{category='Lip Stick';sourcePart='Lipstick';anchor='P0475'},
        @{category='Facial Makeup';sourcePart='FacialMakeup';anchor='P0074'})) {
        $queryPath=Join-Path $env:TEMP "manekin-fullmap-$($body)_$($item.sourcePart)-20260929.json"
        $query=Get-Content -LiteralPath $queryPath -Raw|ConvertFrom-Json
        $pattern="^Beyd_Avatar_${body}_$($item.sourcePart)_P[0-9]{4}_01$"
        $all=@($query.entries|Where-Object {$_.Type -eq 'MonoBehaviour' -and $_.Name -match $pattern}|Sort-Object Name)
        $anchorName="Beyd_Avatar_${body}_$($item.sourcePart)_$($item.anchor)_01"
        $selected=@($all|Where-Object {$_.Name -eq $anchorName}|Select-Object -First 1)
        $selected+=@($all|Where-Object {$_.Name -ne $anchorName}|Select-Object -First (5-$selected.Count))
        $choices=@($selected|ForEach-Object {
            SourceCandidate $_ $body $item.category 'Exact MonoBehaviour map entry; full values, source dependencies and application registry not certified per candidate'
        })
        AddCell $body $item.category $choices $all.Count 'Decode all selected records, exact texture dependencies, face layering/application registry and live material result'
    }
    $skinIds=@('P0151','P0154','P0156','P0161','P0279')
    $skinChoices=@(foreach($id in $skinIds) {
        $r=@($skin.records|Where-Object {$_.Name -eq "Beyd_Avatar_Girl_SkinTone_${id}_01"})
        if($r.Count -ne 1) { throw "Missing exact skin source $id" }
        $record=$r[0]
        $observed=@()
        if($body -eq 'Boy' -and $id -in @('P0154','P0279')) {$observed=@('Boy FacePS10 capture color pair')}
        if($body -eq 'Girl' -and $id -in @('P0151','P0156','P0161')) {$observed=@('Girl FacePS10 capture color pair')}
        [ordered]@{id=(CandidateId $body 'Skin Tone' $record.Source 'MonoBehaviour' $record.PathID);
            label=$record.Name;status='unsupported';
            reason=if($body -eq 'Boy'){'Girl-named source palette; only P0154/P0279 observed in Boy captures, no cross-body selection registry'}
                else{'Exact source values decoded, but application registry and generated skin target unproved'};
            sourceRef=[ordered]@{source=$record.Source;type='MonoBehaviour';
                serializedFile=$record.serializedFile;pathId=$record.PathID;rawSha256=$record.rawSha256};
            observed=$observed;parameters=$record.values}
    })
    AddCell $body 'Skin Tone' $skinChoices 14 'Cross-body palette registry, generated skin texture composition and material parameter application'
    foreach($category in @('Outfit sets','Multi part outfits')) {
        $choices=@(foreach($setId in $setIds) {
            $a=@($assemblies.records|Where-Object {$_.body -eq $body -and $_.setId -eq $setId})
            if($a.Count -ne 1 -or $a[0].status -ne 'assembled') { throw "Missing source merged oracle $body S$setId" }
            [ordered]@{id="gi-manekin-$($body.ToLowerInvariant())-$($category.Replace(' ','-'))-s$setId";
                label="${body} S$setId";status='unsupported';geometryStatus='exported';
                reason='Source structural oracle exists; no native complete/multipart visibility or saved-preset rule';
                sourceGroup="S$setId";partSlots=@($a[0].partSlots);
                mergedManifest=($a[0].output+'/manifest.json').Replace('\','/');
                assemblyRequest=$a[0].request}
        })
        AddCell $body $category $choices $setIds.Count 'Native preset membership, required/excluded slots, underlying mesh visibility and ordinary animated look'
    }
}
if($cells.Count -ne 18 -or (@($cells|ForEach-Object {$_.candidates}).Count) -ne 90) {
    throw 'Unexpected source candidate grid size'
}
$report=[ordered]@{schemaVersion=1;kind='manekin-90-source-candidate-grid';
    status='inventory only; zero accepted appearance choices or complete/multipart recipes';
    sourceMapSha256=(Get-FileHash -LiteralPath $map -Algorithm SHA256).Hash;
    sceneIndexSha256=(Get-FileHash -LiteralPath (Join-Path $RepositoryRoot 'docs/Genshin/Maps/manekin-scene-index/scene-index.json') -Algorithm SHA256).Hash;
    outfitSelectionSeed=$batch.seed;selectedSets=$batch.selectedSets;
    cells=@($cells);cellCount=$cells.Count;candidateCount=90;acceptedCount=0;
    limitations='Source map name/object identity and geometry oracles are narrower than native menu selection, UGC materialization and 1:1 reference fidelity.'}
$report|ConvertTo-Json -Depth 18|Set-Content -LiteralPath $OutputPath
[pscustomobject]@{cells=$cells.Count;candidates=90;accepted=0;output=$OutputPath}|ConvertTo-Json
