<#
.SYNOPSIS
    Abre DigitalDeck, compilando antes si el código cambió desde la última vez.

.DESCRIPTION
    Se abre con "Iniciar DigitalDeck.bat", en la raíz del proyecto.

    Un botón, una decisión: el panel de desarrollo tiene seis acciones porque sirve para trabajar
    sobre el proyecto. Esto no; esto es para usar la app. Lo único que hay que resolver es que lo
    que se abre sea la versión de ahora y no la del mes pasado, y eso el lanzador lo mira solo
    comparando las fechas de los archivos fuente contra la del .exe compilado.

    Compilar cuando no hace falta cuesta quince segundos cada vez que querés abrir el deck, así
    que si no hay nada nuevo lanza directo.
#>

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$root = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $root 'src\DigitalDeck.App\bin\Debug\net8.0-windows\DigitalDeck.exe'

# ---------- Paleta, la misma de la app ----------

$colBack = [System.Drawing.Color]::FromArgb(11, 16, 19)
$colSurface = [System.Drawing.Color]::FromArgb(20, 27, 31)
$colText = [System.Drawing.Color]::FromArgb(228, 233, 236)
$colDim = [System.Drawing.Color]::FromArgb(140, 152, 160)
$colAccent = [System.Drawing.Color]::FromArgb(51, 201, 220)
$colOk = [System.Drawing.Color]::FromArgb(95, 216, 143)
$colWarn = [System.Drawing.Color]::FromArgb(234, 181, 76)

# ---------- Ventana ----------

$form = New-Object System.Windows.Forms.Form
$form.Text = 'Iniciar DigitalDeck'
$form.Size = New-Object System.Drawing.Size(520, 452)
$form.StartPosition = 'CenterScreen'
$form.BackColor = $colBack
$form.Font = New-Object System.Drawing.Font('Segoe UI', 9)
$form.FormBorderStyle = 'FixedSingle'
$form.MaximizeBox = $false

$title = New-Object System.Windows.Forms.Label
$title.Text = 'DIGITALDECK'
$title.ForeColor = $colText
$title.Font = New-Object System.Drawing.Font('Segoe UI', 12, [System.Drawing.FontStyle]::Bold)
$title.Location = New-Object System.Drawing.Point(20, 18)
$title.Size = New-Object System.Drawing.Size(300, 24)
$form.Controls.Add($title)

$pathLabel = New-Object System.Windows.Forms.Label
$pathLabel.Text = $root
$pathLabel.ForeColor = $colDim
$pathLabel.Font = New-Object System.Drawing.Font('Segoe UI', 8)
$pathLabel.Location = New-Object System.Drawing.Point(20, 42)
$pathLabel.Size = New-Object System.Drawing.Size(470, 16)
$form.Controls.Add($pathLabel)

# ---------- Estado ----------

$statusBox = New-Object System.Windows.Forms.Panel
$statusBox.Location = New-Object System.Drawing.Point(20, 70)
$statusBox.Size = New-Object System.Drawing.Size(466, 62)
$statusBox.BackColor = $colSurface
$form.Controls.Add($statusBox)

function New-StatusLabel($x, $caption) {
    $head = New-Object System.Windows.Forms.Label
    $head.Text = $caption
    $head.ForeColor = $colDim
    $head.Font = New-Object System.Drawing.Font('Segoe UI', 8)
    $head.Location = New-Object System.Drawing.Point($x, 10)
    $head.Size = New-Object System.Drawing.Size(210, 16)
    $statusBox.Controls.Add($head)

    $value = New-Object System.Windows.Forms.Label
    $value.ForeColor = $colText
    $value.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
    $value.Location = New-Object System.Drawing.Point($x, 28)
    $value.Size = New-Object System.Drawing.Size(210, 20)
    $statusBox.Controls.Add($value)

    return $value
}

$codeStatus = New-StatusLabel 16 'EL CÓDIGO'
$appStatus = New-StatusLabel 240 'LA APP'

# ---------- Log ----------

$log = New-Object System.Windows.Forms.TextBox
$log.Multiline = $true
$log.ScrollBars = 'Vertical'
$log.ReadOnly = $true
$log.BackColor = $colSurface
$log.ForeColor = $colDim
$log.Font = New-Object System.Drawing.Font('Consolas', 9)
$log.BorderStyle = 'None'
$log.Location = New-Object System.Drawing.Point(20, 246)
$log.Size = New-Object System.Drawing.Size(466, 132)
$form.Controls.Add($log)

function Write-Log($text) {
    $stamp = (Get-Date).ToString('HH:mm:ss')
    $log.AppendText("[$stamp] $text`r`n")
}

# ---------- Botón ----------

$go = New-Object System.Windows.Forms.Button
$go.Text = 'Abrir DigitalDeck'
$go.Location = New-Object System.Drawing.Point(20, 148)
$go.Size = New-Object System.Drawing.Size(466, 58)
$go.FlatStyle = 'Flat'
$go.FlatAppearance.BorderSize = 1
$go.FlatAppearance.BorderColor = $colAccent
$go.BackColor = $colAccent
$go.ForeColor = $colBack
$go.Font = New-Object System.Drawing.Font('Segoe UI', 12, [System.Drawing.FontStyle]::Bold)
$go.Cursor = [System.Windows.Forms.Cursors]::Hand
$form.Controls.Add($go)

