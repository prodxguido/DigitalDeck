@echo off
REM Panel de control de DigitalDeck. Doble clic para abrirlo.
REM -STA lo necesita WinForms; -NoProfile evita que el perfil del usuario lo rompa.
powershell -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0herramientas\panel.ps1"
