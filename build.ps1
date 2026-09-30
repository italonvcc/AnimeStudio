$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# A fresh staging directory prevents timestamp-based reuse of obsolete native DLLs.
$repo = [IO.Path]::GetFullPath($PSScriptRoot)
$run = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$records = Join-Path $repo "artifacts/build-records/$run"
$stage = Join-Path $repo "artifacts/build-stage-$run"
$destination = Join-Path $repo 'dist/net10.0-windows'
$package = Join-Path $stage 'package'
$framework = 'net10.0-windows'

function Assert-RepoPath([string]$path) {
    $full = [IO.Path]::GetFullPath($path)
    if (-not $full.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Output escapes repository: $full"
    }
    $ancestor = $full
    while ($ancestor) {
        if ((Test-Path -LiteralPath $ancestor) -and
            ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing linked output ancestor: $ancestor"
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    return $full
}
foreach ($path in $records,$stage,$package,$destination) { $null = Assert-RepoPath $path }
New-Item -ItemType Directory -Force $records,$stage,"$package/bin" | Out-Null
function Invoke-Dotnet([string]$label, [string[]]$arguments) {
    & dotnet @arguments '-m:1' '-p:UseSharedCompilation=false' *> (Join-Path $records "$label.log")
    if ($LASTEXITCODE -ne 0) { throw "$label failed; see $records/$label.log" }
}
Push-Location $repo
try {
    foreach ($app in 'CLI','GUI') {
        Write-Host "Publishing $app (Release, $framework)..."
        Invoke-Dotnet $app @('publish',"AnimeStudio.$app/AnimeStudio.$app.csproj",'-c','Release','-f',$framework,
            '--self-contained','false','--artifacts-path',"$stage/intermediate",'-o',"$stage/$app")
        foreach ($file in Get-ChildItem -LiteralPath "$stage/$app" -File -Recurse) {
            $relative = [IO.Path]::GetRelativePath("$stage/$app", $file.FullName)
            $target = Join-Path "$package/bin" $relative
            if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) {
                throw "GUI/CLI dependency mismatch: $relative"
            }
            New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($target)) | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        }
    }
    Invoke-Dotnet 'patcher' @('build','AnimeStudio.Patcher/AnimeStudio.Patcher.csproj','-c','Release','-f','net10.0',
        '--artifacts-path',"$stage/intermediate",'-o',"$stage/patcher")
    foreach ($app in 'CLI','GUI') {
        $exe = "$package/bin/AnimeStudio.$app.exe"
        & "$stage/patcher/AnimeStudio.Patcher.exe" $exe -d bin
        if ($LASTEXITCODE -ne 0) { throw "Apphost patch failed: $app" }
        Move-Item -LiteralPath (Assert-RepoPath $exe) -Destination (Assert-RepoPath "$package/AnimeStudio.$app.exe")
    }
    Copy-Item -LiteralPath "$repo/LICENSE" -Destination "$package/LICENSE"
    $nativeHash = (Get-FileHash -LiteralPath "$repo/AnimeStudio.Libraries/AnimeStudio.FBXNative.dll").Hash
    if ((Get-FileHash -LiteralPath "$package/bin/AnimeStudio.FBXNative.dll").Hash -ne $nativeHash) {
        throw 'Packaged FBXNative differs from the current source library'
    }
    & "$package/AnimeStudio.CLI.exe" --help *> "$records/cli-help.txt"
    if ($LASTEXITCODE -ne 0) { throw 'Packaged CLI startup failed' }
    $payload = @(Get-ChildItem -LiteralPath $package -File -Recurse | ForEach-Object {
        @{ path=[IO.Path]::GetRelativePath($package,$_.FullName); sha256=(Get-FileHash -LiteralPath $_.FullName).Hash }
    })
    $sourceFiles = @(& git ls-files --cached --others --exclude-standard -- 'AnimeStudio*' 'Directory.Build.props' 'build.ps1')
    $sourceHashes = @($sourceFiles | Sort-Object -Unique | Where-Object {
        (Test-Path -LiteralPath $_ -PathType Leaf) -and $_ -notmatch '(^|/)(bin|obj)/'
    } | ForEach-Object { @{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash} })
    $manifest = @{ buildUtc=$run; framework=$framework; configuration='Release';
        gitCommit=(& git rev-parse HEAD); workingTree= @(& git status --short);
        nativeFbxSha256=$nativeHash; sources=$sourceHashes; files=$payload; logs=$records;
        payloadScope='Build payload only; preserved user maps/settings are recorded separately.' }
    if (Test-Path -LiteralPath $destination) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory($destination,"$records/previous-distribution.zip")
        # These are user data, preserved in place and in the backup.
        foreach ($name in 'Maps','saved_transformation_options.xml') {
            $old = Join-Path $destination $name
            if (Test-Path -LiteralPath $old) { Copy-Item -LiteralPath $old -Destination $package -Recurse }
        }
    }
    $manifest.preservedUserData = @(Get-ChildItem -LiteralPath $package -File -Recurse | Where-Object {
        $relative = [IO.Path]::GetRelativePath($package,$_.FullName)
        $relative.StartsWith('Maps'+[IO.Path]::DirectorySeparatorChar) -or $relative -eq 'saved_transformation_options.xml'
    } | ForEach-Object { @{path=[IO.Path]::GetRelativePath($package,$_.FullName);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$package/build-manifest.json" -Encoding utf8
    New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
    $previous = Join-Path $stage 'previous-distribution'
    if (Test-Path -LiteralPath $destination) {
        Move-Item -LiteralPath (Assert-RepoPath $destination) -Destination (Assert-RepoPath $previous)
    }
    try {
        Move-Item -LiteralPath (Assert-RepoPath $package) -Destination (Assert-RepoPath $destination)
    } catch {
        if ((Test-Path -LiteralPath $previous) -and -not (Test-Path -LiteralPath $destination)) {
            Move-Item -LiteralPath (Assert-RepoPath $previous) -Destination (Assert-RepoPath $destination)
        }
        throw
    }
    Copy-Item -LiteralPath "$destination/build-manifest.json" -Destination "$records/build-manifest.json"
    Remove-Item -LiteralPath (Assert-RepoPath $stage) -Recurse -Force
    Write-Host "Built and checked: $destination" -ForegroundColor Green
} catch {
    $_ | Out-String | Set-Content -LiteralPath "$records/failure.txt"
    throw
} finally { Pop-Location }
