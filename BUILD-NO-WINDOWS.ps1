# ============================================================
#  VoxAiGo Windows — Script de Build
#  Execute com duplo clique ou: powershell -ExecutionPolicy Bypass -File BUILD-NO-WINDOWS.ps1
# ============================================================

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "   VoxAiGo Windows - Build Automatico" -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host ""

# --- 1. Verificar .NET 8 SDK ---
Write-Host "[1/4] Verificando .NET 8 SDK..." -ForegroundColor Yellow

$dotnetVersion = $null
try {
    $dotnetVersion = & dotnet --version 2>$null
} catch {}

if (-not $dotnetVersion -or -not $dotnetVersion.StartsWith("8.")) {
    Write-Host ""
    Write-Host "ERRO: .NET 8 SDK nao encontrado!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Instale o .NET 8 SDK em:" -ForegroundColor White
    Write-Host "https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Blue
    Write-Host ""
    Write-Host "Pressione qualquer tecla para abrir o site de download..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    Start-Process "https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
}

Write-Host "   OK: .NET $dotnetVersion encontrado" -ForegroundColor Green

# --- 2. Restaurar dependencias ---
Write-Host ""
Write-Host "[2/4] Restaurando dependencias NuGet..." -ForegroundColor Yellow

$slnPath = Join-Path $PSScriptRoot "VoxAiGo.sln"
& dotnet restore $slnPath

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERRO ao restaurar dependencias!" -ForegroundColor Red
    exit 1
}

Write-Host "   OK: Dependencias restauradas" -ForegroundColor Green

# --- 3. Build & Publish ---
Write-Host ""
Write-Host "[3/4] Compilando e gerando .exe (pode demorar ~60s)..." -ForegroundColor Yellow

$projPath = Join-Path $PSScriptRoot "src\VoxAiGo.App\VoxAiGo.App.csproj"
$outputPath = Join-Path $PSScriptRoot "publish"

& dotnet publish $projPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $outputPath

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERRO durante o build!" -ForegroundColor Red
    Write-Host "Verifique os erros acima e tente novamente." -ForegroundColor White
    Read-Host "Pressione Enter para sair"
    exit 1
}

# --- 4. Resultado ---
Write-Host ""
Write-Host "[4/4] Verificando arquivo gerado..." -ForegroundColor Yellow

$exePath = Join-Path $outputPath "VoxAiGo.App.exe"

if (Test-Path $exePath) {
    $size = [math]::Round((Get-Item $exePath).Length / 1MB, 1)
    Write-Host ""
    Write-Host "=================================================" -ForegroundColor Green
    Write-Host "   BUILD CONCLUIDO COM SUCESSO!" -ForegroundColor Green
    Write-Host "=================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "   Arquivo: VoxAiGo.App.exe ($size MB)" -ForegroundColor White
    Write-Host "   Local:   $outputPath" -ForegroundColor White
    Write-Host ""
    Write-Host "Abrindo pasta com o executavel..." -ForegroundColor Cyan
    Start-Process "explorer.exe" $outputPath
} else {
    Write-Host "ERRO: .exe nao foi gerado!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Pressione qualquer tecla para fechar..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
