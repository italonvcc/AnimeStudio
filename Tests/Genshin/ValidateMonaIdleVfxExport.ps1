param([Parameter(Mandatory)][string]$ExportRoot)

$ErrorActionPreference = 'Stop'
function Assert($condition, $message) { if (-not $condition) { throw $message } }

$manifest = Get-Content -LiteralPath (Join-Path $ExportRoot 'manifest.json') -Raw | ConvertFrom-Json
$entry = $manifest.actions[0].effects[0]
Assert ($entry.identity.Name -eq 'Eff_Avatar_Girl_Catalyst_Mona_LiquidStrike_Idle') 'Wrong source prefab.'
Assert (@($entry.unresolved).Count -eq 0) 'Source dependency graph is unresolved.'
$effectPath = Join-Path $ExportRoot $entry.effectFile
$effect = Get-Content -LiteralPath $effectPath -Raw | ConvertFrom-Json
Assert ($effect.objects.Count -eq 6) 'Expected root and five Idle children.'

$expected = @{
    '00_Bubble' = @{ id='1417524466111809513'; sha='1AAD7EAAB949079A514C717AE49197594A51AA217574EA4214EA509014713211'; bytes=7868; mode='Mesh'; rate=10 }
    '00_Drops' = @{ id='4888742058643213888'; sha='4B0DFBBFC12641B1624B21CD6FBBCFAEB4742EFFB1F16484BA7C5F2E9BC0E629'; bytes=8060; mode='Billboard'; rate=9 }
    '00_LittleSpark' = @{ id='-1526399622845684252'; sha='52ACCC69FD27AEA00B9F46A3E1A6F25345B6F5F45F54178E5B72C1C2E1ED233D'; bytes=7868; mode='Billboard'; rate=10 }
    '00_Splash' = @{ id='-3483459620427163085'; sha='D17F01ABFB1A287B39CCA7143717E58923129E2241B2D57AE8204BA79A4F13C3'; bytes=8060; mode='Billboard'; rate=20 }
    '01_SplashCenter' = @{ id='1012374391672548122'; sha='D57F655C7A1B232D638B07F7BC78E62C6C818650A6E37E430C8A9EBB3CE543F2'; bytes=8116; mode='Billboard'; rate=12 }
}
foreach ($node in $effect.objects | Where-Object { $_.Name -in $expected.Keys }) {
    $wanted = $expected[$node.Name]
    $particle = @($node.components | Where-Object type -eq 'ParticleSystem')
    $renderer = @($node.components | Where-Object type -eq 'ParticleSystemRenderer')
    Assert ($particle.Count -eq 1 -and $renderer.Count -eq 1) "$($node.Name): missing source particle/renderer pair."
    $particle = $particle[0]; $renderer = $renderer[0]
    Assert ($particle.id.pathID -eq $wanted.id -and $particle.raw.bytes -eq $wanted.bytes -and $particle.raw.sha256 -eq $wanted.sha) "$($node.Name): source identity or raw payload changed."
    $rawPath = Join-Path (Split-Path $effectPath) $particle.raw.assetPath
    Assert ((Get-FileHash -LiteralPath $rawPath -Algorithm SHA256).Hash -eq $wanted.sha) "$($node.Name): particle raw provenance mismatch."
    Assert ($particle.modules.looping -and $particle.modules.EmissionModule.enabled -and $particle.modules.ShapeModule.enabled) "$($node.Name): active simulation modules changed."
    Assert ([Math]::Abs($particle.modules.EmissionModule.rateOverTime.scalar - $wanted.rate) -lt 0.00001) "$($node.Name): source emission rate changed."
    Assert ($particle.coverage.status -eq 'Partial' -and -not $particle.coverage.nativePlaybackReady) "$($node.Name): unsupported fields were silently declared ready."
    $activeUnknown = @($particle.coverage.unresolvedRanges | Where-Object { $_.moduleEnabled -eq $true })
    Assert ($activeUnknown.Count -eq 0 -and @($particle.coverage.unresolvedRanges).Count -eq 2) "$($node.Name): unresolved byte ranges must be limited to disabled Collision/Trail additions."
    Assert ($particle.modules.ShapeModule.m_UseMeshScale -and -not $particle.modules.ShapeModule.alignToDirection) "$($node.Name): packed Shape flags were confused."
    Assert ($particle.modules.ShapeModule.m_AABBSlice.x -eq 1 -and $particle.modules.ShapeModule.m_MeshSpawn.mode -eq 0) "$($node.Name): native Shape additions changed."
    Assert ($particle.modules.ColorOverDayModule.enabled -and $particle.modules.ColorOverDayModule.gradient.minMaxState -eq 1 -and -not $particle.modules.TextModule.enabled) "$($node.Name): native tail module activation changed."
    Assert ($particle.modules.EmissionModule.m_EnableFallOff -and $particle.modules.EmissionModule.m_EmissionFalloffStart -eq 50 -and $particle.modules.EmissionModule.m_EmissionFalloffEnd -eq 150) "$($node.Name): native distance falloff changed."
    Assert ($particle.modules.useOptPrewarm -and $particle.modules.useCullingUpdate -and $particle.modules.cullingUpdateDistance -eq 20) "$($node.Name): native header controls changed."
    Assert ($renderer.renderer.renderMode -eq $wanted.mode -and $renderer.renderer.sourceUvOutline.Count -eq 8) "$($node.Name): renderer mode or source outline changed."
    Assert ($renderer.coverage.status -eq 'Partial' -and -not $renderer.coverage.nativePlaybackReady) "$($node.Name): renderer unsupported data was silently declared ready."
    Assert ($renderer.renderer.useCustomVertexStreams -eq ($node.Name -ne '00_Bubble') -and $renderer.renderer.maskInteractionRaw -eq 0) "$($node.Name): verified 2017 renderer fields changed."
    $rendererRaw = [IO.File]::ReadAllBytes((Join-Path (Split-Path $effectPath) $renderer.raw.assetPath))
    Assert ($rendererRaw[213] -eq 0 -and $rendererRaw[214] -eq 0 -and $rendererRaw[215] -eq 0) "$($node.Name): renderer bytes 213..215 are no longer verified zero padding."
    $expectedGpu = $node.Name -in @('00_Bubble', '00_LittleSpark')
    Assert ($renderer.renderer.enableGPUInstancing -eq $expectedGpu -and -not $renderer.renderer.enableGPUInstancingV2 -and $renderer.renderer.useOctagonShape) "$($node.Name): native renderer feature flags changed."
    Assert ($renderer.renderer.rotateWithParent -and $renderer.renderer.parentScale.x -eq 1 -and $renderer.renderer.parentScale.y -eq 1 -and $renderer.renderer.parentScale.z -eq 1) "$($node.Name): renderer parent scale controls changed."
    Assert ($renderer.renderer.parentRotation.x -eq 0 -and $renderer.renderer.parentRotation.y -eq 0 -and $renderer.renderer.parentRotation.z -eq 0 -and $renderer.renderer.parentRotation.w -eq 1) "$($node.Name): renderer parent rotation changed."
    Assert ($renderer.renderer.flip.x -eq 0 -and $renderer.renderer.flip.y -eq 0 -and $renderer.renderer.flip.z -eq 0) "$($node.Name): renderer flip changed."
    Assert (@($renderer.coverage.unresolvedRanges).Count -eq 0 -and @($renderer.renderer.unknownBlocks).Count -eq 0) "$($node.Name): renderer unknown range accounting changed."
}

