param(
    [Parameter(Mandatory)][string]$RunRoot,
    [string]$AuditName='look-payload-audit.json'
)

$ErrorActionPreference='Stop'
$report=Get-Content -LiteralPath (Join-Path $RunRoot 'look-assembly-report.json') -Raw|ConvertFrom-Json
if(@($report.records).Count -ne 10){throw 'Expected ten candidate head looks'}
$auditPath=Join-Path $RunRoot $AuditName
if(Test-Path -LiteralPath $auditPath){throw "Choose a fresh audit path: $auditPath"}
$rows=[Collections.Generic.List[object]]::new()
$payloads=[Collections.Generic.List[object]]::new()
foreach($record in $report.records){
    if($record.status -ne 'assembled'){throw "Candidate geometry failed: $($record.body) S$($record.setId)"}
    $folder=Join-Path $RunRoot $record.output
    $manifest=Get-Content -LiteralPath (Join-Path $folder 'manifest.json') -Raw|ConvertFrom-Json
    $brow=@($manifest.assembly|Where-Object {$_.result.slot -eq 'Eyebrow'})
    $pupil=@($manifest.assembly|Where-Object {$_.result.slot -eq 'Pupil'})
    $hair=@($manifest.assembly|Where-Object {$_.result.slot -eq 'Hair'})
    if($hair.Count -ne 1 -or
        $hair[0].source.Name -cne $record.hairSource.name -or
        "$($hair[0].source.PathID)" -ne "$($record.hairSource.pathId)" -or
        $hair[0].source.Source -cne $record.hairSource.source){
        throw "Hair source identity mismatch: $($record.body) S$($record.setId)"
    }
    if($brow.Count -ne 1 -or $pupil.Count -ne 1 -or
        $brow[0].source.Name -cne $record.brow.label -or
        "$($brow[0].source.PathID)" -ne "$($record.brow.sourceRef.pathId)" -or
        $brow[0].source.Source -cne $record.brow.sourceRef.source -or
        $pupil[0].source.Name -cne $record.pupil.label -or
        "$($pupil[0].source.PathID)" -ne "$($record.pupil.sourceRef.pathId)" -or
        $pupil[0].source.Source -cne $record.pupil.sourceRef.source){
        throw "Brow or pupil source identity mismatch: $($record.body) S$($record.setId)"
    }
    if(@($manifest.unresolved).Count -ne 0 -or @($manifest.nativeTextureFailures).Count -ne 0 -or
        @($manifest.materialBindings|Where-Object status -ne 'resolved').Count -ne 0){
        throw "Unresolved source/material/native binding: $($record.body) S$($record.setId)"
    }
    $references=0
    foreach($binding in $manifest.materialBindings){
        $textures=@($binding.textures.PSObject.Properties)
        if($textures.Count -ne @($binding.nativeTextures.PSObject.Properties).Count -or
            $textures.Count -ne @($binding.textureImport.PSObject.Properties).Count){
            throw "Texture policy count mismatch: $($binding.rendererPath)"
        }
        foreach($property in $textures){
            $native=$binding.nativeTextures.($property.Name)
            $intent=$binding.textureImport.($property.Name)
            if(-not $native -or -not $intent.evidence -or $intent.mipCount -lt 1 -or
                -not(Test-Path -LiteralPath (Join-Path $folder $native))){
                throw "Missing source-backed native texture: $($binding.rendererPath) $($property.Name)"
            }
            $references++
        }
    }
    $files=@(Get-ChildItem -LiteralPath $folder -Recurse -File)
    foreach($file in $files){
        $payloads.Add([ordered]@{path=[IO.Path]::GetRelativePath($RunRoot,$file.FullName).Replace('\','/');
            sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash;
            kind=$file.Extension.TrimStart('.').ToLowerInvariant()})
    }
    $rows.Add([ordered]@{body=$record.body;setId=$record.setId;
        hairSourceSetId=$record.hairSourceSetId;hair=$record.hairSource.name;
        brow=$record.brow.label;pupil=$record.pupil.label;
        meshes=@($manifest.meshes).Count;materialBindings=@($manifest.materialBindings).Count;
        nativeReferences=$references;nativeFiles=@($files|Where-Object Extension -eq '.astexture').Count;
        exclusiveRestOverrides=@($manifest.assembly|ForEach-Object {$_.result.exclusiveRestOverrides}|Where-Object {$_}).Count;
        sourceStatus='assembled';unityStatus='not checked by this source audit'})
}
$audit=[ordered]@{schemaVersion=1;kind='manekin-ten-candidate-head-look-payload-audit';
    sourceReport='look-assembly-report.json';sourceBatch=$report.sourceBatch;
    candidateGrid=$report.candidateGrid;binaryFilesSha256=$report.binaryFilesSha256;
    sourceMapSha256=$report.sourceMapSha256;sceneIndexSha256=$report.sceneIndexSha256;
    records=@($rows);payloads=@($payloads);
    limitations='Geometry/source bindings only; native selection, generated skin targets, animation pivots and rendered fidelity are not established.'}
$audit|ConvertTo-Json -Depth 11|Set-Content -LiteralPath $auditPath
$meshTotal=0;$referenceTotal=0
foreach($row in $rows){$meshTotal+=[int]$row.meshes;$referenceTotal+=[int]$row.nativeReferences}
[pscustomobject]@{looks=$rows.Count;meshes=$meshTotal;
    nativeReferences=$referenceTotal;
    payloads=$payloads.Count;output=$auditPath}|ConvertTo-Json
