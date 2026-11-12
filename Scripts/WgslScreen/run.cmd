@echo off
rem Offline WGSL screen, end to end: snapshot ILGPU binaries, build the tool, generate WGSL for the shared test suite
rem (and optionally a consumer assembly) with the narrowing pass ON and OFF, validate with naga, compute inlined sizes,
rem write report.txt. No GPU, no network. One process at a time. See README.md.
rem
rem   run.cmd [outputRoot] [consumerDir] [ilgpuBinDir]
rem
rem   outputRoot   default %TEMP%\wgslscreen. Everything the run produces goes here, nothing goes into the repo.
rem   consumerDir  folder with the consumer assembly and its dependencies. Else env WGSLSCREEN_CONSUMER_DIR, else
rem                the SpawnDev.Codecs DemoConsole Release folder if it exists. Missing = consumer run skipped.
rem   ilgpuBinDir  folder with the freshly built ILGPU binaries. Else env WGSLSCREEN_ILGPU_DIR, else the
rem                SpawnDev.ILGPU.DemoConsole Release folder of this repo.
rem   env WGSLSCREEN_CONSUMER_DLL  consumer assembly file name, default SpawnDev.Codecs.dll
rem   env NAGA_EXE                 naga executable; else naga.exe on PATH; else %USERPROFILE%\.cargo\bin\naga.exe
rem   env WGSLSCREEN_RESUME=1      keep the existing snapshot and result folders and continue an interrupted run
rem                                (the driver skips kernels already recorded in results.tsv)
rem Exit code: 0 = every step finished; non-zero = the step that failed (4 = ILGPU was not loaded from the snapshot).
setlocal
set "HERE=%~dp0"
if "%HERE:~-1%"=="\" set "HERE=%HERE:~0,-1%"

rem ---- arguments and defaults -------------------------------------------------------------------------------------
set "OUT=%~1"
if not defined OUT set "OUT=%TEMP%\wgslscreen"
set "CONSUMER_DIR=%~2"
if not defined CONSUMER_DIR set "CONSUMER_DIR=%WGSLSCREEN_CONSUMER_DIR%"
rem default consumer: SpawnDev.Codecs checked out beside SpawnDev.ILGPU (four folders up from this script)
set "CODECS_DEFAULT=%HERE%\..\..\..\..\SpawnDev.Codecs\SpawnDev.Codecs\SpawnDev.Codecs.DemoConsole\bin\Release\net10.0"
if not defined CONSUMER_DIR if exist "%CODECS_DEFAULT%\" set "CONSUMER_DIR=%CODECS_DEFAULT%"
set "ILGPU_DIR=%~3"
if not defined ILGPU_DIR set "ILGPU_DIR=%WGSLSCREEN_ILGPU_DIR%"
if not defined ILGPU_DIR set "ILGPU_DIR=%HERE%\..\..\SpawnDev.ILGPU.DemoConsole\bin\Release\net10.0"
set "CONSUMER_DLL=%WGSLSCREEN_CONSUMER_DLL%"
if not defined CONSUMER_DLL set "CONSUMER_DLL=SpawnDev.Codecs.dll"

rem ---- absolute paths without a trailing backslash (a trailing one breaks quoted command-line arguments) ----------
for %%I in ("%OUT%") do set "OUT=%%~fI"
for %%I in ("%ILGPU_DIR%") do set "ILGPU_DIR=%%~fI"
if defined CONSUMER_DIR for %%I in ("%CONSUMER_DIR%") do set "CONSUMER_DIR=%%~fI"
if "%OUT:~-1%"=="\" set "OUT=%OUT:~0,-1%"
if "%ILGPU_DIR:~-1%"=="\" set "ILGPU_DIR=%ILGPU_DIR:~0,-1%"
if defined CONSUMER_DIR if "%CONSUMER_DIR:~-1%"=="\" set "CONSUMER_DIR=%CONSUMER_DIR:~0,-1%"

