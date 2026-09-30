[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RuntimeDirectory,
    [string]$OutputZip
)

$ErrorActionPreference = 'Stop'
$studyRoot = Split-Path $PSScriptRoot -Parent
$sourceRoot = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
if (-not $OutputZip) {
    $OutputZip = Join-Path $PSScriptRoot 'renderdoc/renderdoc-1.45-genshin-win64.zip'
}
$OutputZip = [IO.Path]::GetFullPath($OutputZip)
if ((Test-Path -LiteralPath $OutputZip) -or (Test-Path -LiteralPath "$OutputZip.sha256")) {
    throw 'Output already exists. Choose a fresh -OutputZip; existing packages are preserved.'
}
$originalHash = '313f450bfa1f7f7d5f50cc7ee80d9f99b68d3fe873e25eb865ff0433e359f7b3'
$patchedHash = 'cbcab42f67e784540fc321ce0fc2ddb215ac005510cbef6ba903965f17411d18'
$guiHash = 'c9904905fe380b2869d48c7a4209c2331370e0bfda502a24da26ec4031cf885b'
function Get-BytesHash([byte[]]$Bytes) {
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hasher.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $hasher.Dispose() }
}

$dllPath = Join-Path $sourceRoot 'renderdoc.dll'
$dllBytes = [IO.File]::ReadAllBytes($dllPath)
$sourceHash = Get-BytesHash $dllBytes
if ($sourceHash -notin @($originalHash, $patchedHash)) { throw 'Unrecognized RenderDoc DLL; require the pinned 1.45 build.' }
if ((Get-FileHash -LiteralPath (Join-Path $sourceRoot 'qrenderdoc.exe')).Hash.ToLowerInvariant() -ne $guiHash) {
    throw 'RenderDoc GUI does not match the pinned 1.45 distribution.'
}
foreach ($required in @('renderdoccmd.exe', 'LICENSE.md', 'LICENSE.rtf', 'Qt5Core.dll', 'python36.dll', 'qtplugins', 'plugins', 'x86')) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot $required))) { throw "Incomplete runtime: missing $required" }
}
$sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File)
if ($sourceFiles.Count -ne 66) { throw "Expected the preserved 66-file runtime; found $($sourceFiles.Count). Inspect the distribution before packaging." }
foreach ($file in $sourceFiles) {
    if ($file.Extension -in @('.rdc', '.cap', '.log', '.dmp')) { throw "Unexpected local capture/configuration in runtime: $($file.Name)" }
}

# Both input forms must reconstruct the exact pinned original with a single byte.
$offset = 0x343b61
$originalBytes = [byte[]]$dllBytes.Clone()
$originalBytes[$offset] = 0x74
if ((Get-BytesHash $originalBytes) -ne $originalHash) { throw 'Original DLL reconstruction failed.' }
$signature = ([BitConverter]::ToString($originalBytes[($offset - 7)..($offset + 1)])).Replace('-', '').ToLowerInvariant()
if ($signature -ne '8bc7c1e80724017419') { throw 'Unexpected D3D11 branch signature.' }
$patchedBytes = [byte[]]$originalBytes.Clone()
$patchedBytes[$offset] = 0xeb
if ((Get-BytesHash $patchedBytes) -ne $patchedHash) { throw 'Patched DLL identity mismatch.' }

$stageRoot = Join-Path $studyRoot ('Export/renderdoc-package-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stageRoot | Out-Null
foreach ($item in Get-ChildItem -LiteralPath $sourceRoot -Force) {
    Copy-Item -LiteralPath $item.FullName -Destination $stageRoot -Recurse
}
[IO.File]::WriteAllBytes((Join-Path $stageRoot 'renderdoc.dll'), $patchedBytes)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'renderdoc/README.md') -Destination (Join-Path $stageRoot 'README.md')

$files = @(Get-ChildItem -LiteralPath $stageRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
    [ordered]@{
        path = $_.FullName.Substring($stageRoot.Length + 1).Replace('\', '/')
        bytes = $_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()
    }
})
$manifest = [ordered]@{
    package = 'RenderDoc 1.45 custom Genshin capture build, Windows x64'
    upstream = 'https://github.com/baldurk/renderdoc/releases/tag/v1.45'
    validation = 'User confirmed Genshin startup and frame capture on 2026-09-27; capture replay was not inspected by the assistant.'
    patch = [ordered]@{
        file = 'renderdoc.dll'; originalSha256 = $originalHash; modifiedSha256 = $patchedHash
        rva = '0x344761'; fileOffset = '0x343b61'; originalByte = '74'; modifiedByte = 'eb'
        purpose = 'Select normal D3D11 device/context wrapping when the opt-out flag is present.'
    }
    files = $files
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $stageRoot 'manifest.json') -Encoding utf8
New-Item -ItemType Directory -Path (Split-Path $OutputZip -Parent) -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stageRoot, $OutputZip, [IO.Compression.CompressionLevel]::Optimal, $false)

# Read and hash every archived file, not just the archive header.
$archive = [IO.Compression.ZipFile]::OpenRead($OutputZip)
try {
    $entries = @($archive.Entries | Where-Object { $_.Name })
    if ($entries.Count -ne ($files.Count + 1)) { throw 'Archive file count mismatch.' }
    foreach ($record in $files) {
        $entry = $archive.GetEntry($record.path)
        if ($null -eq $entry -or $entry.Length -ne $record.bytes) { throw "Archive entry mismatch: $($record.path)" }
        $stream = $entry.Open()
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { $hash = ([BitConverter]::ToString($hasher.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
        finally { $stream.Dispose(); $hasher.Dispose() }
        if ($hash -ne $record.sha256) { throw "Archive checksum mismatch: $($record.path)" }
    }
    $reader = [IO.StreamReader]::new($archive.GetEntry('manifest.json').Open())
    try { $archivedManifest = $reader.ReadToEnd() | ConvertFrom-Json }
    finally { $reader.Dispose() }
    if ($archivedManifest.patch.modifiedSha256 -ne $patchedHash -or $archivedManifest.files.Count -ne $files.Count) {
        throw 'Archived manifest verification failed.'
    }
}
finally { $archive.Dispose() }
if ((Get-Item -LiteralPath $OutputZip).Length -ge 100MB) { throw 'ZIP exceeds GitHub regular-file limit; use a release asset.' }
$zipHash = (Get-FileHash -LiteralPath $OutputZip).Hash.ToLowerInvariant()
"$zipHash  $([IO.Path]::GetFileName($OutputZip))" | Set-Content -LiteralPath "$OutputZip.sha256" -Encoding ascii
Write-Output "Verified $($files.Count + 1) archive files. ZIP: $OutputZip"
Write-Output "SHA-256: $zipHash"
