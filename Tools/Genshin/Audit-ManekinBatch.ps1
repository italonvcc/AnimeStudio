param(
    [Parameter(Mandatory)][string]$BatchRoot,
    [string]$OutputPath = (Join-Path $BatchRoot 'payload-audit.json')
)

$ErrorActionPreference = 'Stop'
$batch = Get-Content -LiteralPath (Join-Path $BatchRoot 'batch-report.json') -Raw | ConvertFrom-Json
$results = foreach ($record in $batch.records) {
    $folder = Join-Path $BatchRoot $record.output
    $manifestPath = Join-Path $folder 'manifest.json'
    $errors = [System.Collections.Generic.List[string]]::new()
    $shaderLeaves = [System.Collections.Generic.HashSet[string]]::new()
    if ($record.status -ne 'exported' -or -not (Test-Path -LiteralPath $manifestPath)) {
        $errors.Add('Batch record or manifest not exported')
        [ordered]@{ source=$record.source.name; output=$record.output; status='failed'; errors=$errors; meshCount=0; materialBindings=0; shaderLeaves=@() }
        continue
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if (-not (Test-Path -LiteralPath (Join-Path $folder $manifest.model))) { $errors.Add('Model payload missing') }
    if (@($manifest.unresolved).Count -gt 0) { $errors.Add("$(@($manifest.unresolved).Count) unresolved source references") }
    if (@($manifest.nativeTextureFailures).Count -gt 0) { $errors.Add("$(@($manifest.nativeTextureFailures).Count) native texture write failures") }
    if (@($manifest.meshes).Count -eq 0) { $errors.Add('No meshes') }
    $bindingCount = 0
    foreach ($binding in @($manifest.materialBindings)) {
        $bindingCount++
        if ($binding.status -ne 'resolved') { $errors.Add("Unresolved material binding $($binding.rendererPath):$($binding.submeshIndex):$($binding.reason)"); continue }
        $materialPath = Join-Path $folder $binding.materialJsonPath
        if (-not (Test-Path -LiteralPath $materialPath)) { $errors.Add("Missing material JSON $($binding.materialJsonPath)"); continue }
        $material = Get-Content -LiteralPath $materialPath -Raw | ConvertFrom-Json -AsHashtable
        if ([string]::IsNullOrWhiteSpace($material.m_Shader.Name)) { $errors.Add("Missing shader name $($binding.materialJsonPath)") }
        else { [void]$shaderLeaves.Add($material.m_Shader.Name) }
        foreach ($property in $binding.textures.PSObject.Properties) {
            if (-not (Test-Path -LiteralPath (Join-Path $folder $property.Value))) {
                $errors.Add("Missing texture $($property.Name):$($property.Value)")
            }
            $native = $binding.nativeTextures.($property.Name)
            $intent = $binding.textureImport.($property.Name)
            if (-not $native -or -not $intent -or -not $intent.evidence -or $intent.mipCount -lt 1) {
                $errors.Add("Missing native texture/import intent $($property.Name)")
            }
            elseif (-not (Test-Path -LiteralPath (Join-Path $folder $native))) {
                $errors.Add("Missing native texture $($property.Name):$native")
            }
        }
    }
    if ($bindingCount -eq 0) { $errors.Add('No material bindings') }
    [ordered]@{ source=$record.source.name; output=$record.output;
        status=if ($errors.Count -eq 0) {'payload-resolved'} else {'audit-failed'};
        errors=@($errors); meshCount=@($manifest.meshes).Count;
        materialBindings=$bindingCount; shaderLeaves=@($shaderLeaves | Sort-Object) }
}
$report = [ordered]@{ schemaVersion=1; sourceBatchReport='batch-report.json';
    total=@($results).Count; payloadResolved=@($results | Where-Object status -eq 'payload-resolved').Count;
    auditFailed=@($results | Where-Object status -ne 'payload-resolved').Count; records=@($results) }
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath
([pscustomobject]@{ total=$report.total; payloadResolved=$report.payloadResolved;
    auditFailed=$report.auditFailed }) | ConvertTo-Json
if ($report.auditFailed -gt 0) { exit 1 }
