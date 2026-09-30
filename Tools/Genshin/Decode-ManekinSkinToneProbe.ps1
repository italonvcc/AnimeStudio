param(
    [Parameter(Mandatory)][string]$ProbePath,
    [Parameter(Mandatory)][string]$OutputPath
)

$ErrorActionPreference='Stop'
if (Test-Path -LiteralPath $OutputPath) { throw "Choose a new output path: $OutputPath" }
$probe=Get-Content -LiteralPath $ProbePath -Raw|ConvertFrom-Json
$properties=[ordered]@{
    '_SkinTintColorOutlineLerp'=1
    '_SkinTintColor'=4
    '_SkinTintSecondColor'=4
    '_FirstShadowMultColor'=4
    '_CoolShadowMultColor'=4
}
function ReadProperty([byte[]]$bytes,[string]$name,[int]$count) {
    $pattern=[Text.Encoding]::ASCII.GetBytes($name)
    $matches=[System.Collections.Generic.List[int]]::new()
    for ($at=4;$at -le $bytes.Length-$pattern.Length;$at+=4) {
        if ([BitConverter]::ToUInt32($bytes,$at-4) -ne $pattern.Length) { continue }
        $same=$true
        for ($i=0;$i -lt $pattern.Length;$i++) {
            if ($bytes[$at+$i] -ne $pattern[$i]) { $same=$false; break }
        }
        if ($same) { $matches.Add($at) }
    }
    if ($matches.Count -ne 1) { throw "Expected one aligned $name field, got $($matches.Count)" }
    $valuesAt=($matches[0]+$pattern.Length+3)-band (-bnot 3)
    if ($valuesAt+4*$count -gt $bytes.Length) { throw "Truncated $name value" }
    @((0..($count-1))|ForEach-Object {
        $value=[BitConverter]::ToSingle($bytes,$valuesAt+4*$_)
        if (-not [float]::IsFinite($value)) { throw "Non-finite $name value" }
        $value
    })
}
$records=@($probe.results|Where-Object {$_.type -eq 'MonoBehaviour' -and $_.Name -match '^Beyd_Avatar_Girl_SkinTone_'}|ForEach-Object {
    if (-not $_.rawBase64 -or $_.script -ne 'BydCostumeRenderAsset') { throw "Missing exact raw BydCostumeRenderAsset: $($_.Name)" }
    $bytes=[Convert]::FromBase64String($_.rawBase64)
    $values=[ordered]@{}
    foreach ($name in $properties.Keys) { $values[$name]=ReadProperty $bytes $name $properties[$name] }
    [ordered]@{name=$_.Name;source=$_.Source;serializedFile=$_.serializedFile;
        pathId=$_.pathId;byteCount=$bytes.Length;
        rawSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes));
        values=$values}
})
$report=[ordered]@{schemaVersion=1;sourceProbe=[IO.Path]::GetFullPath($ProbePath);
    representation='Read only the five unique aligned named fields and their exact IEEE754 words; surrounding custom layout and application registry unresolved';
    count=$records.Count;records=$records}
$report|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $OutputPath
[pscustomobject]@{count=$records.Count;output=$OutputPath}|ConvertTo-Json
