param(
    [Parameter(Mandatory)][string]$BatchRoot,
    [Parameter(Mandatory)][string]$DiagnosticCatalogRoot,
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)

$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputRoot) { throw "Choose a new output root: $OutputRoot" }
$batch = Get-Content -LiteralPath (Join-Path $BatchRoot 'batch-report.json') -Raw | ConvertFrom-Json -AsHashtable
$audit = Get-Content -LiteralPath (Join-Path $BatchRoot 'payload-audit.json') -Raw | ConvertFrom-Json -AsHashtable
$selection = Get-Content -LiteralPath (Join-Path $BatchRoot 'phase1-selection.json') -Raw | ConvertFrom-Json -AsHashtable
$build = Get-Content -LiteralPath (Join-Path $BatchRoot 'build-provenance.json') -Raw | ConvertFrom-Json -AsHashtable
$diagnostic = Get-Content -LiteralPath (Join-Path $DiagnosticCatalogRoot 'catalog.json') -Raw | ConvertFrom-Json -AsHashtable
if ($batch.records.Count -ne 64 -or $audit.payloadResolved -ne 64 -or $audit.auditFailed -ne 0) {
    throw 'Expected the frozen 64-part fully resolved batch'
}
if ($build.binaryFilesSha256['AnimeStudio.CLI.exe'] -ne $batch.exporterExeSha256) {
    throw 'Batch/build provenance mismatch'
}
$output = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $output | Out-Null
foreach ($sex in @('Boy','Girl')) {
    Copy-Item -LiteralPath (Join-Path $BatchRoot $sex) -Destination (Join-Path $output $sex) -Recurse
}
$baseSource = Join-Path $DiagnosticCatalogRoot 'Boy/Base/diagnostic-s0017'
$assemblySource = Join-Path $DiagnosticCatalogRoot 'Boy/Assemblies/diagnostic-s0017-hair-s0133'
$baseRelative = 'Boy/Base/diagnostic-s0017'
$assemblyRelative = 'Boy/Assemblies/diagnostic-s0017-hair-s0133'
New-Item -ItemType Directory -Path (Split-Path (Join-Path $output $baseRelative) -Parent) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path (Join-Path $output $assemblyRelative) -Parent) -Force | Out-Null
Copy-Item -LiteralPath $baseSource -Destination (Join-Path $output $baseRelative) -Recurse
Copy-Item -LiteralPath $assemblySource -Destination (Join-Path $output $assemblyRelative) -Recurse

