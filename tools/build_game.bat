@echo off
rem Сборка Backyard Bet в exe.
rem
rem Unity перед запуском надо ЗАКРЫТЬ: редактор держит проект, и второй
rem экземпляр на нём не стартует - сборка молча не начнётся.
rem
rem Запуск: двойной клик по этому файлу.

setlocal
chcp 65001 >nul

set UNITY=D:\Program Files\6000.5.10f1\Editor\Unity.exe
set PROJECT=D:\cloudi\experiment
set OUT=D:\cloudi\BackyardBet-Build

echo.
echo ==== Backyard Bet: сборка игры ====
echo Проект: %PROJECT%
echo Папка сборки: %OUT%
echo.

if not exist "%UNITY%" (
    echo НЕ НАЙДЕН Unity: %UNITY%
    echo Поправь путь в начале этого файла.
    pause
    exit /b 1
)

if exist "%PROJECT%\Temp\UnityLockfile" (
    echo Проект занят редактором Unity.
    echo Закрой Unity полностью и запусти этот файл снова.
    pause
    exit /b 1
)

echo Собираю. Это занимает несколько минут, окно закрывать нельзя.
echo.

"%UNITY%" -batchmode -quit -projectPath "%PROJECT%" ^
    -executeMethod BuildGame.FromCommandLine -buildOut "%OUT%" ^
    -logFile "%OUT%\build.log"

if errorlevel 1 (
    echo.
    echo СБОРКА НЕ УДАЛАСЬ. Смотри %OUT%\build.log
    pause
    exit /b 1
)

echo.
echo Готово. Игра здесь: %OUT%\BackyardBet.exe
echo Рядом лежит "КАК ИГРАТЬ.txt" - отправь его друзьям вместе с игрой.
echo.
explorer "%OUT%"
pause