$spark = ($effect.objects | Where-Object Name -eq '00_LittleSpark').components | Where-Object type -eq ParticleSystem
$custom = $spark.modules.CustomDataModule
Assert ($spark.modules.ClampVelocityModule.enabled -and $custom.enabled -and $custom.mode0 -eq 2 -and $custom.mode1 -eq 2) 'LittleSpark active modules changed.'
Assert ($custom.vectorComponentCount0 -eq 4 -and $custom.vectorComponentCount1 -eq 4) 'LittleSpark custom streams changed.'
Assert ([Math]::Abs($custom.color1.maxColor.b - 2.0) -lt 0.00001) 'LittleSpark source HDR custom color changed.'

$bubbleRenderer = ($effect.objects | Where-Object Name -eq '00_Bubble').components | Where-Object type -eq ParticleSystemRenderer
Assert ($bubbleRenderer.renderer.meshPointers[0].fileId -eq 2 -and $bubbleRenderer.renderer.meshPointers[0].pathId -eq '1097857709675081423') 'Bubble source mesh pointer changed.'
Assert ($bubbleRenderer.meshes[0].id.pathID -eq '1097857709675081423') 'Bubble mesh dependency was not resolved.'
$mesh = Get-Content -LiteralPath (Join-Path (Split-Path $effectPath) $bubbleRenderer.meshes[0].assetPath) -Raw | ConvertFrom-Json
Assert ($mesh.identity.Name -eq 'Eff_Model_GeoSphere_Noise_00b' -and $mesh.details.vertexCount -eq 181) 'Wrong Bubble mesh.'
Assert ($mesh.details.vertices.Count -eq 543 -and $mesh.details.normals.Count -eq 543 -and $mesh.details.tangents.Count -eq 724 -and $mesh.details.uv0.Count -eq 362) 'Native Bubble vertex channel export changed.'
Assert ($mesh.details.indices.Count -eq 960 -and $mesh.details.subMeshes.Count -eq 1 -and $mesh.details.subMeshes[0].topology -eq 'Triangles') 'Native Bubble topology changed.'
Assert ($null -eq $mesh.details.colors -and $null -eq $mesh.details.uv1) 'Unexpected Bubble mesh color or second UV channel.'

$keywords = @{}
foreach ($path in Get-ChildItem -LiteralPath (Join-Path $ExportRoot 'Material') -Filter '*.json' -File) {
    $mat = Get-Content -LiteralPath $path.FullName -Raw | ConvertFrom-Json
    $keywords[$mat.m_Name] = $mat.ShaderKeywordState.Raw
    Assert ($mat.ShaderKeywordState.Status -eq 'Serialized') "$($mat.m_Name): shader keyword provenance missing."
    $materialRaw = Join-Path $path.DirectoryName $mat.SourceRaw.assetPath
    Assert ((Get-FileHash -LiteralPath $materialRaw -Algorithm SHA256).Hash -eq $mat.SourceRaw.sha256) "$($mat.m_Name): source Material raw provenance mismatch."
}
Assert ($keywords.Count -eq 4 -and $keywords['Eff_Water_059_00'] -ceq '' -and $keywords['Eff_Water_009_01'] -ceq '' -and $keywords['Eff_Glow_169_O_A_00'] -ceq '' -and $keywords['Eff_Water_084_00'] -ceq 'EFFECTED_BY_FOG') 'Source material keyword states changed.'
Assert (@($effect.dependencies | Where-Object kind -eq 'Texture2D').Count -eq 9) 'Expected nine source texture dependencies.'
Write-Output 'Mona stationary Idle source export regression passed.'
