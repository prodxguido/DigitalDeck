<#
.SYNOPSIS
    Panel de control de DigitalDeck: compilar, ejecutar, testear e instalar el Remote Script.

.DESCRIPTION
    Se abre con Panel.bat, en la raíz del proyecto.

    Existe para tres cosas que en la consola se olvidan y cuestan una vuelta entera:

      * La app bloquea sus propios DLL. Compilar con DigitalDeck.exe abierto falla con MSB3027,
        así que el panel la cierra antes de compilar, siempre.
      * El puerto UDP 11001 lo toma la app. Con la app abierta, la suite de humo contra Ableton
        no puede escuchar, así que el panel también la cierra antes de correr esos tests.
      * Los Remote Scripts sólo se leen cuando Live arranca. Instalar con Live abierto deja
        código viejo en memoria, así que el panel avisa que hay que reiniciarlo.

    No compila ni ejecuta nada por su cuenta: todo lo hace llamando a dotnet, igual que a mano.
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
$colBad = [System.Drawing.Color]::FromArgb(232, 97, 95)

# ---------- Ventana ----------

$form = New-Object System.Windows.Forms.Form
$form.Text = 'DigitalDeck — Panel'
$form.Size = New-Object System.Drawing.Size(620, 610)
$form.StartPosition = 'CenterScreen'
$form.BackColor = $colBack
$form.Font = New-Object System.Drawing.Font('Segoe UI', 9)

$title = New-Object System.Windows.Forms.Label
$title.Text = 'DIGITALDECK'
$title.ForeColor = $colText
$title.Font = New-Object System.Drawing.Font('Segoe UI', 12, [System.Drawing.FontStyle]::Bold)
$title.Location = New-Object System.Drawing.Point(20, 16)
$title.Size = New-Object System.Drawing.Size(300, 24)
$form.Controls.Add($title)

$pathLabel = New-Object System.Windows.Forms.Label
$pathLabel.Text = $root
$pathLabel.ForeColor = $colDim
$pathLabel.Location = New-Object System.Drawing.Point(20, 40)
$pathLabel.Size = New-Object System.Drawing.Size(570, 18)
$form.Controls.Add($pathLabel)

# ---------- Estado ----------

$statusBox = New-Object System.Windows.Forms.Panel
$statusBox.Location = New-Object System.Drawing.Point(20, 68)
$statusBox.Size = New-Object System.Drawing.Size(566, 62)
$statusBox.BackColor = $colSurface
$form.Controls.Add($statusBox)

function New-StatusLabel($x, $caption) {
    $head = New-Object System.Windows.Forms.Label
    $head.Text = $caption
    $head.ForeColor = $colDim
    $head.Font = New-Object System.Drawing.Font('Segoe UI', 8)
    $head.Location = New-Object System.Drawing.Point($x, 10)
    $head.Size = New-Object System.Drawing.Size(180, 16)
    $statusBox.Controls.Add($head)

    $value = New-Object System.Windows.Forms.Label
    $value.ForeColor = $colText
    $value.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
    $value.Location = New-Object System.Drawing.Point($x, 28)
    $value.Size = New-Object System.Drawing.Size(180, 20)
    $statusBox.Controls.Add($value)

    return $value
}

$appStatus = New-StatusLabel 16 'LA APP'
$liveStatus = New-StatusLabel 200 'ABLETON LIVE'
$bridgeStatus = New-StatusLabel 384 'PUENTE OSC'

# ---------- Log ----------

$log = New-Object System.Windows.Forms.TextBox
$log.Multiline = $true
$log.ScrollBars = 'Vertical'
$log.ReadOnly = $true
$log.BackColor = $colSurface
$log.ForeColor = $colDim
$log.Font = New-Object System.Drawing.Font('Consolas', 9)
$log.BorderStyle = 'None'
$log.Location = New-Object System.Drawing.Point(20, 330)
$log.Size = New-Object System.Drawing.Size(566, 218)
$form.Controls.Add($log)

function Write-Log($text) {
    $stamp = (Get-Date).ToString('HH:mm:ss')
    $log.AppendText("[$stamp] $text`r`n")
}

# ---------- Botones ----------

$script:buttons = @()

