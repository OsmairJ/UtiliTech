@echo off
rem Gera um .exe unico (com o .NET embutido) para levar em pendrive para outros PCs
cd /d "%~dp0"
rem EnableCompressionInSingleFile reduz o .exe pela metade; DebugType=none dispensa o .pdb
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "..\UtiliTech-dist"
echo.
echo Pronto: ..\UtiliTech-dist\UtiliTech.exe
pause
