param([Parameter(Mandatory)][string]$ExportRoot)

$ErrorActionPreference = 'Stop'
function Assert($condition, $message) { if (-not $condition) { throw $message } }

$manifest = Get-Content -LiteralPath (Join-Path $ExportRoot 'manifest.json') -Raw | ConvertFrom-Json
Assert (@($manifest.failures).Count -eq 0 -and @($manifest.missingSelections).Count -eq 0 -and @($manifest.unresolved).Count -eq 0) 'Source export has unresolved objects.'
$effectPath = Join-Path $ExportRoot $manifest.actions[0].effects[0].effectFile
$effect = Get-Content -LiteralPath $effectPath -Raw | ConvertFrom-Json
$root = $effect.objects | Where-Object { $_.id.pathID -eq '451401874049533967' }
Assert ($root.Name -eq 'Eff_Avatar_Girl_Catalyst_Mona_LiquidStrike_Idle') 'Wrong Idle root.'

$expected = @{
    MonoEffect = @{ pathID='5717950287282407900'; bytes=184; hash='96EFAF2845705C9CDD16AF444B33E53C'; script='-6095123100385083407' }
    MonoEffectPluginFollow = @{ pathID='-8372187366204061082'; bytes=312; hash='3A140E5C1F4C6A3CFFB17AB0F3E28A47'; script='-4579206049002199124' }
    MonoEffectPluginAudio = @{ pathID='-3699506857928399399'; bytes=296; hash='3328A57742DE2EE75925DEA077773399'; script='-7651656729664257369' }
}
$scripts = @($root.components | Where-Object { $_.script -in $expected.Keys })
Assert ($scripts.Count -eq 3) 'Expected three root action scripts.'
foreach ($component in $scripts) {
    $wanted = $expected[$component.script]
    Assert ($component.id.pathID -eq $wanted.pathID -and $component.raw.bytes -eq $wanted.bytes -and $component.raw.typeHash -eq $wanted.hash) "$($component.script): wrong source component."
    Assert ($component.scriptReference.pathID -eq $wanted.script -and $component.scriptReference.fileID -eq 0 -and $component.scriptReference.resolved) "$($component.script): script reference was lost."
    Assert ($component.scriptReference.className -eq $component.script -and -not [string]::IsNullOrEmpty($component.scriptReference.assemblyName)) "$($component.script): MonoScript metadata was lost."
    Assert ($component.actionData.unityHeaderBytes -eq 32 -and $component.actionData.customPayloadBytes -eq $wanted.bytes - 32) "$($component.script): header size changed."
    Assert ($component.coverage.status -eq 'Partial' -and -not $component.coverage.nativePlaybackReady) "$($component.script): custom script was declared ready."
    Assert ($component.coverage.unresolvedRanges.Count -eq 1 -and $component.coverage.unresolvedRanges[0].start -eq 32 -and $component.coverage.unresolvedRanges[0].end -eq $wanted.bytes) "$($component.script): untyped payload accounting changed."
    $raw = Join-Path (Split-Path $effectPath) $component.raw.assetPath
    Assert ((Get-FileHash -LiteralPath $raw -Algorithm SHA256).Hash -eq $component.raw.sha256) "$($component.script): raw provenance mismatch."
}
$audio = $scripts | Where-Object script -eq MonoEffectPluginAudio
$strings = @($audio.actionData.observedStrings)
Assert ($strings.Count -eq 2) 'Expected the two bounded Audio string observations.'
Assert ($strings[0].lengthOffset -eq 40 -and $strings[0].dataOffset -eq 44 -and $strings[0].byteCount -eq 42 -and $strings[0].text -ceq 'Play_sfx_char_mona_catalyst_invisable_idle') 'First source Audio string changed.'
Assert ($strings[1].lengthOffset -eq 116 -and $strings[1].dataOffset -eq 120 -and $strings[1].byteCount -eq 44 -and $strings[1].text -ceq 'Stop_sfx_char_mona_catalyst_invisable_sprint') 'Second source Audio string changed.'
Write-Output 'Mona action provenance regression passed; timing and follow behavior remain untyped.'