function New-Action($x, $y, $w, $caption, $hint, $accent, $onClick) {
    $button = New-Object System.Windows.Forms.Button
    $button.Text = $caption
    $button.Location = New-Object System.Drawing.Point($x, $y)
    $button.Size = New-Object System.Drawing.Size($w, 46)
    $button.FlatStyle = 'Flat'
    $button.FlatAppearance.BorderSize = 1
    $button.Cursor = [System.Windows.Forms.Cursors]::Hand
    $button.TextAlign = 'MiddleCenter'

    if ($accent) {
        $button.BackColor = $colAccent
        $button.ForeColor = $colBack
        $button.Font = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold)
        $button.FlatAppearance.BorderColor = $colAccent
    } else {
        $button.BackColor = $colSurface
        $button.ForeColor = $colText
        $button.FlatAppearance.BorderColor = [System.Drawing.Color]::FromArgb(38, 48, 54)
    }

    if ($hint) {
        $tip = New-Object System.Windows.Forms.ToolTip
        $tip.SetToolTip($button, $hint)
    }

    $button.Add_Click($onClick)
    $form.Controls.Add($button)

    # Sin devolver el botón: nadie lo usa, y PowerShell escribiría el objeto entero a la salida.
    $script:buttons += $button
}

function Set-Busy($busy) {
    foreach ($button in $script:buttons) { $button.Enabled = -not $busy }
    if ($busy) { $form.Cursor = [System.Windows.Forms.Cursors]::AppStarting }
    else { $form.Cursor = [System.Windows.Forms.Cursors]::Default }
}

# ---------- Procesos ----------

function Get-App { Get-Process -Name 'DigitalDeck' -ErrorAction SilentlyContinue }
function Get-Live { Get-Process -Name 'Ableton Live*' -ErrorAction SilentlyContinue }

function Stop-App($quiet) {
    $app = Get-App
    if (-not $app) {
        if (-not $quiet) { Write-Log 'La app no estaba abierta.' }
        return $false
    }

    foreach ($process in $app) {
        $process.CloseMainWindow() | Out-Null
    }

    Start-Sleep -Milliseconds 700
    Stop-Process -Name 'DigitalDeck' -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 400
    Write-Log 'Cerré la app.'
    return $true
}

function Start-App {
    if (-not (Test-Path $exePath)) {
        Write-Log 'No existe el .exe todavía. Compilá primero.'
        return
    }

    Start-Process -FilePath $exePath
    Write-Log 'Lancé DigitalDeck.'
}

# Un comando por vez, con la salida volcada al log cuando termina. El timer evita que la ventana
# se congele mientras dotnet trabaja, que con un build son varios segundos.
$script:running = $null
$script:outFile = $null
$script:errFile = $null
$script:onDone = $null
$script:label = ''

$runTimer = New-Object System.Windows.Forms.Timer
$runTimer.Interval = 250

$runTimer.Add_Tick({
    if ($null -eq $script:running) { $runTimer.Stop(); return }
    if (-not $script:running.HasExited) { return }

    $runTimer.Stop()

    # WaitForExit sobre un proceso ya terminado vuelve enseguida, y es lo que hace que ExitCode
    # quede poblado: leerlo directo después de HasExited devuelve $null bastante seguido.
    $script:running.WaitForExit()
    $code = $script:running.ExitCode

    foreach ($file in @($script:outFile, $script:errFile)) {
        if ((Test-Path $file) -and (Get-Item $file).Length -gt 0) {
            # dotnet escribe UTF-8; sin decirlo, Get-Content asume la codepage ANSI y los acentos
            # llegan rotos ("DuraciÃ³n").
            foreach ($line in (Get-Content $file -Encoding UTF8)) {
                $trimmed = $line.Trim()
                if ($trimmed.Length -gt 0) { $log.AppendText("    $trimmed`r`n") }
            }
        }
        Remove-Item $file -ErrorAction SilentlyContinue
    }

    if ($code -eq 0) { Write-Log "$($script:label): OK" }
    else { Write-Log "$($script:label): falló (código $code)" }

    $script:running = $null
    Set-Busy $false

    $callback = $script:onDone
    $script:onDone = $null
    if ($callback) { & $callback $code }
})

function Start-Command($argList, $label, $filter, $onDone) {
    if ($script:running) { return }

    $script:label = $label
    $script:onDone = $onDone
    $script:outFile = [System.IO.Path]::GetTempFileName()
    $script:errFile = [System.IO.Path]::GetTempFileName()

    Write-Log "$label…"
    Set-Busy $true

    $script:running = Start-Process -FilePath 'dotnet' -ArgumentList $argList `
        -WorkingDirectory $root -NoNewWindow -PassThru `
        -RedirectStandardOutput $script:outFile -RedirectStandardError $script:errFile

    $runTimer.Start()
}

# ---------- Acciones ----------

