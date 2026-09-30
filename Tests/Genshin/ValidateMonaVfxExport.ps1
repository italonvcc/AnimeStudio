param([Parameter(Mandatory)][string]$ExportRoot)

$ErrorActionPreference = 'Stop'
function Assert($condition, $message) {
    if (-not $condition) { throw $message }
}

$effect = Get-ChildItem -LiteralPath (Join-Path $ExportRoot 'Effects') -Filter '*.effect.json' -File
Assert ($effect.Count -eq 1) 'Expected exactly one structured effect.'
$document = Get-Content -LiteralPath $effect[0].FullName -Raw | ConvertFrom-Json
Assert ($document.schemaVersion -eq 1) 'Unexpected effect schema.'
Assert ($document.objects.Count -eq 3) 'Expected root, Drops, and Splash.'
Assert (($document.objects | Where-Object { -not $_.active }).Count -eq 0) 'Source GameObject active flags changed.'

$expected = @{
    '00_Drops' = @{ raw='8AED12A0DF097E05DCCC62B357E6D6BA5A564F082C87CE675AD3805D139C1ACC'; max=12; min=11; life=0.85; streams=7 }
    '00_Splash' = @{ raw='4FAC1F9DF8757EDAEB665A0C5020A422CDB467C0140840BA7797528D93947003'; max=30; min=28; life=0.6; streams=5 }
}
foreach ($node in $document.objects | Where-Object { $_.name -in $expected.Keys }) {
    $reference = $expected[$node.name]
    $particle = @($node.components | Where-Object type -eq 'ParticleSystem')
    $renderer = @($node.components | Where-Object type -eq 'ParticleSystemRenderer')
    Assert ($particle.Count -eq 1 -and $renderer.Count -eq 1) "$($node.name): missing particle components."
    $particle = $particle[0]; $renderer = $renderer[0]
    Assert ($particle.raw.sha256 -eq $reference.raw) "$($node.name): source raw SHA changed."
    Assert ($particle.raw.bytes -eq 8116) "$($node.name): source particle length changed."
    $rawPath = Join-Path (Split-Path $effect[0].FullName) $particle.raw.assetPath
    Assert ((Get-FileHash -LiteralPath $rawPath -Algorithm SHA256).Hash -eq $reference.raw) "$($node.name): exported raw differs from source."
    Assert ($particle.coverage.status -eq 'Partial' -and $particle.coverage.verifiedNativeFieldsReady -and -not $particle.coverage.nativePlaybackReady) "$($node.name): incorrect coverage claim."
    $burst = $particle.modules.EmissionModule.m_Bursts.Array[0]
    Assert ($particle.modules.EmissionModule.enabled -and $particle.modules.EmissionModule.m_BurstCount -eq 1 -and $burst.countCurve.scalar -eq $reference.max -and $burst.countCurve.minScalar -eq $reference.min) "$($node.name): source burst changed."
    Assert ([Math]::Abs($particle.modules.InitialModule.startLifetime.minScalar - $reference.life) -lt 0.00001) "$($node.name): lifetime changed."
    Assert ($particle.modules.Genshin_Trail_extension.byteCount -eq 380 -and $particle.modules.CollisionModule.giUnknownCollisionWord -eq -1 -and @($particle.coverage.unresolvedRanges).Count -eq 2) "$($node.name): unresolved Collision/Trail source byte accounting changed."
    Assert ($particle.modules.ShapeModule.m_UseMeshScale -and -not $particle.modules.ShapeModule.alignToDirection -and $particle.modules.ColorOverDayModule.enabled -and -not $particle.modules.TextModule.enabled) "$($node.name): native-transfer-proven fields changed."
    Assert ($renderer.coverage.status -eq 'Partial' -and $renderer.coverage.verifiedRendererFieldsReady) "$($node.name): renderer coverage changed."
    Assert ($renderer.sourceUvOutline.Count -eq 8 -and $renderer.renderer.vertexStreamIds.Count -eq $reference.streams) "$($node.name): source polygon/streams changed."
    $base = $renderer.renderer.sourceRendererBase
    Assert ($base.castShadowsRaw -eq 0 -and -not $base.receiveShadows -and $base.lightProbeUsageRaw -eq 0 -and $base.reflectionProbeUsageRaw -eq 0 -and $base.sortingLayerId -eq 0 -and $base.sortingOrder -eq 0) "$($node.name): renderer base controls changed."
    Assert ($renderer.materials.Count -eq 2 -and $null -ne $renderer.materials[0] -and $null -eq $renderer.materials[1]) "$($node.name): material slots changed."
}

$materials = Get-ChildItem -LiteralPath (Join-Path $ExportRoot 'Material') -Filter '*.json' -File
Assert ($materials.Count -eq 2) 'Expected two source materials.'
$keywordStates = @{}
foreach ($materialPath in $materials) {
    $material = Get-Content -LiteralPath $materialPath.FullName -Raw | ConvertFrom-Json
    $keywordStates[$material.m_Name] = $material.ShaderKeywordState
    Assert ($material.ShaderKeywordState.Status -eq 'Serialized') "$($material.m_Name): keyword provenance missing."
    Assert ($material.SourceRaw.bytes -gt 0) "$($material.m_Name): material raw provenance missing."
    $rawPath = Join-Path $materialPath.DirectoryName $material.SourceRaw.assetPath
    Assert ((Get-FileHash -LiteralPath $rawPath -Algorithm SHA256).Hash -eq $material.SourceRaw.sha256) "$($material.m_Name): material raw SHA mismatch."
}
Assert ($keywordStates['Eff_Water_059_00'].Raw -ceq '' -and $keywordStates['Eff_Water_059_00'].Enabled.Count -eq 0) 'Drops source keywords must be explicitly empty.'
Assert ($keywordStates['Eff_Water_084_00'].Raw -ceq 'EFFECTED_BY_FOG' -and $keywordStates['Eff_Water_084_00'].Enabled.Count -eq 1 -and $keywordStates['Eff_Water_084_00'].Enabled[0] -ceq 'EFFECTED_BY_FOG') 'Splash source keywords changed.'

$textures = @($document.dependencies | Where-Object kind -eq 'Texture2D')
Assert ($textures.Count -eq 6) 'Expected six native texture dependencies.'
foreach ($texture in $textures) {
    $path = Join-Path (Split-Path $effect[0].FullName) $texture.assetPath
    $bytes = [IO.File]::ReadAllBytes($path)
    Assert ([Text.Encoding]::ASCII.GetString($bytes,0,8) -eq 'ASTEX001') "Invalid native texture magic: $path"
    $headerLength = [BitConverter]::ToInt32($bytes,8)
    Assert ($headerLength -gt 0 -and $headerLength -lt 65536) "Invalid native texture header length: $path"
    $header = [Text.Encoding]::UTF8.GetString($bytes,12,$headerLength) | ConvertFrom-Json
    Assert ($bytes.Length -eq 12+$headerLength+$header.byteCount) "Native texture byte accounting failed: $path"
}
Write-Output 'Mona InFloor source export regression passed.'
