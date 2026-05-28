# ─────────────────────────────────────────────────────────────────────────────
# publish-release.ps1
# Publica MainApp como ejecutable único, crea el ZIP y actualiza el
# GitHub Release correspondiente a la versión actual del .csproj.
#
# Uso:
#   .\publish-release.ps1              → publica + actualiza release existente
#   .\publish-release.ps1 -NewRelease  → crea un release nuevo (si cambiaste la versión)
#   .\publish-release.ps1 -SkipBuild   → solo reempaqueta y sube (build ya hecho)
# ─────────────────────────────────────────────────────────────────────────────
param(
    [switch]$NewRelease,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Rutas ─────────────────────────────────────────────────────────────────────
$Root       = $PSScriptRoot
$CsprojPath = Join-Path $Root "MainApp\MainApp.csproj"
$PublishDir = Join-Path $Root "publish"
$ZipDir     = Join-Path $Root "dist"

# ── Leer versión del .csproj ──────────────────────────────────────────────────
[xml]$csproj = Get-Content $CsprojPath
$Version     = $csproj.Project.PropertyGroup.Version
if (-not $Version) { throw "No se encontró <Version> en MainApp.csproj" }

$Tag     = "v$Version"
$ZipName = "AlbionApp-$Tag.zip"
$ZipPath = Join-Path $ZipDir $ZipName

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host "  Albion App  $Tag" -ForegroundColor White
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host ""

# ── Paso 1: Publicar ──────────────────────────────────────────────────────────
if (-not $SkipBuild) {
    Write-Host "[ 1/3 ]  Publicando..." -ForegroundColor Yellow

    if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

    dotnet publish "$Root\MainApp\MainApp.csproj" `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o "$PublishDir" `
        --nologo -v q

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló (exit $LASTEXITCODE)" }
    Write-Host "         Publicado en: $PublishDir" -ForegroundColor DarkGray
} else {
    Write-Host "[ 1/3 ]  Build omitido (-SkipBuild)" -ForegroundColor DarkGray
}

# ── Paso 2: Crear ZIP ─────────────────────────────────────────────────────────
Write-Host "[ 2/3 ]  Empaquetando..." -ForegroundColor Yellow

if (-not (Test-Path $ZipDir)) { New-Item -ItemType Directory -Path $ZipDir | Out-Null }
if (Test-Path $ZipPath)       { Remove-Item $ZipPath -Force }

Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -CompressionLevel Optimal

$SizeMB = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
Write-Host "         $ZipName  ($SizeMB MB)" -ForegroundColor DarkGray

# ── Paso 3: GitHub Release ────────────────────────────────────────────────────
Write-Host "[ 3/3 ]  Actualizando GitHub Release..." -ForegroundColor Yellow

# Comprobar si el release ya existe
$releaseExists = gh release view $Tag 2>$null
if ($LASTEXITCODE -eq 0 -and -not $NewRelease) {
    # Release existente → reemplazar el asset ZIP
    Write-Host "         Release $Tag encontrado — reemplazando asset..." -ForegroundColor DarkGray
    gh release upload $Tag $ZipPath --clobber
    if ($LASTEXITCODE -ne 0) { throw "gh release upload falló" }
} else {
    # Release nuevo (o -NewRelease forzado)
    Write-Host "         Creando release $Tag..." -ForegroundColor DarkGray
    gh release create $Tag $ZipPath `
        --title "Albion App $Tag" `
        --notes "Release $Tag" `
        --latest
    if ($LASTEXITCODE -ne 0) { throw "gh release create falló" }
}

Write-Host ""
Write-Host "  ✅  Release $Tag actualizado correctamente." -ForegroundColor Green
Write-Host "      $ZipPath" -ForegroundColor DarkGray
Write-Host ""
