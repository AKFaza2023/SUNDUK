$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$project = Join-Path $root 'src\Sunduk.Desktop\Sunduk.Desktop.csproj'
$output = Join-Path $root 'publish\win-x64'

if (Test-Path $output) {
    Remove-Item $output -Recurse -Force
}

dotnet restore $project -r win-x64

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    --no-restore `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output

$exe = Join-Path $output 'SUNDUK.exe'
if (-not (Test-Path $exe)) {
    throw 'SUNDUK.exe was not produced.'
}

$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
"SHA256  $hash  SUNDUK.exe" | Set-Content -Encoding ascii (Join-Path $output 'SHA256.txt')

Write-Host ''
Write-Host "SUNDUK.exe: $exe"
Write-Host "SHA256: $hash"
