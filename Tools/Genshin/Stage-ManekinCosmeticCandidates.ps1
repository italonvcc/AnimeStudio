param(
    [Parameter(Mandatory)][string]$SkinValuesPath,
    [Parameter(Mandatory)][string]$MakeupProbePath,
    [Parameter(Mandatory)][string]$LipstickProbePath,
    [Parameter(Mandatory)][string]$LipstickMapQueryPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$PartsInventoryPath = (Join-Path $PSScriptRoot '../../docs/Genshin/Export/manekin-parts.json')
)

$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputPath) { throw "Choose a new output path: $OutputPath" }
$skin = Get-Content -LiteralPath $SkinValuesPath -Raw | ConvertFrom-Json
$makeup = Get-Content -LiteralPath $MakeupProbePath -Raw | ConvertFrom-Json
$lipstick = Get-Content -LiteralPath $LipstickProbePath -Raw | ConvertFrom-Json
$lipstickMap = Get-Content -LiteralPath $LipstickMapQueryPath -Raw | ConvertFrom-Json
$inventory = Get-Content -LiteralPath $PartsInventoryPath -Raw | ConvertFrom-Json
$allEntries = @($inventory.entries) + @($lipstickMap.entries)
$chosenSkinIds = @('P0151','P0154','P0156','P0161','P0279')
$captureBySkin = @{
    P0151 = @('48306:E934:FacePS10:CB2:SkinTintColor@128:SkinTintSecondColor@144')
    P0154 = @('49477:E778:FacePS10:CB2:SkinTintColor@128:SkinTintSecondColor@144; Boy face draw')
    P0156 = @('43096:E931:FacePS10:CB2:SkinTintColor@128:SkinTintSecondColor@144')
    P0161 = @('48104:E926:FacePS10:CB2:SkinTintColor@128:SkinTintSecondColor@144')
    P0279 = @('51482:E808:FacePS10:CB2:SkinTintColor@128:SkinTintSecondColor@144; Boy face draw')
}
function SourceRef($record) {
    $match = @($allEntries | Where-Object {
        $_.Name -ceq $record.name -and $_.Type -eq 'MonoBehaviour' -and
        $_.PathID -eq $record.pathId -and $_.Source -ceq $record.source
    })
    if ($match.Count -ne 1) { throw "Expected one exact inventory record: $($record.name)" }
    [ordered]@{name=$record.name;type='MonoBehaviour';source=$record.source;
        serializedFile=$record.serializedFile;pathId=$record.pathId;
        container=$match[0].Container;offset=$match[0].Offset;
        rawSha256=$record.rawSha256}
}
function StableId($record) {
    $text = $record.source.Replace('\','/').ToLowerInvariant() + '|' +
        $record.serializedFile.ToLowerInvariant() + '|MonoBehaviour|' + $record.pathId
    $hash = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text))
    'gi-source-' + [Convert]::ToHexString($hash).Substring(0,20).ToLowerInvariant()
}
function ReadNamedValue([byte[]]$bytes,[string]$name,[string]$kind) {
    $needle=[Text.Encoding]::ASCII.GetBytes($name)
    $matches=[Collections.Generic.List[int]]::new()
    for($i=4;$i -le $bytes.Length-$needle.Length;$i+=4) {
        if([BitConverter]::ToUInt32($bytes,$i-4) -ne $needle.Length) { continue }
        $same=$true
        for($j=0;$j -lt $needle.Length;$j++) {
            if($bytes[$i+$j] -ne $needle[$j]) { $same=$false; break }
        }
        if($same) { $matches.Add($i) }
    }
    if($matches.Count -ne 1) { throw "Expected one aligned field $name, got $($matches.Count)" }
    $at=($matches[0]+$needle.Length+3)-band (-bnot 3)
    if($kind -eq 'float') {
        if($at+4 -gt $bytes.Length) { throw "Truncated $name" }
        $value=[BitConverter]::ToSingle($bytes,$at)
        if(-not [float]::IsFinite($value)) { throw "Non-finite $name" }
        return $value
    }
    if($kind -eq 'texture') {
        if($at+12 -gt $bytes.Length) { throw "Truncated $name" }
        return [ordered]@{fileId=[BitConverter]::ToInt32($bytes,$at);
            pathId=[BitConverter]::ToInt64($bytes,$at+4).ToString()}
    }
    throw "Unknown property kind: $kind"
}
$options=[Collections.Generic.List[object]]::new()
foreach($id in $chosenSkinIds) {
    $records=@($skin.records|Where-Object {$_.name -ceq "Beyd_Avatar_Girl_SkinTone_${id}_01"})
    if($records.Count -ne 1) { throw "Expected one skin record $id" }
    $record=$records[0]
    $capture=@()
    if($captureBySkin.ContainsKey($id)) { $capture=@($captureBySkin[$id]) }
    $options.Add([ordered]@{id=(StableId $record);label=$record.name;
        category='SkinTone';slot='SkinTone';bodyCompatibility=@('Girl');
        status='unsupported';reason='Exact source parameters decoded; preset application registry, generated skin targets and runtime UGC shader binding remain unproved';
        sourceRefs=@((SourceRef $record));parameters=$record.values;
        captureMatches=$capture;
        evidence='Aligned named IEEE754 fields in source MonoBehaviour raw bytes; capture CB values match decoded source sRGB colors after standard sRGB-to-linear conversion for listed captures';
        dependencies=@()})
}
$makeupKinds=@(
    @{name='Beyd_Avatar_Girl_EyeShadow_P0045_01';category='EyeMakeup';flag='_UseEyeMakeup';texture='_EyeMakeupTex'},
    @{name='Beyd_Avatar_Girl_FacialMakeup_P0074_01';category='FacialMakeup';flag='_UseFaceDecal';texture='_FaceDecalTex'},
    @{name='Beyd_Avatar_Girl_Lipstick_P0475_01';category='LipStick';flag='_UseLipMakeup';texture='_LipMakeupTex'},
    @{name='Beyd_Avatar_Boy_Lipstick_P0475_01';category='LipStick';flag='_UseLipMakeup';texture='_LipMakeupTex'}
)
foreach($kind in $makeupKinds) {
    $raw=@(@($makeup.results) + @($lipstick.results)|Where-Object {$_.Name -ceq $kind.name})
    if($raw.Count -ne 1 -or $raw[0].script -ne 'BydCostumeRenderAsset' -or -not $raw[0].rawBase64) {
        throw "Missing raw cosmetic source record: $($kind.name)"
    }
    $x=$raw[0];$bytes=[Convert]::FromBase64String($x.rawBase64)
    $record=[ordered]@{name=$x.Name;source=$x.Source;serializedFile=$x.serializedFile;
        pathId=$x.pathId;rawSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))}
    $pointer=ReadNamedValue $bytes $kind.texture 'texture'
    $matches=@($allEntries|Where-Object {$_.Type -eq 'Texture2D' -and $_.PathID -eq $pointer.pathId -and
        $_.Name -ceq ($kind.name+'_Tex_Diffuse')})
    if($pointer.fileId -ne 1 -or $matches.Count -ne 1) { throw "Unresolved source texture reference for $($kind.name)" }
    $options.Add([ordered]@{id=(StableId $record);label=$record.name;
        category=$kind.category;slot=$kind.category;bodyCompatibility=@(if($kind.name -match '_Boy_'){'Boy'}else{'Girl'});
        status='unsupported';reason='Exact enabled flag and source texture PPtr resolved; face application registry and runtime UGC material binding remain unproved';
        sourceRefs=@((SourceRef $record));
        parameters=[ordered]@{$kind.flag=(ReadNamedValue $bytes $kind.flag 'float')};
        textureReferences=[ordered]@{$kind.texture=[ordered]@{fileId=$pointer.fileId;
            pathId=$pointer.pathId;name=$matches[0].Name;source=$matches[0].Source;
            container=$matches[0].Container;offset=$matches[0].Offset}};
        captureMatches=@(if($kind.name -eq 'Beyd_Avatar_Girl_Lipstick_P0475_01'){
            '48104:E926 and 48306:E934:FacePS10:t3 bound resource name matches exact source Texture2D'});
        evidence='Aligned named source flag and serialized texture PPtr; exact Texture2D path ID in source map';
        dependencies=@()})
}
$report=[ordered]@{schemaVersion=1;kind='manekin-cosmetic-candidates';
    status='source candidates only; no accepted appearance selections or production material bindings';
    sourceProbe=[IO.Path]::GetFullPath($MakeupProbePath);
    lipstickProbe=[IO.Path]::GetFullPath($LipstickProbePath);
    lipstickMapQuery=[IO.Path]::GetFullPath($LipstickMapQueryPath);
    skinValues=[IO.Path]::GetFullPath($SkinValuesPath);
    selectedFiveSkinIds=$chosenSkinIds;options=@($options)}
$report|ConvertTo-Json -Depth 16|Set-Content -LiteralPath $OutputPath
[pscustomobject]@{options=$options.Count;skin=5;makeup=4;output=$OutputPath}|ConvertTo-Json
