param(
    [Parameter(Mandatory)][string]$OutputRoot,
    [Parameter(Mandatory)][string]$BaseExport,
    [Parameter(Mandatory)][string]$HairExport,
    [Parameter(Mandatory)][string]$AssemblyExport,
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)

$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputRoot) { throw "Choose a new output root: $OutputRoot" }
$output = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $output | Out-Null
$sourceIndex = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'docs/Genshin/Maps/manekin-scene-index/scene-index.json') -Raw | ConvertFrom-Json
$locations = [ordered]@{
    Base = @{source=$BaseExport; relative='Boy/Base/diagnostic-s0017'}
    Hair = @{source=$HairExport; relative='Boy/Parts/Hair/diagnostic-s0133'}
    Assembly = @{source=$AssemblyExport; relative='Boy/Assemblies/diagnostic-s0017-hair-s0133'}
}
foreach ($key in $locations.Keys) {
    $record = $locations[$key]
    if (-not (Test-Path -LiteralPath (Join-Path $record.source 'manifest.json'))) { throw "Missing $key manifest" }
    $destination = Join-Path $output $record.relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $record.source -Destination $destination -Recurse
    $record.manifest = Get-Content -LiteralPath (Join-Path $destination 'manifest.json') -Raw | ConvertFrom-Json -AsHashtable
}
function RelativeFile([string]$absolute) {
    $part = [System.IO.Path]::GetRelativePath($output, $absolute).Replace('\','/')
    if ($part.StartsWith('../') -or [System.IO.Path]::IsPathRooted($part)) { throw "Payload escapes catalog root: $absolute" }
    return $part
}
function Payloads([string]$folder) {
    @(Get-ChildItem -LiteralPath (Join-Path $output $folder) -File -Recurse | ForEach-Object {
        $relative = RelativeFile $_.FullName
        [ordered]@{ path=$relative; kind=([System.IO.Path]::GetExtension($relative).TrimStart('.').ToLowerInvariant());
            sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}
function StableId($source) {
    $stamp = ($source.Source.Replace('\','/').ToLowerInvariant() + '|' + $source.SerializedFile.ToLowerInvariant() +
        '|' + $source.Type + '|' + $source.PathID)
    $hash = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($stamp))
    return 'gi-source-' + ([Convert]::ToHexString($hash).Substring(0,20).ToLowerInvariant())
}
function SourceRef($source) {
    $entry = @($sourceIndex.AssetEntries | Where-Object { $_.Type -eq $source.Type -and
        "$($_.PathID)" -eq "$($source.PathID)" -and $_.Source -eq $source.Source })
    if ($entry.Count -ne 1) { throw "Source container identity missing/ambiguous for $($source.Name)" }
    [ordered]@{ source=$source.Source; serializedFile=$source.SerializedFile; pathId=$source.PathID;
        container="$($entry[0].Container)"; type=$source.Type; name=$source.Name }
}
function Bindings($location) {
    @($location.manifest.materialBindings | ForEach-Object {
        $textures = [ordered]@{}
        foreach ($key in $_.textures.Keys) { $textures[$key] = ($location.relative + '/' + $_.textures[$key]).Replace('\','/') }
        [ordered]@{ rendererPath=$_.rendererPath; sourceRendererPath=$_.sourceRendererPath;
            submeshIndex=$_.submeshIndex; materialJsonPath=($location.relative + '/' + $_.materialJsonPath).Replace('\','/');
            sourceMaterial=$_.sourceMaterial; textures=$textures; status=$_.status; reason=$_.reason }
    })
}
function Rig($location) {
    [ordered]@{ rootPath='.'; sourceRootPath=$location.manifest.root.Name;
        meshes=@($location.manifest.meshes | ForEach-Object {
            [ordered]@{ rendererPath=($_.Path.Substring($location.manifest.root.Name.Length+1));
                sourceRendererPath=$_.Path;
                bonePaths=@($_.bonePaths | ForEach-Object { $_.Substring($location.manifest.root.Name.Length+1) });
                bindMatrices=$_.bindMatrices }
        }) }
}
$base=$locations.Base; $hair=$locations.Hair; $assembly=$locations.Assembly
$baseId=StableId $base.manifest.root; $hairId=StableId $hair.manifest.root
$baseModel=($base.relative + '/' + $base.manifest.model).Replace('\','/')
$hairModel=($hair.relative + '/' + $hair.manifest.model).Replace('\','/')
$assemblyModel=($assembly.relative + '/' + $assembly.manifest.model).Replace('\','/')
$recipeId='diagnostic-boy-s0017-hair-s0133'
$revision=(& git -C $RepositoryRoot rev-parse HEAD).Trim()
$sourceFiles=@('AnimeStudio/GenshinShaderNameReader.cs','AnimeStudio/Classes/Shader.cs','AnimeStudio/IImported.cs',
    'AnimeStudio.Utility/ModelConverter.cs','AnimeStudio.Utility/GenshinModelExporter.cs',
    'AnimeStudio.CLI/Program.cs','AnimeStudio.CLI/GenshinSourceProbeCommand.cs')
$fingerprintInput=($sourceFiles | Sort-Object | ForEach-Object {
    $_ + ':' + (Get-FileHash -LiteralPath (Join-Path $RepositoryRoot $_) -Algorithm SHA256).Hash
}) -join "`n"
$dirtyFingerprint=[Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData(
    [System.Text.Encoding]::UTF8.GetBytes($fingerprintInput)))
$distributionRoot=Join-Path $RepositoryRoot 'dist/net10.0-windows'
$binaryRoot=Join-Path $distributionRoot 'bin'
$binaryFiles=[ordered]@{}
foreach ($file in @('AnimeStudio.CLI.exe','AnimeStudio.CLI.dll','AnimeStudio.dll',
    'AnimeStudio.Utility.dll','AnimeStudio.FBXNative.dll','AnimeStudio.Ooz.dll')) {
    $path=if ($file -eq 'AnimeStudio.CLI.exe') { Join-Path $distributionRoot $file } else { Join-Path $binaryRoot $file }
    $binaryFiles[$file]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}
$catalog=[ordered]@{
    schemaVersion=1
    identity=[ordered]@{ game='GI'; gameVersion=$base.manifest.gameVersion;
        sourceMapSha256=(Get-FileHash -LiteralPath (Join-Path $RepositoryRoot 'docs/Genshin/Maps/genshin-7.1.map') -Algorithm SHA256).Hash;
        exporterGitRevision=$revision; exporterDirtyFingerprint=$dirtyFingerprint;
        binarySha256=$binaryFiles['AnimeStudio.CLI.exe'];
        runId='diagnostic-boy-s0017-hair-s0133-20260929';
        options=[ordered]@{ seed=20260929; scope='one source-validated geometry control';
            sceneIndexSha256=(Get-FileHash -LiteralPath (Join-Path $RepositoryRoot 'docs/Genshin/Maps/manekin-scene-index/scene-index.json') -Algorithm SHA256).Hash;
            dirtyFingerprintScope='listed exporter source files';
            binaryFilesSha256=$binaryFiles;
            binaryProvenance='CLI EXE is a .NET apphost; managed DLL and native hashes identify implementation' } }
    options=@(
        [ordered]@{ id=$baseId; label='Boy Suit S0017 base'; bodyCompatibility=@('Boy'); category='Base'; slot='Body';
            status='unsupported'; reason='Original UGC shader families have no implemented package shader yet'; geometryStatus='exported';
            sourceRefs=@((SourceRef $base.manifest.root)); modelPath=$baseModel;
            manifestPath=($base.relative+'/manifest.json'); payloads=(Payloads $base.relative);
            materialBindings=(Bindings $base); rig=(Rig $base); dependencies=@() },
        [ordered]@{ id=$hairId; label='Boy Hair S0133'; bodyCompatibility=@('Boy'); category='Hair'; slot='Hair';
            status='unsupported'; reason='Original UGC shader family has no implemented package shader yet'; geometryStatus='exported';
            sourceRefs=@((SourceRef $hair.manifest.root)); modelPath=$hairModel;
            manifestPath=($hair.relative+'/manifest.json'); payloads=(Payloads $hair.relative);
            materialBindings=(Bindings $hair); rig=(Rig $hair); dependencies=@() }
    )
    assets=@([ordered]@{ id='diagnostic-boy-s0017-hair-s0133-merged'; status='exported';
        sourceRefs=@((SourceRef $assembly.manifest.root)); payloads=(Payloads $assembly.relative) })
    recipes=@([ordered]@{ id=$recipeId; body='Boy'; baseId=$baseId; optionIds=@($hairId); outfitMode='multipart';
        requiredSlots=@('Hair'); excludedSlots=@(); status='unsupported';
        reason='Geometry verified; original UGC shader implementation and full appearance unresolved';
        geometryStatus='exported'; assembledModelPath=$assemblyModel;
        assembledManifestPath=($assembly.relative+'/manifest.json');
        visibilityRules=@(
            [ordered]@{ targetId=$baseId; rendererPath='Hair_S0017_Hair01'; visible=$false; evidence='assembly-request.json' },
            [ordered]@{ targetId=$baseId; rendererPath='Hair_S0017_Hair02'; visible=$false; evidence='assembly-request.json' },
            [ordered]@{ targetId=$baseId; rendererPath='Hair_S0017_HairBase01'; visible=$false; evidence='assembly-request.json' }
        ); evidence='Boy/Assemblies/diagnostic-s0017-hair-s0133/assembly-request.json; source assembler checked rest transforms and bind matrices' })
}
$catalog | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $output 'catalog.json')
$report=[ordered]@{ schemaVersion=1; runId=$catalog.identity.runId; optionCount=2;
    optionsGeometryExported=2; recipesGeometryExported=1; appearanceReady=0;
    unresolvedShaderLeaves=@('miHoYo/Character/Character_Ugc','miHoYo/Character/Character_Ugc_Skin','miHoYo/Character/Character_Ugc_Face') }
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'export-report.json')
Write-Host $output