set "REFS=%OUT%\refs"
set "CONSUMER_COPY=%OUT%\consumer"
set "BUILD=%OUT%\build"
set "EXE=%BUILD%\bin\Screen.exe"

rem ---- tools --------------------------------------------------------------------------------------------------------
set "NAGA="
if defined NAGA_EXE goto :naga_env
for /f "delims=" %%N in ('where naga.exe 2^>nul') do if not defined NAGA set "NAGA=%%N"
if not defined NAGA if exist "%USERPROFILE%\.cargo\bin\naga.exe" set "NAGA=%USERPROFILE%\.cargo\bin\naga.exe"
if not defined NAGA goto :no_naga
goto :naga_done
:naga_env
if not exist "%NAGA_EXE%" goto :bad_naga_env
set "NAGA=%NAGA_EXE%"
:naga_done
where python >nul 2>&1
if errorlevel 1 goto :no_python
where dotnet >nul 2>&1
if errorlevel 1 goto :no_dotnet

if not exist "%ILGPU_DIR%\SpawnDev.ILGPU.dll" goto :no_ilgpu
if not exist "%ILGPU_DIR%\SpawnDev.ILGPU.Demo.Shared.dll" goto :no_ilgpu

set "DO_CONSUMER=1"
if not defined CONSUMER_DIR goto :consumer_skip_unset
if not exist "%CONSUMER_DIR%\%CONSUMER_DLL%" goto :consumer_skip_missing
goto :consumer_ok
:consumer_skip_unset
echo [run] consumer run SKIPPED: no consumer folder given, WGSLSCREEN_CONSUMER_DIR is not set and the default folder does not exist
set "DO_CONSUMER="
goto :consumer_ok
:consumer_skip_missing
echo [run] consumer run SKIPPED: %CONSUMER_DIR%\%CONSUMER_DLL% does not exist
set "DO_CONSUMER="
:consumer_ok

echo [run] output root : %OUT%
echo [run] ilgpu bins  : %ILGPU_DIR%
if defined DO_CONSUMER echo [run] consumer     : %CONSUMER_DIR%\%CONSUMER_DLL%
echo [run] naga        : %NAGA%

rem ---- 1. snapshot --------------------------------------------------------------------------------------------------
if defined WGSLSCREEN_RESUME goto :resume_skip_refresh
echo [run] 1/6 snapshot
if exist "%REFS%" rmdir /s /q "%REFS%"
if exist "%CONSUMER_COPY%" rmdir /s /q "%CONSUMER_COPY%"
for %%D in (suite_on suite_off consumer_on consumer_off) do if exist "%OUT%\%%D" rmdir /s /q "%OUT%\%%D"
del /q "%OUT%\analyzer_*.txt" "%OUT%\report.txt" 2>nul
mkdir "%REFS%"
if errorlevel 1 goto :fail
copy /y "%ILGPU_DIR%\*.dll" "%REFS%\" >nul
if errorlevel 1 goto :fail
if not defined DO_CONSUMER goto :refresh_done
mkdir "%CONSUMER_COPY%"
if errorlevel 1 goto :fail
copy /y "%CONSUMER_DIR%\*.dll" "%CONSUMER_COPY%\" >nul
if errorlevel 1 goto :fail
goto :refresh_done
:resume_skip_refresh
echo [run] 1/6 snapshot SKIPPED (WGSLSCREEN_RESUME is set, reusing %REFS%)
:refresh_done

rem ---- 2. build -----------------------------------------------------------------------------------------------------
rem The build folder is wiped every time: copy keeps source timestamps, so an incremental build could keep an
rem executable compiled against an older snapshot. Output and intermediate folders are both under the output root.
echo [run] 2/6 build
if exist "%BUILD%" rmdir /s /q "%BUILD%"
dotnet build "%HERE%\Screen\Screen.csproj" -c Release -v q --nologo -o "%BUILD%\bin" -p:RefsDir="%REFS%" -p:BaseIntermediateOutputPath="%BUILD%\obj/"
if errorlevel 1 goto :fail
if not exist "%EXE%" goto :no_exe

