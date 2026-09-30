param(
    [Parameter(Mandatory)][string]$CandidateGridPath,
    [Parameter(Mandatory)][string]$RawProbePath,
    [Parameter(Mandatory)][string]$TextureMapPath,
    [Parameter(Mandatory)][string]$ExportRoot,
    [Parameter(Mandatory)][string]$OutputPath
)

$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputPath) { throw "Choose a new output path: $OutputPath" }
$grid=Get-Content -LiteralPath $CandidateGridPath -Raw|ConvertFrom-Json
$probe=Get-Content -LiteralPath $RawProbePath -Raw|ConvertFrom-Json
$map=Get-Content -LiteralPath $TextureMapPath -Raw|ConvertFrom-Json
$export=Get-Content -LiteralPath (Join-Path $ExportRoot 'manifest.json') -Raw|ConvertFrom-Json
if(@($export.failures).Count -ne 0 -or @($export.unresolved).Count -ne 0 -or
    @($export.missingSelections).Count -ne 0) { throw 'Exporter manifest reports unresolved native inputs.' }
function FindField([byte[]]$bytes,[string]$name) {
    $needle=[Text.Encoding]::ASCII.GetBytes($name)
    $found=[Collections.Generic.List[int]]::new()
    for($at=4;$at -le $bytes.Length-$needle.Length;$at+=4) {
        if([BitConverter]::ToUInt32($bytes,$at-4) -ne $needle.Length) { continue }
        $match=$true
        for($i=0;$i -lt $needle.Length;$i++) {if($bytes[$at+$i] -ne $needle[$i]){$match=$false;break}}
        if($match){$found.Add($at)}
    }
    if($found.Count -eq 0){return $null}
    if($found.Count -ne 1){throw "Ambiguous aligned $name"}
    $at=($found[0]+$needle.Length+3)-band (-bnot 3)
    [pscustomobject]@{offset=$at}
}
function ReadFloat([byte[]]$bytes,[string]$name,[bool]$required) {
    $field=FindField $bytes $name
    if($null -eq $field){if($required){throw "Missing $name"};return $null}
    if($field.offset+4 -gt $bytes.Length){throw "Truncated $name"}
    $v=[BitConverter]::ToSingle($bytes,$field.offset)
    if(-not [float]::IsFinite($v)){throw "Non-finite $name"}
    $v
}
function ReadColor([byte[]]$bytes,[string]$name) {
    $field=FindField $bytes $name
    if($null -eq $field){throw "Missing $name"}
    if($field.offset+16 -gt $bytes.Length){throw "Truncated $name"}
    @((0..3)|ForEach-Object {$v=[BitConverter]::ToSingle($bytes,$field.offset+4*$_);
        if(-not [float]::IsFinite($v)){throw "Non-finite $name"};$v})
}
function ReadPointer([byte[]]$bytes,[string]$name) {
    $field=FindField $bytes $name
    if($null -eq $field -or $field.offset+12 -gt $bytes.Length){throw "Missing/truncated $name"}
    [ordered]@{fileId=[BitConverter]::ToInt32($bytes,$field.offset);
        pathId=[BitConverter]::ToInt64($bytes,$field.offset+4).ToString()}
}
function NativeHeader([string]$path) {
    $stream=[IO.File]::OpenRead($path)
    try {
        $reader=[IO.BinaryReader]::new($stream)
        if([Text.Encoding]::ASCII.GetString($reader.ReadBytes(8)) -ne 'ASTEX001') { throw "Wrong native magic: $path" }
        $count=$reader.ReadInt32()
        if($count -lt 1 -or $count -gt 65536) { throw "Invalid native header length: $path" }
        $header=[Text.Encoding]::UTF8.GetString($reader.ReadBytes($count))|ConvertFrom-Json
        if($stream.Length -ne 12+$count+$header.byteCount) { throw "Native byte count mismatch: $path" }
        return $header
    }
    finally {$stream.Dispose()}
}
$rows=[Collections.Generic.List[object]]::new()
foreach($cell in $grid.cells|Where-Object {$_.category -in @('Eye Makeup','Lip Stick','Facial Makeup')}) {
    foreach($candidate in $cell.candidates) {
        $raw=@($probe.results|Where-Object {$_.Name -ceq $candidate.label -and
            $_.pathId -eq $candidate.sourceRef.pathId -and $_.Source -ceq $candidate.sourceRef.source})
        if($raw.Count -ne 1 -or $raw[0].script -ne 'BydCostumeRenderAsset') { throw "Missing exact costume asset $($candidate.label)" }
        $source=$raw[0];$bytes=[Convert]::FromBase64String($source.rawBase64)
        $configPath=Join-Path $ExportRoot ("MonoBehaviour/$($source.serializedFile)_$($source.pathId).bin")
        if(-not (Test-Path -LiteralPath $configPath) -or
            -not [Linq.Enumerable]::SequenceEqual([byte[]]$bytes,[byte[]][IO.File]::ReadAllBytes($configPath))) {
            throw "Exported raw costume bytes differ: $($candidate.label)"
        }
        $flag,$textureProperty=$null,$null
        $values=[ordered]@{}
        if($cell.category -eq 'Eye Makeup') {
            $flag='_UseEyeMakeup';$textureProperty='_EyeMakeupTex'
            $values[$flag]=ReadFloat $bytes $flag $true
            $blend=ReadFloat $bytes '_EyeMakeupLinearBlend' $false
            if($null -ne $blend){$values['_EyeMakeupLinearBlend']=$blend}
        }
        elseif($cell.category -eq 'Lip Stick') {
            $flag='_UseLipMakeup';$textureProperty='_LipMakeupTex'
            $values[$flag]=ReadFloat $bytes $flag $true
            $blend=ReadFloat $bytes '_LipMakeupLinearBlend' $false
            if($null -ne $blend){$values['_LipMakeupLinearBlend']=$blend}
        }
        else {
            $flag='_UseFaceDecal';$textureProperty='_FaceDecalTex'
            foreach($name in @($flag,'_FaceDecalPatternScaleX','_FaceDecalPatternScaleY',
                '_FaceDecalPatternRotation','_FaceDecalPatternCenterX','_FaceDecalPatternCenterY')) {
                $values[$name]=ReadFloat $bytes $name $true
            }
            $values['_FaceDecalColor']=ReadColor $bytes '_FaceDecalColor'
            $values['_FaceDecalColorRamp']=ReadColor $bytes '_FaceDecalColorRamp'
            $values['_FaceDecalAlphaRamp']=ReadColor $bytes '_FaceDecalAlphaRamp'
        }
        $pointer=ReadPointer $bytes $textureProperty
        if($pointer.fileId -ne 1) { throw "Unsupported texture file ID: $($candidate.label)" }
        $textures=@($map.entries|Where-Object {$_.Type -eq 'Texture2D' -and $_.PathID -eq $pointer.pathId})
        if($textures.Count -ne 1) { throw "Missing/ambiguous source texture path ID: $($pointer.pathId)" }
        $texture=$textures[0]
        $nativeFiles=@(Get-ChildItem -LiteralPath (Join-Path $ExportRoot 'Texture2D') -Filter "*_$($pointer.pathId).astexture")
        if($nativeFiles.Count -ne 1) { throw "Missing/ambiguous native texture: $($pointer.pathId)" }
        $native=$nativeFiles[0];$header=NativeHeader $native.FullName
        if($header.sourceId -ne $pointer.pathId -or $header.name -cne $texture.Name -or
            $header.colorSpace -notin @(0,1) -or $header.mipCount -lt 1 -or
            $header.filterMode -notin @(0,1,2) -or $header.wrapU -notin @(0,1,2,3) -or
            $header.wrapU -ne $header.wrapV -or $header.wrapU -ne $header.wrapW) {
            throw "Unverified native header/import mode: $($candidate.label)"
        }
        $filters=@('Point','Bilinear','Trilinear')
        $wraps=@('Repeat','Clamp','Mirror','MirrorOnce')
        $configRel=[IO.Path]::GetRelativePath($ExportRoot,$configPath).Replace('\','/')
        $nativeRel=[IO.Path]::GetRelativePath($ExportRoot,$native.FullName).Replace('\','/')
        $rows.Add([ordered]@{id=$candidate.id;body=$cell.body;category=$cell.category;
            label=$candidate.label;status='resolved-source-inputs';
            applicationStatus='unsupported';
            reason='Exact source property words and Texture2D PPtr exported; face application/layering and UGC runtime material result unproved';
            sourceRef=[ordered]@{source=$source.Source;serializedFile=$source.serializedFile;
                type='MonoBehaviour';pathId=$source.pathId;container=$candidate.sourceRef.container};
            parameters=$values;
            textureBindings=[ordered]@{$textureProperty=[ordered]@{fileId=$pointer.fileId;
                pathId=$pointer.pathId;name=$texture.Name;source=$texture.Source;
                container=$texture.Container;nativePath=$nativeRel;
                textureImport=[ordered]@{sRGB=($header.colorSpace -eq 1);mipmaps=($header.mipCount -gt 1);
                    filter=$filters[$header.filterMode];wrap=$wraps[$header.wrapU];
                    wrapU=$wraps[$header.wrapU];wrapV=$wraps[$header.wrapV];wrapW=$wraps[$header.wrapW];
                    aniso=$header.anisoLevel;sourceColorSpace=$header.colorSpace;mipCount=$header.mipCount;
                    evidence="Source Texture2D $($header.sourceFile)@$($header.sourceId) ASTEX001 header"}}};
            payloads=@([ordered]@{path=$configRel;kind='raw-mono';sha256=(Get-FileHash $configPath -Algorithm SHA256).Hash},
                [ordered]@{path=$nativeRel;kind='astexture';sha256=(Get-FileHash $native.FullName -Algorithm SHA256).Hash})})
    }
}
if($rows.Count -ne 30) { throw "Expected 30 costume inputs, got $($rows.Count)" }
$report=[ordered]@{schemaVersion=1;kind='manekin-selected-makeup-source-inputs';
    sourceGrid=[IO.Path]::GetFullPath($CandidateGridPath);exportRoot=[IO.Path]::GetFullPath($ExportRoot);
    count=$rows.Count;records=@($rows);acceptedSelections=0;
    limitations='Named property values and exact native texture payloads are available; no menu application registry, generated face-composition result or shader fidelity claim.'}
$report|ConvertTo-Json -Depth 18|Set-Content -LiteralPath $OutputPath
[pscustomobject]@{count=$rows.Count;nativeDistinct=@($rows|ForEach-Object {$_.textureBindings.PSObject.Properties.Value.nativePath}|Sort-Object -Unique).Count;
    output=$OutputPath}|ConvertTo-Json
