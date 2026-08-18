@echo off
REM Local equivalent of the CI "Fork Version Sync Check" workflow.
REM Run before pushing if you've touched ILGPU/, ILGPU.Algorithms/, or
REM SpawnDev.ILGPU/SpawnDev.ILGPU.csproj's PackageReferences.
REM
REM What it does: validates that the four ILGPU package versions are in
REM lockstep. If you bump ILGPU.csproj's <Version> but forget to bump
REM the matching SpawnDev.ILGPU.Fork PackageReference inside
REM SpawnDev.ILGPU.csproj, this check fails. Without the check, the
REM published wrapper rc.N would still pull the OLD Fork transitively
REM and the fix would be invisible to consumers (exactly what bit rc.28
REM on 2026-04-28).
REM
REM Same check CI runs - catching it locally saves a CI round-trip.

setlocal
cd /d "%~dp0"

dotnet run _check-fork-version-sync.cs
exit /b %ERRORLEVEL%