$hint = New-Object System.Windows.Forms.Label
$hint.ForeColor = $colDim
$hint.Font = New-Object System.Drawing.Font('Segoe UI', 8)
$hint.Location = New-Object System.Drawing.Point(20, 212)
$hint.Size = New-Object System.Drawing.Size(466, 30)
$form.Controls.Add($hint)

$panelLink = New-Object System.Windows.Forms.LinkLabel
$panelLink.Text = 'Abrir el panel de desarrollo'
$panelLink.LinkColor = $colDim
$panelLink.ActiveLinkColor = $colAccent
$panelLink.Font = New-Object System.Drawing.Font('Segoe UI', 8)
$panelLink.Location = New-Object System.Drawing.Point(20, 386)
$panelLink.Size = New-Object System.Drawing.Size(190, 18)
$panelLink.Add_LinkClicked({ Start-Process (Join-Path $root 'Panel.bat') })
$form.Controls.Add($panelLink)

$dataLink = New-Object System.Windows.Forms.LinkLabel
$dataLink.Text = 'Abrir la carpeta de configuración'
$dataLink.LinkColor = $colDim
$dataLink.ActiveLinkColor = $colAccent
$dataLink.Font = New-Object System.Drawing.Font('Segoe UI', 8)
$dataLink.Location = New-Object System.Drawing.Point(266, 386)
$dataLink.Size = New-Object System.Drawing.Size(220, 18)
$dataLink.TextAlign = 'TopRight'
$dataLink.Add_LinkClicked({
    $data = Join-Path $env:APPDATA 'DigitalDeck'
    if (Test-Path $data) { Start-Process explorer.exe $data }
    else { Write-Log 'Todavía no existe: se crea la primera vez que abrís la app.' }
})
$form.Controls.Add($dataLink)

# ---------- Procesos y frescura ----------

function Get-App { Get-Process -Name 'DigitalDeck' -ErrorAction SilentlyContinue }

function Stop-App {
    $app = Get-App
    if (-not $app) { return }

    foreach ($process in $app) { $process.CloseMainWindow() | Out-Null }

    # La app guarda su posición al cerrarse, así que se le da margen para terminar sola antes de
    # forzarla; matarla de entrada le perdería el tamaño y la pantalla donde estaba.
    Start-Sleep -Milliseconds 900
    Stop-Process -Name 'DigitalDeck' -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 400
    Write-Log 'Cerré la copia que estaba abierta.'
}

