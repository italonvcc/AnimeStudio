param(
    [Parameter(Mandatory)][string]$SkinValuesPath,
    [Parameter(Mandatory)][string]$ExportRoot,
    [Parameter(Mandatory)][string]$OutputPath
)

$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputPath){throw "Choose a new output path: $OutputPath"}
$values=Get-Content -LiteralPath $SkinValuesPath -Raw|ConvertFrom-Json
$export=Get-Content -LiteralPath (Join-Path $ExportRoot 'manifest.json') -Raw|ConvertFrom-Json
if(@($export.failures).Count -ne 0 -or @($export.unresolved).Count -ne 0 -or
    @($export.missingSelections).Count -ne 0){throw 'Skin raw export has missing/failing references.'}
$ids=@('P0151','P0154','P0156','P0161','P0279')
$captures=@{
    P0151='48306:E934 Girl FacePS10'
    P0154='49477:E778 Boy FacePS10'
    P0156='43096:E931 Girl FacePS10'
    P0161='48104:E926 Girl FacePS10'
    P0279='51482:E808 Boy FacePS10'
}
$rows=@(foreach($id in $ids){
    $record=@($values.records|Where-Object {$_.name -eq "Beyd_Avatar_Girl_SkinTone_${id}_01"})
    if($record.Count -ne 1){throw "Missing source values $id"}
    $r=$record[0]
    $path=Join-Path $ExportRoot ("MonoBehaviour/$($r.serializedFile)_$($r.pathId).bin")
    if(-not (Test-Path -LiteralPath $path)){throw "Missing raw skin preset $id"}
    $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if($hash -ne $r.rawSha256){throw "Raw skin preset hash mismatch $id"}
    [ordered]@{id=$id;label=$r.name;status='resolved-source-inputs';
        applicationStatus='unsupported';observedCapture=$captures[$id];
        sourceRef=[ordered]@{source=$r.source;serializedFile=$r.serializedFile;
            type='MonoBehaviour';pathId=$r.pathId;rawSha256=$r.rawSha256};
        parameters=$r.values;payload=[ordered]@{path=[IO.Path]::GetRelativePath($ExportRoot,$path).Replace('\','/');
            kind='raw-mono';sha256=$hash};
        reason='Exact five source values and raw MonoBehaviour payload; generated UGC skin targets and menu application registry unproved'}
})
$report=[ordered]@{schemaVersion=1;kind='manekin-five-skin-source-inputs';
    sourceValues=[IO.Path]::GetFullPath($SkinValuesPath);exportRoot=[IO.Path]::GetFullPath($ExportRoot);
    count=$rows.Count;records=$rows;acceptedSelections=0;
    limitations='All five color pairs match captured FacePS10 after sRGB-to-linear conversion, but generated skin texture composition and runtime preset selection are unknown.'}
$report|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $OutputPath
[pscustomobject]@{count=$rows.Count;output=$OutputPath}|ConvertTo-Json
