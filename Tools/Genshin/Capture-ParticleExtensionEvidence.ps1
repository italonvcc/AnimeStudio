param(
    [Parameter(Mandatory)][string]$PlayerExecutable,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$probe = Join-Path $PSScriptRoot 'ParticleExtensionProbe/bin/Debug/net10.0/ParticleExtensionProbe.dll'
if (-not (Test-Path -LiteralPath $probe)) { throw 'Build ParticleExtensionProbe first.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a fresh evidence directory.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
# Bounded ranges discovered through native constructor -> transfer -> field joins.
# These are executable file offsets, never ParticleSystem serialized offsets.
$ranges = @(
    @('shape-named-transfer',20395728,5032), @('shape-fast-read',20400768,2607),
    @('shape-property-registration',20407392,7483), @('shape-property-getter',20414880,1404),
    @('shape-constructor',20279024,1650),
    @('shape-mesh-acquisition-gate',20283840,1303),
    @('shape-sampling-dispatch',20286672,6764),
    @('emission-named-transfer',20718512,2528), @('emission-fast-read',20721040,1520),
    @('header-named-transfer',20540800,2019), @('header-fast-read',20542832,1328),
    @('particle-modules-named-transfer',21182848,2702),
    @('text-named-transfer',19934880,569), @('text-fast-read',19935456,698),
    @('color-named-transfer',20820800,131), @('color-fast-read',20820944,48)
)
foreach ($range in $ranges) {
    & dotnet $probe $PlayerExecutable $range[1] $range[2] |
        Set-Content -LiteralPath (Join-Path $OutputDirectory ($range[0]+'.txt')) -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw "Probe failed: $($range[0])" }
}
Get-ChildItem -LiteralPath $OutputDirectory -File | ForEach-Object {
    [pscustomobject]@{name=$_.Name; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'evidence-hashes.json') -Encoding utf8
