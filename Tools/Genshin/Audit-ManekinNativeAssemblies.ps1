param(
    [Parameter(Mandatory)][string]$RunRoot,
    [string]$AuditName = 'native-payload-audit.json',
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)

$ErrorActionPreference='Stop'
$reportPath=Join-Path $RunRoot 'assembly-probe-report.json'
$report=Get-Content -LiteralPath $reportPath -Raw|ConvertFrom-Json
if(@($report.records).Count -ne 10) { throw 'Expected ten selected group assemblies.' }
$auditPath=Join-Path $RunRoot $AuditName
if(Test-Path -LiteralPath $auditPath) { throw "Choose a fresh run; audit exists: $auditPath" }
$rows=[Collections.Generic.List[object]]::new()
$allPayloads=[Collections.Generic.List[object]]::new()
foreach($record in $report.records) {
    if($record.status -ne 'assembled') { throw "Assembly failed: $($record.body) S$($record.setId)" }
    $folder=Join-Path $RunRoot $record.output
    $manifestPath=Join-Path $folder 'manifest.json'
    $manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
    $bindings=@($manifest.materialBindings)
    if(@($manifest.unresolved).Count -ne 0 -or @($manifest.nativeTextureFailures).Count -ne 0 -or
        @($bindings|Where-Object {$_.status -ne 'resolved'}).Count -ne 0) {
        throw "Unresolved payload: $($record.body) S$($record.setId)"
    }
    $references=0
    foreach($binding in $bindings) {
        $native=@($binding.nativeTextures.PSObject.Properties)
        $textureImport=@($binding.textureImport.PSObject.Properties)
        $textures=@($binding.textures.PSObject.Properties)
        if($native.Count -ne $textures.Count -or $textureImport.Count -ne $native.Count) {
            throw "Native/property count mismatch: $($binding.rendererPath)"
        }
        foreach($property in $textures) {
            $path=$binding.nativeTextures.($property.Name)
            $import=$binding.textureImport.($property.Name)
            if(-not $path -or -not $import -or -not $import.evidence -or
                $import.mipCount -lt 1 -or $import.sourceColorSpace -notin @(0,1)) {
                throw "Native provenance missing: $($binding.rendererPath) $($property.Name)"
            }
            $full=[IO.Path]::GetFullPath((Join-Path $folder $path))
            if(-not $full.StartsWith([IO.Path]::GetFullPath($folder)+[IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $full)) {
                throw "Native payload outside run/missing: $path"
            }
            $references++
        }
    }
    $files=@(Get-ChildItem -LiteralPath $folder -Recurse -File)
    $nativeFiles=@($files|Where-Object {$_.Extension -eq '.astexture'})
    foreach($file in $files) {
        $allPayloads.Add([ordered]@{path=[IO.Path]::GetRelativePath($RunRoot,$file.FullName).Replace('\','/');
            kind=$file.Extension.TrimStart('.').ToLowerInvariant();sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash})
    }
    $rows.Add([ordered]@{body=$record.body;setId=$record.setId;meshes=@($manifest.meshes).Count;
        materialBindings=$bindings.Count;nativeReferences=$references;nativeFiles=$nativeFiles.Count;
        unresolved=0;nativeTextureFailures=0})
}
$distributionDir=Join-Path $RepositoryRoot 'dist/net10.0-windows'
$binaryDir=Join-Path $distributionDir 'bin'
$binaryNames=@('AnimeStudio.CLI.exe','AnimeStudio.CLI.dll','AnimeStudio.dll',
    'AnimeStudio.Utility.dll','AnimeStudio.FBXNative.dll','AnimeStudio.FBXWrapper.dll',
    'Texture2DDecoderNative.dll','Texture2DDecoderWrapper.dll')
$binary=[ordered]@{}
foreach($name in $binaryNames) {
    $path=if($name -eq 'AnimeStudio.CLI.exe') { Join-Path $distributionDir $name } else { Join-Path $binaryDir $name }
    if(Test-Path -LiteralPath $path) { $binary[$name]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
}
$sourceNames=@('AnimeStudio/IImported.cs','AnimeStudio.Utility/ModelConverter.cs',
    'AnimeStudio.Utility/GenshinModelExporter.cs','AnimeStudio.Utility/GenshinPartAssembler.cs',
    'AnimeStudio.Utility/GenshinBindPose.cs','AnimeStudio.Utility/GenshinVfxTexturePayload.cs',
    'AnimeStudio.FBXWrapper/FbxExporterContext.cs','AnimeStudio.FBXNative/api.cpp',
    'AnimeStudio.Libraries/AnimeStudio.FBXNative.dll',
    'AnimeStudio.CLI/GenshinModelCommand.cs',
    'Tools/Genshin/Probe-ManekinSelectedSets.ps1')
$sourceHashes=[ordered]@{}
foreach($name in $sourceNames) {
    $sourceHashes[$name]=(Get-FileHash -LiteralPath (Join-Path $RepositoryRoot $name) -Algorithm SHA256).Hash
}
$commit=(& git -C $RepositoryRoot rev-parse HEAD).Trim()
$changed=@(& git -C $RepositoryRoot status --short --untracked-files=all 2>$null)
$dirty=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
    [Text.Encoding]::UTF8.GetBytes(($changed -join "`n"))))
$audit=[ordered]@{schemaVersion=1;runRoot=[IO.Path]::GetFullPath($RunRoot);
    sourceReport='assembly-probe-report.json';gitRevision=$commit;
    dirtyPathListSha256=$dirty;sourceFilesSha256=$sourceHashes;binaryFilesSha256=$binary;
    records=@($rows);payloads=@($allPayloads);
    limitations='Binary hashes identify the frozen executable used for export. Listed exporter source hashes were captured at audit time; unrelated CLI edits may exist. dirtyPathListSha256 is a path-list fingerprint, not a content patch hash. Native payload header and Unity import are separately checked by importer/Editor owner.'}
$audit|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $auditPath
$audited=Get-Content -LiteralPath $auditPath -Raw|ConvertFrom-Json
[pscustomobject]@{assemblies=$rows.Count;meshes=(@($audited.records)|Measure-Object -Property meshes -Sum).Sum;
    nativeReferences=(@($audited.records)|Measure-Object -Property nativeReferences -Sum).Sum;
    nativeFiles=(@($audited.records)|Measure-Object -Property nativeFiles -Sum).Sum;
    payloads=$allPayloads.Count;output=$auditPath}|ConvertTo-Json
