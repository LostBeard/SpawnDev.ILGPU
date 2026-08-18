@echo off
REM Local equivalent of the CI "T4 Drift Check" workflow.
REM Run before pushing if you've touched anything under ILGPU/ or ILGPU.Algorithms/.
REM
REM What it does: forces T4 transforms (clean build), then checks git for any .cs
REM file that was regenerated to something different than what's committed.
REM If drift is detected, the build is failing-pending and the script prints the
REM exact files + the fix. Same check CI runs - catching it locally saves a CI
REM round-trip.

setlocal enabledelayedexpansion
cd /d "%~dp0"

echo [tt-drift] Forcing clean build of ILGPU.csproj...
if exist ILGPU\obj rmdir /s /q ILGPU\obj
if exist ILGPU\bin rmdir /s /q ILGPU\bin
if exist ILGPU.Algorithms\obj rmdir /s /q ILGPU.Algorithms\obj
if exist ILGPU.Algorithms\bin rmdir /s /q ILGPU.Algorithms\bin

REM -p:TextTemplateTransformSkipUpToDate=false forces T4 to regenerate on every build,
REM mirroring CI's fresh-clone behavior (where all files have equal mtime). Without the
REM flag, T4.Build's --skip-up-to-date check sees the working-tree .cs mtime as newer
REM than the .tt and skips regen, which lets local builds silently pass while CI fails.
REM Hit this 2026-04-28 - local drift check passed, CI flagged ArithmeticOperations.cs.
dotnet build ILGPU\ILGPU.csproj -c Release --nologo -p:TextTemplateTransformSkipUpToDate=false
if errorlevel 1 (
    echo [tt-drift] BUILD FAILED. Fix compile errors first, then re-run drift check.
    exit /b 1
)

echo.
echo [tt-drift] Checking for regen drift...
for /f "delims=" %%f in ('git diff --name-only -- ILGPU/ ILGPU.Algorithms/ ":!ILGPU/obj" ":!ILGPU/bin" ":!ILGPU.Algorithms/obj" ":!ILGPU.Algorithms/bin"') do (
    set drifted=1
    echo [tt-drift] DRIFT: %%f
)

if defined drifted (
    echo.
    echo [tt-drift] T4 template drift detected.
    echo [tt-drift] Root cause: a .cs file in ILGPU/ was edited manually but the
    echo [tt-drift] matching .tt template wasn't updated. T4 regenerated the .cs
    echo [tt-drift] without your manual edit. Local incremental builds passed
    echo [tt-drift] because T4 was skipped; CI's clean build runs T4 fresh.
    echo.
    echo [tt-drift] Fix: port the manual edit to the matching .tt, regen the .cs
    echo [tt-drift] (this script just regenerated them), commit BOTH together.
    echo.
    echo [tt-drift] See Docs/development.md for the full pattern.
    exit /b 1
)

echo [tt-drift] OK - no drift. Generated .cs files match committed state.
exit /b 0
