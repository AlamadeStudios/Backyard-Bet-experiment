# Пересборка карты из Blender и перенос в проект Unity.
#
# Нужен потому, что исходник карты и проект Unity живут в разных папках и
# расходятся молча: правки в генераторе делаются, а в игре остаётся старый
# FBX. Именно так дом однажды простоял монолитом уже после того, как в
# генераторе у него появились стены и мебель.
#
# Запуск из корня репозитория:
#     powershell -ExecutionPolicy Bypass -File tools\rebuild_map.ps1
#
# Unity при этом лучше закрыть: он держит FBX и может не заметить подмену.

param(
    [string]$Blender = "D:\steam\steamapps\common\Blender\blender.exe",
    [string]$Source  = "D:\cloudi\blender"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path $Blender)) {
    Write-Error "Не найден Blender: $Blender. Укажи путь через -Blender"
}

$script = Join-Path $repo "blender\backyard_bet.py"
if (-not (Test-Path $script)) { Write-Error "Не найден генератор: $script" }

Write-Host "Собираю карту в Blender..." -ForegroundColor Cyan
& $Blender --background --python $script -- fbx novstills | Select-String -Pattern '^### '

$map = Join-Path $Source "BackyardBet.fbx"
if (-not (Test-Path $map)) { Write-Error "Экспорт не создал $map" }

Copy-Item $map (Join-Path $repo "Assets\BackyardBet\Map\BackyardBet.fbx") -Force
Write-Host "Карта перенесена" -ForegroundColor Green

foreach ($ch in @("CH_Bo", "CH_Mia", "CH_Rex", "CH_Sam")) {
    $src = Join-Path $Source "$ch.fbx"
    if (Test-Path $src) {
        Copy-Item $src (Join-Path $repo "Assets\BackyardBet\Characters\$ch.fbx") -Force
    }
}
Write-Host "Персонажи перенесены" -ForegroundColor Green
Write-Host "Готово. Открой Unity - он переимпортирует карту." -ForegroundColor Cyan
