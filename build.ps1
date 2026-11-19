# Build PinDisplay ra thư mục dist\
#   dist\PinDisplay.exe             — nhỏ (~0.2 MB), cần .NET 10 Desktop Runtime
#   dist\PinDisplay-standalone.exe  — chạy ở mọi máy Windows 10/11 x64, không cần cài .NET
# Chạy: powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not (Test-Path app.ico)) { & "$PSScriptRoot\tools\make-icon.ps1" }

$common = @('-c', 'Release', '-r', 'win-x64', '-p:PublishSingleFile=true', '-p:DebugType=none', '-nologo', '-v', 'q')

dotnet publish @common --self-contained false -o obj\dist-fd
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet publish @common --self-contained true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o obj\dist-sc
if ($LASTEXITCODE) { exit $LASTEXITCODE }

New-Item -ItemType Directory -Force dist | Out-Null
Copy-Item obj\dist-fd\PinDisplay.exe dist\PinDisplay.exe -Force
Copy-Item obj\dist-sc\PinDisplay.exe dist\PinDisplay-standalone.exe -Force
Get-ChildItem dist | Select-Object Name, @{n = 'Size (MB)'; e = { [math]::Round($_.Length / 1MB, 2) } }
