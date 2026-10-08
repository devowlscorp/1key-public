@echo off
chcp 949 >nul
setlocal EnableExtensions
title 1Key 강제 종료

rem ---------------------------------------------------------------------------
rem 1Key 를 종료했는데도 프로세스가 남아 있을 때 쓰는 도구입니다.
rem 남은 프로세스가 단일 실행 잠금을 쥐고 있으면 새로 실행할 때
rem "이미 실행 중입니다" 안내만 나오고 시작되지 않습니다. 그때 이 파일을 실행하세요.
rem
rem 사용법: 그냥 두 번 누르면 됩니다.
rem         (실행 파일 이름을 바꿔서 쓰는 경우에만  1Key-강제종료.cmd 내파일.exe  처럼 지정)
rem
rem 주의: 이 파일은 CP949(ANSI)로 저장해야 한글이 깨지지 않습니다.
rem ---------------------------------------------------------------------------

set "IMG=%~1"
if "%IMG%"=="" set "IMG=1Key.exe"

echo.
echo   [1Key 강제 종료]  대상: %IMG%
echo.

call :LIST
if errorlevel 1 (
    echo   실행 중인 %IMG% 가 없습니다. 바로 다시 실행하셔도 됩니다.
    goto :END
)

echo   아래 프로세스를 종료합니다.
echo.
tasklist /FI "IMAGENAME eq %IMG%" /FO table | findstr /I "%IMG%"
echo.

taskkill /F /IM "%IMG%" /T >nul 2>&1
call :WAIT

call :LIST
if errorlevel 1 (
    echo   종료했습니다. 이제 1Key 를 다시 실행하시면 됩니다.
    goto :END
)

echo   일반 권한으로는 끝낼 수 없습니다. 관리자 권한으로 다시 시도합니다.
echo   UAC 창이 뜨면 [예] 를 눌러 주세요.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Start-Process -FilePath 'taskkill.exe' -ArgumentList '/F','/IM','%IMG%','/T' -Verb RunAs -WindowStyle Hidden -Wait } catch { exit 1 }"
call :WAIT

call :LIST
if errorlevel 1 (
    echo   종료했습니다. 이제 1Key 를 다시 실행하시면 됩니다.
    goto :END
)

echo   아직 남아 있습니다. 작업 관리자(Ctrl+Shift+Esc)의 [세부 정보] 탭에서
echo   %IMG% 를 찾아 직접 [작업 끝내기] 해 주세요.
echo.
tasklist /FI "IMAGENAME eq %IMG%" /FO table | findstr /I "%IMG%"

:END
echo.
pause
exit /b 0

rem --- 대상 프로세스가 있으면 errorlevel 0, 없으면 1 ---
:LIST
tasklist /FI "IMAGENAME eq %IMG%" 2>nul | findstr /I /C:"%IMG%" >nul
exit /b %errorlevel%

rem --- 종료가 반영될 때까지 잠깐 기다린다 ---
:WAIT
ping -n 3 127.0.0.1 >nul
exit /b 0
