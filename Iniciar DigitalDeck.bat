@echo off
REM Abre DigitalDeck, compilando antes si el codigo cambio. Doble clic para usarlo.
REM -STA lo necesita WinForms; -NoProfile evita que el perfil del usuario lo rompa.
powershell -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0herramientas\iniciar.ps1"