function StableId($source) {
    $stamp = ($source.Source.Replace('\','/').ToLowerInvariant() + '|' + $source.SerializedFile.ToLowerInvariant() +
        '|' + $source.Type + '|' + $source.PathID)
    $hash = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($stamp))
    return 'gi-source-' + ([Convert]::ToHexString($hash).Substring(0,20).ToLowerInvariant())
}
function Payloads([string]$relative) {
    @((Get-ChildItem -LiteralPath (Join-Path $output $relative) -File -Recurse | ForEach-Object {
        $path=[System.IO.Path]::GetRelativePath($output, $_.FullName).Replace('\','/')
        [ordered]@{path=$path;kind=$_.Extension.TrimStart('.').ToLowerInvariant();
            sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    }))
}
function Bindings($manifest, [string]$relative) {
    @($manifest.materialBindings | ForEach-Object {
        $textures=[ordered]@{}
        foreach ($property in $_.textures.Keys) { $textures[$property]=($relative+'/'+$_.textures[$property]).Replace('\','/') }
        [ordered]@{rendererPath=$_.rendererPath;sourceRendererPath=$_.sourceRendererPath;
            submeshIndex=$_.submeshIndex;materialJsonPath=($relative+'/'+$_.materialJsonPath).Replace('\','/');
            sourceMaterial=$_.sourceMaterial;textures=$textures;status=$_.status;reason=$_.reason}
    })
}
function Rig($manifest) {
    $root=$manifest.root.Name
    [ordered]@{rootPath='.';sourceRootPath=$root;meshes=@($manifest.meshes|ForEach-Object {
        [ordered]@{rendererPath=$_.Path.Substring($root.Length+1);sourceRendererPath=$_.Path;
            bonePaths=@($_.bonePaths|ForEach-Object {$_.Substring($root.Length+1)});
            bindMatrices=$_.bindMatrices}
    })}
}
function Option($manifest, [string]$relative, [string]$body, [string]$slot, [string]$container) {
    $source=$manifest.root
    [ordered]@{id=(StableId $source);label=$source.Name;bodyCompatibility=@($body);
        category=$slot;slot=$slot;status='unsupported';
        reason='Exported geometry and source materials; original UGC shader implementation is not yet available';
        geometryStatus='exported';sourceRefs=@([ordered]@{source=$source.Source;
            serializedFile=$source.SerializedFile;container=$container;pathId=$source.PathID;
            type=$source.Type;name=$source.Name});
        modelPath=($relative+'/'+$manifest.model).Replace('\','/');
        manifestPath=($relative+'/manifest.json').Replace('\','/');
        payloads=(Payloads $relative);materialBindings=(Bindings $manifest $relative);
        rig=(Rig $manifest);dependencies=@()}
}
$options=[System.Collections.Generic.List[object]]::new()
$baseManifest=Get-Content -LiteralPath (Join-Path $output ($baseRelative+'/manifest.json')) -Raw|ConvertFrom-Json -AsHashtable
$baseSourceRef=$diagnostic.options[0].sourceRefs[0]
$baseOption=Option $baseManifest $baseRelative 'Boy' 'Body' $baseSourceRef.container
$baseOption.category='Base'
$options.Add($baseOption)
foreach ($record in $batch.records) {
    $relative=$record.output.Replace('\','/')
    $manifest=Get-Content -LiteralPath (Join-Path $output ($relative+'/manifest.json')) -Raw|ConvertFrom-Json -AsHashtable
    if ($manifest.root.Name -ne $record.source.name -or "$($manifest.root.PathID)" -ne "$($record.source.pathId)" -or
        $manifest.root.Source -ne $record.source.file) { throw "Source mismatch $relative" }
    $options.Add((Option $manifest $relative $record.body $record.slot "$($record.source.container)"))
}
$hair=@($options|Where-Object { $_.label -eq 'Beyd_Avatar_Boy_Hair_S0133' })
if ($hair.Count -ne 1) { throw 'Missing unique Boy Hair S0133' }
$assemblyManifest=Get-Content -LiteralPath (Join-Path $output ($assemblyRelative+'/manifest.json')) -Raw|ConvertFrom-Json -AsHashtable
$recipe=[ordered]@{id='diagnostic-boy-s0017-hair-s0133';body='Boy';baseId=$baseOption.id;
    optionIds=@($hair[0].id);outfitMode='multipart';requiredSlots=@('Hair');excludedSlots=@();
    status='unsupported';reason='Geometry verified; original UGC shader implementation and full appearance unresolved';
    geometryStatus='exported';assembledModelPath=$assemblyRelative+'/'+$assemblyManifest.model;
    assembledManifestPath=$assemblyRelative+'/manifest.json';
    visibilityRules=@(
        [ordered]@{targetId=$baseOption.id;rendererPath='Hair_S0017_Hair01';visible=$false;evidence='assembly-request.json'},
        [ordered]@{targetId=$baseOption.id;rendererPath='Hair_S0017_Hair02';visible=$false;evidence='assembly-request.json'},
        [ordered]@{targetId=$baseOption.id;rendererPath='Hair_S0017_HairBase01';visible=$false;evidence='assembly-request.json'});
    evidence=$assemblyRelative+'/assembly-request.json; source assembler checked rest transforms and bind matrices'}
$identity=[ordered]@{game='GI';gameVersion=$baseManifest.gameVersion;
    sourceMapSha256=$build.sourceMapSha256;exporterGitRevision=$build.sourceRevision;
    exporterDirtyFingerprint=$build.exporterDirtyFingerprint;
    binarySha256=$build.binaryFilesSha256['AnimeStudio.CLI.exe'];
    runId='manekin-phase1-64parts-20260929T2008';
    options=[ordered]@{seed=$selection.seed;sceneIndexSha256=$build.sceneIndexSha256;
        selectionAlgorithm=$selection.algorithm;binaryFilesSha256=$build.binaryFilesSha256;
        binaryProvenance=$build.provenanceNote;sourceFingerprintScope=$build.sourceFingerprintScope}}
$catalog=[ordered]@{schemaVersion=1;identity=$identity;options=@($options);
    assets=@([ordered]@{id='diagnostic-boy-s0017-hair-s0133-merged';status='exported';
        sourceRefs=$diagnostic.assets[0].sourceRefs;payloads=(Payloads $assemblyRelative)});
    recipes=@($recipe)}
$catalog|ConvertTo-Json -Depth 100|Set-Content -LiteralPath (Join-Path $output 'catalog.json')
Copy-Item -LiteralPath (Join-Path $BatchRoot 'phase1-selection.json') -Destination (Join-Path $output 'phase1-selection.json')
Copy-Item -LiteralPath (Join-Path $BatchRoot 'payload-audit.json') -Destination (Join-Path $output 'payload-audit.json')
Copy-Item -LiteralPath (Join-Path $BatchRoot 'build-provenance.json') -Destination (Join-Path $output 'build-provenance.json')
$summary=[ordered]@{schemaVersion=1;runId=$identity.runId;geometryPartOptions=64;
    malePartOptions=34;femalePartOptions=30;maleBaseOptions=1;
    certifiedGeometryRecipes=1;requestedAppearanceSelections=90;
    certifiedAppearanceSelections=0;requestedOutfitStates=20;certifiedFullOutfitStates=0;
    note='Same S-code garment roots are selection groups, not certified native outfit presets or visibility/rig mappings.'}
$summary|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $output 'export-report.json')
$leadCounts=[ordered]@{
    'Hair'=@(5,5); 'Eye Brow'=@(0,94); 'Eye / Pupil'=@(0,52);
    'Eye Makeup'=@(10,14); 'Lip Stick'=@(6,8);
    'Facial Makeup'=@(22,22); 'Skin Tone'=@(0,14);
    'Outfit sets'=@(5,5); 'Multi part outfits'=@(5,5)
}
$coverage=[System.Collections.Generic.List[object]]::new()
foreach ($category in $leadCounts.Keys) {
    for ($bodyIndex=0; $bodyIndex -lt 2; $bodyIndex++) {
        $body=@('Boy','Girl')[$bodyIndex]
        $coverage.Add([ordered]@{body=$body;category=$category;target=5;
            sourceLeadCount=$leadCounts[$category][$bodyIndex];
            exportedGeometryCount=if ($category -eq 'Hair') {5} else {0};
            acceptedAppearanceChoices=0;status='unsupported';
            reason=if ($category -eq 'Hair') {
                'Individual source roots exported; full-avatar appearance/compatibility not certified for all five'
            } elseif ($category -in @('Outfit sets','Multi part outfits')) {
                'Selected S groups and their constituent part roots exported; no complete source-valid preset/occlusion recipe certified'
            } else {
                'Name-matched source leads exist where counted; exact value/dependency and body/material application rules unproven'
            }})
    }
}
$coverageReport=[ordered]@{schemaVersion=1;targetCells=18;targetSelections=90;
    acceptedAppearanceSelections=0;targetOutfitStates=20;acceptedFullOutfitStates=0;
    sourceAccount='AnimeStudio/docs/Genshin/Manekin-Phase1-Source-Study.md';
    cells=@($coverage)}
$coverageReport|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $output 'phase1-coverage.json')
$lines=[System.Collections.Generic.List[string]]::new()
$lines.Add('# Manekin Phase 1 selection coverage')
$lines.Add('')
$lines.Add('Source account: AnimeStudio `docs/Genshin/Manekin-Phase1-Source-Study.md`. Leads are not verified selectable options.')
$lines.Add('')
$lines.Add('| Body | Category | Target | Source leads | Geometry exported | Accepted appearance choices |')
$lines.Add('| --- | --- | ---: | ---: | ---: | ---: |')
foreach ($cell in $coverage) {
    $lines.Add("| $($cell.body) | $($cell.category) | $($cell.target) | $($cell.sourceLeadCount) | $($cell.exportedGeometryCount) | $($cell.acceptedAppearanceChoices) |")
}
$lines.Add('')
$lines.Add('64 constituent part FBXs passed payload audit; one Boy hair recipe has catalog-backed geometry certification. A later Girl common-body + Hair S0084 diagnostic is outside this frozen catalog run. No complete outfit state or 1:1 rendered appearance is accepted.')
$lines|Set-Content -LiteralPath (Join-Path $output 'phase1-coverage.md')
Write-Host $output
