param(
    [Parameter(Mandatory)][string]$CandidateGridPath,
    [Parameter(Mandatory)][string]$FirstRunRoot,
    [Parameter(Mandatory)][string]$ReplacementPupilRoot,
    [Parameter(Mandatory)][string]$OutputRoot
)

$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputRoot){throw "Choose a new output root: $OutputRoot"}
$grid=Get-Content -LiteralPath $CandidateGridPath -Raw|ConvertFrom-Json
$old=Get-Content -LiteralPath (Join-Path $FirstRunRoot 'export-report.json') -Raw|ConvertFrom-Json
$provenance=Get-Content -LiteralPath (Join-Path $FirstRunRoot 'build-provenance.json') -Raw|ConvertFrom-Json
$replacement=Get-Content -LiteralPath (Join-Path $ReplacementPupilRoot 'manifest.json') -Raw|ConvertFrom-Json
if($replacement.root.Name -ne 'Beyd_Avatar_Boy_Pupil_P0061' -or
    $replacement.root.PathID -ne '4081576452364043754'){throw 'Unexpected replacement source identity'}
$output=[IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $output|Out-Null
$rows=[Collections.Generic.List[object]]::new()
foreach($cell in $grid.cells|Where-Object {$_.category -in @('Eye Brow','Eye / Pupil')}){
    $slot=if($cell.category -eq 'Eye Brow'){'Eyebrow'}else{'Pupil'}
    foreach($candidate in $cell.candidates){
        $id=($candidate.label -split '_')[-1]
        $relative="$($cell.body)/$slot/$id"
        $from=if($cell.body -eq 'Boy' -and $slot -eq 'Pupil' -and $id -eq 'P0061'){
            $ReplacementPupilRoot
        }else{
            $record=@($old.records|Where-Object {$_.label -eq $candidate.label -and
                $_.sourceRef.pathId -eq $candidate.sourceRef.pathId -and $_.status -eq 'exported'})
            if($record.Count -ne 1){throw "No exported source identity: $($candidate.label)"}
            Join-Path $FirstRunRoot $record[0].output
        }
        $target=Join-Path $output $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force|Out-Null
        Copy-Item -LiteralPath $from -Destination $target -Recurse
        $manifest=Get-Content -LiteralPath (Join-Path $target 'manifest.json') -Raw|ConvertFrom-Json
        if($manifest.root.Name -cne $candidate.label -or $manifest.root.PathID -ne $candidate.sourceRef.pathId -or
            @($manifest.unresolved).Count -ne 0 -or @($manifest.nativeTextureFailures).Count -ne 0 -or
            @($manifest.materialBindings|Where-Object {$_.status -ne 'resolved'}).Count -ne 0){
            throw "Copied export identity/payload unresolved: $($candidate.label)"
        }
        $payloads=@(Get-ChildItem -LiteralPath $target -Recurse -File|ForEach-Object {
            [ordered]@{path=[IO.Path]::GetRelativePath($output,$_.FullName).Replace('\','/');
                kind=$_.Extension.TrimStart('.').ToLowerInvariant();
                sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
        })
        $rows.Add([ordered]@{body=$cell.body;category=$cell.category;id=$candidate.id;
            sourceRef=$candidate.sourceRef;status='exported';output=$relative.Replace('\','/');
            meshes=@($manifest.meshes).Count;bindings=@($manifest.materialBindings).Count;
            nativeFiles=@(Get-ChildItem -LiteralPath $target -Filter *.astexture).Count;
            payloads=$payloads})
    }
}
if($rows.Count -ne 20){throw 'Expected twenty exact face part exports'}
$report=[ordered]@{schemaVersion=1;kind='manekin-20-selected-face-parts';
    sourceCandidateGrid=[IO.Path]::GetFullPath($CandidateGridPath);
    sourceFirstRun=[IO.Path]::GetFullPath($FirstRunRoot);
    sourceReplacement=[IO.Path]::GetFullPath($ReplacementPupilRoot);
    binaryFilesSha256=$provenance.binaryFilesSha256;
    sourceMapSha256=$provenance.sourceMapSha256;sceneIndexSha256=$provenance.sceneIndexSha256;
    count=$rows.Count;records=@($rows);
    limitations='Independent exact part geometry/payloads exported; modular compatibility, animation and native selection remain unproved for most choices.'}
$report|ConvertTo-Json -Depth 14|Set-Content -LiteralPath (Join-Path $output 'face-part-payload-audit.json')
[pscustomobject]@{count=$rows.Count;meshes=20;output=$output}|ConvertTo-Json