# La fecha del fuente más nuevo. bin y obj quedan afuera: son salida del compilador y siempre son
# más nuevos que el .exe, así que incluirlos haría compilar en cada arranque.
function Get-NewestSourceTime {
    $files = Get-ChildItem -Path (Join-Path $root 'src') -Recurse -File -Include *.cs, *.xaml, *.csproj -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

    if (-not $files) { return $null }

    ($files | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
}

function Test-NeedsBuild {
    if (-not (Test-Path $exePath)) { return $true }

    $newest = Get-NewestSourceTime
    if ($null -eq $newest) { return $false }

    return $newest -gt (Get-Item $exePath).LastWriteTimeUtc
}

function Update-Status {
    $app = Get-App
    if ($app) {
        $appStatus.Text = "abierta (pid $($app[0].Id))"
        $appStatus.ForeColor = $colOk
        $go.Text = 'Reiniciar DigitalDeck'
    } else {
        $appStatus.Text = 'cerrada'
        $appStatus.ForeColor = $colDim
        $go.Text = 'Abrir DigitalDeck'
    }

    if (-not (Test-Path $exePath)) {
        $codeStatus.Text = 'sin compilar todavía'
        $codeStatus.ForeColor = $colWarn
        $hint.Text = 'La primera vez hay que compilar. Tarda un rato; después arranca al toque.'
        return
    }

    if (Test-NeedsBuild) {
        $codeStatus.Text = 'hay cambios nuevos'
        $codeStatus.ForeColor = $colWarn
        $hint.Text = 'Cambió el código desde la última compilación: se compila primero y después abre.'
    } else {
        $codeStatus.Text = 'al día'
        $codeStatus.ForeColor = $colOk
        $compiled = (Get-Item $exePath).LastWriteTime.ToString('dd/MM HH:mm')
        $hint.Text = "Compilado el $compiled. No hay nada nuevo, así que abre directo."
    }
}

# ---------- Lanzar ----------

$script:closeTimer = New-Object System.Windows.Forms.Timer
$script:closeTimer.Interval = 1400
$script:closeTimer.Add_Tick({ $script:closeTimer.Stop(); $form.Close() })

function Start-App {
    if (-not (Test-Path $exePath)) {
        Write-Log 'No hay .exe para abrir.'
        $go.Enabled = $true
        Update-Status
        return
    }

    Start-Process -FilePath $exePath
    Write-Log 'Listo, DigitalDeck está abriendo.'

    # El lanzador se va solo: ya hizo lo suyo y dejarlo en pantalla sería una ventana más que
    # cerrar a mano cada vez.
    $go.Text = 'Abriendo…'
    $script:closeTimer.Start()
}

# El build corre aparte y se vigila con un timer, porque esperarlo en el hilo de la ventana la
# dejaría congelada los diez o quince segundos que tarda, y una ventana congelada parece colgada.
$script:build = $null
$script:outFile = $null
$script:errFile = $null
$script:failed = $false

$buildTimer = New-Object System.Windows.Forms.Timer
$buildTimer.Interval = 250

$buildTimer.Add_Tick({
    if ($null -eq $script:build) { $buildTimer.Stop(); return }
    if (-not $script:build.HasExited) { return }

    $buildTimer.Stop()

    # WaitForExit sobre un proceso ya terminado vuelve enseguida, y es lo que puebla ExitCode:
    # leerlo directo después de HasExited devuelve $null bastante seguido.
    $script:build.WaitForExit()
    $code = $script:build.ExitCode
    $script:build = $null

    $failures = @()

    foreach ($file in @($script:outFile, $script:errFile)) {
        if ((Test-Path $file) -and (Get-Item $file).Length -gt 0) {
            # dotnet escribe UTF-8; sin decirlo, Get-Content asume la codepage ANSI y los acentos
            # llegan rotos ("DuraciÃ³n").
            foreach ($line in (Get-Content $file -Encoding UTF8)) {
                $trimmed = $line.Trim()
                if ($trimmed -match ': error ') { $failures += $trimmed }
            }
        }
        Remove-Item $file -ErrorAction SilentlyContinue
    }

    $form.Cursor = [System.Windows.Forms.Cursors]::Default

    if ($code -eq 0) {
        Write-Log 'Compilado.'
        Start-App
        return
    }

    Write-Log "La compilación falló (código $code)."

    # Sólo los errores: un build roto escupe cientos de líneas y las que importan son estas.
    foreach ($failure in ($failures | Select-Object -First 8)) { $log.AppendText("    $failure`r`n") }

    $script:failed = $true
    $go.Enabled = $true

    if (Test-Path $exePath) {
        Write-Log 'Podés abrir igual la última versión que sí compiló.'
        $go.Text = 'Abrir la última que compiló'
    } else {
        $go.Text = 'Reintentar'
    }
})

$go.Add_Click({
    $go.Enabled = $false
    $form.Cursor = [System.Windows.Forms.Cursors]::AppStarting

    # Después de un build fallido el botón cambia de significado: abre lo último que sí compiló,
    # sin volver a intentar lo que ya sabemos que no anda.
    if ($script:failed) {
        $script:failed = $false
        $form.Cursor = [System.Windows.Forms.Cursors]::Default

        if (Test-Path $exePath) {
            Stop-App
            Start-App
        } else {
            $go.Enabled = $true
            Update-Status
        }

        return
    }

    # Cerrar antes de compilar no es opcional: la app abierta tiene tomados sus propios DLL y el
    # build falla con MSB3027 sin decir por qué.
    Stop-App

    if (-not (Test-NeedsBuild)) {
        $form.Cursor = [System.Windows.Forms.Cursors]::Default
        Start-App
        return
    }

    Write-Log 'Hay cambios sin compilar. Compilando…'
    $go.Text = 'Compilando…'

    $script:outFile = [System.IO.Path]::GetTempFileName()
    $script:errFile = [System.IO.Path]::GetTempFileName()

    try {
        $script:build = Start-Process -FilePath 'dotnet' -ArgumentList @('build', '--nologo') `
            -WorkingDirectory $root -NoNewWindow -PassThru `
            -RedirectStandardOutput $script:outFile -RedirectStandardError $script:errFile
    } catch {
        Write-Log 'No encontré dotnet. Instalá el SDK de .NET 8, o abrí la última versión compilada.'
        $script:failed = $true
        $go.Enabled = $true
        $form.Cursor = [System.Windows.Forms.Cursors]::Default
        return
    }

    $buildTimer.Start()
})

# ---------- Arranque ----------

# Refresco flojo del estado, para que abrir o cerrar la app por afuera se note en la ventana.
# Después de un build fallido se queda quieto: el botón ahí dice "Abrir la última que compiló", y
# pisarlo con el texto de siempre dejaría un botón que promete una cosa y hace otra.
$refresh = New-Object System.Windows.Forms.Timer
$refresh.Interval = 2000
$refresh.Add_Tick({
    if ($null -eq $script:build -and $go.Enabled -and -not $script:failed) { Update-Status }
})
$refresh.Start()

Update-Status
Write-Log 'Listo para abrir.'

$form.Add_Shown({ $form.Activate(); $go.Focus() | Out-Null })
[void]$form.ShowDialog()