New-Action 20 148 274 'Compilar y ejecutar' `
    'Cierra la app si está abierta (bloquea sus DLL), compila y la vuelve a lanzar.' $true {
    Stop-App $true | Out-Null
    Start-Command @('build') 'Compilando' $null {
        param($code)
        if ($code -eq 0) { Start-App }
    }
}

New-Action 312 148 274 'Ejecutar sin compilar' `
    'Lanza el último .exe compilado, sin pasar por dotnet build.' $false {
    Stop-App $true | Out-Null
    Start-App
}

New-Action 20 206 274 'Cerrar la app' `
    'Necesario antes de compilar, y antes de correr la suite de humo (la app toma el puerto 11001).' $false {
    Stop-App $false | Out-Null
}

New-Action 312 206 274 'Correr los tests' `
    'dotnet test. Los que necesitan Ableton se saltean solos.' $false {
    Start-Command @('test') 'Tests' $null $null
}

New-Action 20 264 274 'Suite de humo contra Live' `
    'Necesita Live abierto con DigitalDeck elegido como Control Surface. Cierra la app primero.' $false {
    if (-not (Get-Live)) {
        Write-Log 'Ableton Live no está abierto. Abrilo y activá DigitalDeck en Control Surface.'
        return
    }

    Stop-App $true | Out-Null
    $env:DIGITALDECK_LIVE = '1'
    Start-Command @('test', '--filter', 'FullyQualifiedName~AbletonSmoke') 'Suite de humo' $null $null
}

New-Action 312 264 274 'Instalar el Remote Script' `
    'Copia el script a la User Library de Ableton. Después hay que reiniciar Live.' $false {
    $script = Join-Path $root 'src\DigitalDeck.RemoteScript\install.py'
    $running = [bool](Get-Live)

    $arguments = @($script)
    if ($running) { $arguments += '--live-abierto' }

    $output = & python @arguments 2>&1
    foreach ($line in $output) {
        $trimmed = "$line".Trim()
        if ($trimmed.Length -gt 0) { $log.AppendText("    $trimmed`r`n") }
    }

    if ($running) {
        Write-Log 'Instalado. Live está abierto: reinicialo para que lo cargue.'
    } else {
        Write-Log 'Instalado. Abrí Live y elegí DigitalDeck en Control Surface.'
    }
}

$openFolder = New-Object System.Windows.Forms.LinkLabel
$openFolder.Text = 'Abrir la carpeta del proyecto'
$openFolder.LinkColor = $colDim
$openFolder.ActiveLinkColor = $colAccent
$openFolder.Location = New-Object System.Drawing.Point(20, 556)
$openFolder.Size = New-Object System.Drawing.Size(220, 20)
$openFolder.Add_LinkClicked({ Start-Process explorer.exe $root })
$form.Controls.Add($openFolder)

# ---------- Refresco del estado ----------

function Test-Port($port) {
    $lines = netstat -ano -p UDP | Select-String ":$port\s"
    return [bool]$lines
}

$statusTimer = New-Object System.Windows.Forms.Timer
$statusTimer.Interval = 2000

$refresh = {
    $app = Get-App
    if ($app) {
        $appStatus.Text = "corriendo ($($app[0].Id))"
        $appStatus.ForeColor = $colOk
    } else {
        $appStatus.Text = 'detenida'
        $appStatus.ForeColor = $colDim
    }

    if (Get-Live) {
        $liveStatus.Text = 'abierto'
        $liveStatus.ForeColor = $colOk
    } else {
        $liveStatus.Text = 'cerrado'
        $liveStatus.ForeColor = $colDim
    }

    # 11000 lo abre el Remote Script dentro de Live, 11001 la app. Los dos ocupados es el puente
    # completo; sólo uno significa que falta una de las dos mitades.
    $toLive = Test-Port 11000
    $fromLive = Test-Port 11001

    if ($toLive -and $fromLive) {
        $bridgeStatus.Text = 'conectado'
        $bridgeStatus.ForeColor = $colOk
    } elseif ($toLive) {
        $bridgeStatus.Text = 'falta la app'
        $bridgeStatus.ForeColor = $colWarn
    } elseif ($fromLive) {
        $bridgeStatus.Text = 'falta el script'
        $bridgeStatus.ForeColor = $colWarn
    } else {
        $bridgeStatus.Text = 'sin puente'
        $bridgeStatus.ForeColor = $colBad
    }
}

$statusTimer.Add_Tick($refresh)
$statusTimer.Start()

$form.Add_Shown({
    & $refresh
    Write-Log 'Panel listo.'
    if (-not (Test-Path $exePath)) { Write-Log 'Todavía no hay .exe compilado.' }
})

$form.Add_FormClosed({
    $statusTimer.Stop()
    $runTimer.Stop()
})

[void]$form.ShowDialog()
