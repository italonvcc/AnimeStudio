param(
    [Parameter(Mandatory)][string]$CandidateGridPath,
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)

$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
if(Test-Path -LiteralPath $OutputRoot){throw "Choose a new output root: $OutputRoot"}
$root=[IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $root|Out-Null
$grid=Get-Content -LiteralPath $CandidateGridPath -Raw|ConvertFrom-Json
$map=(Join-Path $RepositoryRoot 'docs/Genshin/Maps/genshin-7.1.map')+'|'+
    (Join-Path $RepositoryRoot 'docs/Genshin/Maps/manekin-scene-index/scene-index.json')
$distDir=Join-Path $RepositoryRoot 'dist/net10.0-windows'
$exe=Join-Path $distDir 'AnimeStudio.CLI.exe'
$binDir=Join-Path $distDir 'bin'
$names=@('AnimeStudio.CLI.exe','AnimeStudio.CLI.dll','AnimeStudio.dll','AnimeStudio.Utility.dll',
    'AnimeStudio.FBXNative.dll','AnimeStudio.FBXWrapper.dll','Texture2DDecoderNative.dll','Texture2DDecoderWrapper.dll')
$bin=[ordered]@{}
foreach($name in $names){
    $path=if($name -eq 'AnimeStudio.CLI.exe'){$exe}else{Join-Path $binDir $name}
    $bin[$name]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}
[ordered]@{schemaVersion=1;gitRevision=(& git -C $RepositoryRoot rev-parse HEAD).Trim();
    candidateGrid=[IO.Path]::GetFullPath($CandidateGridPath);binaryFilesSha256=$bin;
    sourceMapSha256=(Get-FileHash (Join-Path $RepositoryRoot 'docs/Genshin/Maps/genshin-7.1.map') -Algorithm SHA256).Hash;
    sceneIndexSha256=(Get-FileHash (Join-Path $RepositoryRoot 'docs/Genshin/Maps/manekin-scene-index/scene-index.json') -Algorithm SHA256).Hash}|ConvertTo-Json -Depth 8|
    Set-Content -LiteralPath (Join-Path $root 'build-provenance.json')
$records=[Collections.Generic.List[object]]::new()
foreach($cell in $grid.cells|Where-Object {$_.category -in @('Eye Brow','Eye / Pupil')}) {
    $slot=if($cell.category -eq 'Eye Brow'){'Eyebrow'}else{'Pupil'}
    foreach($candidate in $cell.candidates) {
        $id=($candidate.label -split '_')[-1]
        $relative="$($cell.body)/$slot/$id"
        $folder=Join-Path $root $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $folder) -Force|Out-Null
        $selection=$candidate.label+'@'+$candidate.sourceRef.pathId
        $log=@(& $exe --genshin-prefab $map $selection $folder 2>&1)
        $status=if($LASTEXITCODE -eq 0){'exported'}else{'failed'}
        $manifestPath=Join-Path $folder 'manifest.json'
        $meshes=$null;$bindings=$null;$unresolved=$null;$nativeFailures=$null;$nativeFiles=$null
        if($status -eq 'exported' -and (Test-Path -LiteralPath $manifestPath)){
            $manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
            $meshes=@($manifest.meshes).Count
            $bindings=@($manifest.materialBindings).Count
            $unresolved=@($manifest.unresolved).Count+@($manifest.materialBindings|Where-Object {$_.status -ne 'resolved'}).Count
            $nativeFailures=@($manifest.nativeTextureFailures).Count
            $nativeFiles=@(Get-ChildItem -LiteralPath $folder -Filter *.astexture).Count
            if($meshes -lt 1 -or $bindings -lt 1 -or $unresolved -ne 0 -or $nativeFailures -ne 0){$status='payload-unresolved'}
        }
        $records.Add([ordered]@{body=$cell.body;category=$cell.category;id=$candidate.id;
            label=$candidate.label;sourceRef=$candidate.sourceRef;status=$status;
            output=$relative.Replace('\','/');meshes=$meshes;bindings=$bindings;
            unresolved=$unresolved;nativeFailures=$nativeFailures;nativeFiles=$nativeFiles;
            log=($log|Out-String).Trim()})
        [ordered]@{schemaVersion=1;kind='manekin-selected-face-part-exports';
            expected=20;records=@($records)}|ConvertTo-Json -Depth 12|
            Set-Content -LiteralPath (Join-Path $root 'export-report.json')
        Write-Host "$($cell.body) $slot $id $status"
    }
}