rem ---- 3. generate WGSL ---------------------------------------------------------------------------------------------
echo [run] 3/6 generate WGSL
call :screen "%REFS%\SpawnDev.ILGPU.Demo.Shared.dll" BackendTestBase . "%OUT%\suite_on" --refs "%REFS%"
if errorlevel 1 goto :fail
call :screen "%REFS%\SpawnDev.ILGPU.Demo.Shared.dll" BackendTestBase . "%OUT%\suite_off" --refs "%REFS%" --no-narrow
if errorlevel 1 goto :fail
if not defined DO_CONSUMER goto :gen_done
call :screen "%CONSUMER_COPY%\%CONSUMER_DLL%" . . "%OUT%\consumer_on" --refs "%REFS%"
if errorlevel 1 goto :fail
call :screen "%CONSUMER_COPY%\%CONSUMER_DLL%" . . "%OUT%\consumer_off" --refs "%REFS%" --no-narrow
if errorlevel 1 goto :fail
:gen_done

rem ---- 4. naga -----------------------------------------------------------------------------------------------------
echo [run] 4/6 naga validation
call :screen naga "%OUT%\suite_on" --naga "%NAGA%"
if errorlevel 1 goto :fail
call :screen naga "%OUT%\suite_off" --naga "%NAGA%"
if errorlevel 1 goto :fail
if not defined DO_CONSUMER goto :naga_done2
call :screen naga "%OUT%\consumer_on" --naga "%NAGA%"
if errorlevel 1 goto :fail
call :screen naga "%OUT%\consumer_off" --naga "%NAGA%"
if errorlevel 1 goto :fail
:naga_done2

rem ---- 5. inlined sizes ---------------------------------------------------------------------------------------------
echo [run] 5/6 inlined size analyzer
python -I "%HERE%\run_analyzer.py" "%OUT%\suite_on" "%OUT%\analyzer_suite_on.txt"
if errorlevel 1 goto :fail
python -I "%HERE%\run_analyzer.py" "%OUT%\suite_off" "%OUT%\analyzer_suite_off.txt"
if errorlevel 1 goto :fail
if not defined DO_CONSUMER goto :ana_done
python -I "%HERE%\run_analyzer.py" "%OUT%\consumer_on" "%OUT%\analyzer_consumer_on.txt"
if errorlevel 1 goto :fail
python -I "%HERE%\run_analyzer.py" "%OUT%\consumer_off" "%OUT%\analyzer_consumer_off.txt"
if errorlevel 1 goto :fail
:ana_done

rem ---- 6. report ----------------------------------------------------------------------------------------------------
echo [run] 6/6 report
python -I "%HERE%\report.py" "%OUT%" > "%OUT%\report.txt"
if errorlevel 1 goto :fail

echo [run] done. Report: %OUT%\report.txt
exit /b 0

rem ---- helpers ------------------------------------------------------------------------------------------------------
:screen
"%EXE%" %*
exit /b %errorlevel%

:fail
set "RC=%errorlevel%"
echo [run] FAILED with exit code %RC%
exit /b %RC%

:no_naga
echo [run] ERROR: naga not found. Set NAGA_EXE, put naga.exe on PATH, or install it with: cargo install naga-cli
exit /b 1
:bad_naga_env
echo [run] ERROR: NAGA_EXE is set but the file does not exist: %NAGA_EXE%
exit /b 1
:no_python
echo [run] ERROR: python not found on PATH
exit /b 1
:no_dotnet
echo [run] ERROR: dotnet not found on PATH
exit /b 1
:no_ilgpu
echo [run] ERROR: SpawnDev.ILGPU.dll or SpawnDev.ILGPU.Demo.Shared.dll missing in %ILGPU_DIR%
echo [run]        build SpawnDev.ILGPU.DemoConsole in Release first, or pass the folder as the third argument.
exit /b 1
:no_exe
echo [run] ERROR: build finished but %EXE% does not exist
exit /b 1
