# GooseDeluxe installer.
# Finds every Desktop Goose on this PC (or unpacks its .rar), installs the mod into all of them, enables
# mods in config.ini, makes a Desktop shortcut, then starts the goose and waits until the mod reports that
# it is running (GooseDeluxe.status). If it doesn't, it says why and copies a report to the clipboard.
# Runs on Windows PowerShell 5.1 (the one every Windows 10/11 has). No admin rights needed.
#
# Parameters exist only so the logic can be tested outside Windows; a user never passes them.
param(
    [string]$DesktopPath = "",
    [string[]]$SearchRoots = @(),
    [string]$PayloadDir = "",
    [switch]$NoGui,
    [switch]$AutoYes,
    [switch]$NoLaunch,
    [int]$WaitSeconds = 150
)

$ErrorActionPreference = 'Stop'

$ScriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$LogPath = Join-Path $ScriptDir 'install-log.txt'
$ReportPath = Join-Path $ScriptDir 'install-report.txt'
$GooseFolderName = 'Гусь'
$ModName = 'GooseDeluxe'
$script:Report = New-Object System.Collections.Generic.List[string]

function Log([string]$msg) {
    $line = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '  ' + $msg
    Write-Host $line
    try { Add-Content -Path $LogPath -Value $line -Encoding UTF8 } catch { }
}

function Note([string]$msg) {
    # goes into the report the user can paste to us, and into the log
    $script:Report.Add($msg)
    Log ('report: ' + $msg)
}

$script:GuiOk = $false
if (-not $NoGui) {
    try { Add-Type -AssemblyName System.Windows.Forms; Add-Type -AssemblyName System.Drawing; $script:GuiOk = $true } catch { $script:GuiOk = $false }
}

function Show([string]$text, [string]$title = 'GooseDeluxe') {
    Log ("[INFO] " + $text.Replace("`n", ' | '))
    if ($script:GuiOk) {
        [void][System.Windows.Forms.MessageBox]::Show($text, $title, 'OK', 'Information')
    }
}

function ShowError([string]$text) {
    Log ("[ERROR] " + $text.Replace("`n", ' | '))
    if ($script:GuiOk) {
        [void][System.Windows.Forms.MessageBox]::Show($text, 'GooseDeluxe — ошибка', 'OK', 'Error')
    }
}

function Ask([string]$text, [string]$title = 'GooseDeluxe') {
    Log ("[ASK] " + $text.Replace("`n", ' | '))
    if ($AutoYes) { Log '[ASK] auto-yes'; return $true }
    if ($script:GuiOk) {
        $r = [System.Windows.Forms.MessageBox]::Show($text, $title, 'YesNo', 'Question')
        return ($r -eq [System.Windows.Forms.DialogResult]::Yes)
    }
    $answer = Read-Host ($text + ' [y/n]')
    return ($answer -match '^[yYдД]')
}

function Get-KnownFolder([string]$name, [string]$fallback) {
    try {
        $p = [Environment]::GetFolderPath($name)
        if ($p) { return $p }
    } catch { }
    return $fallback
}

function Get-DownloadsPath {
    try {
        $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders'
        $v = (Get-ItemProperty -Path $key -ErrorAction Stop).'{374DE290-123F-4565-9164-39C4925E467B}'
        if ($v) { return [Environment]::ExpandEnvironmentVariables($v) }
    } catch { }
    return (Join-Path $env:USERPROFILE 'Downloads')
}

function Find-Files([string[]]$roots, [string]$filter, [int]$depth) {
    $found = @()
    foreach ($root in $roots) {
        if (-not $root -or -not (Test-Path -LiteralPath $root)) { continue }
        Log ("searching " + $root + " for " + $filter)
        try {
            $items = Get-ChildItem -LiteralPath $root -Filter $filter -File -Recurse -Depth $depth -ErrorAction SilentlyContinue -Force
            if ($items) { $found += @($items) }
        } catch { }
    }
    return $found
}

function Pick-Goose($exes) {
    # Prefer a copy that still has its config next to it, then the most recently touched one.
    $ranked = $exes | Sort-Object -Property @{ Expression = { if (Test-Path -LiteralPath (Join-Path $_.DirectoryName 'config.ini')) { 0 } else { 1 } } },
                                            @{ Expression = { $_.LastWriteTime }; Descending = $true }
    return @($ranked)[0]
}

function Test-Under([string]$path, [string]$root) {
    $p = [IO.Path]::GetFullPath($path).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $r = [IO.Path]::GetFullPath($root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    return $p.StartsWith($r, [StringComparison]::OrdinalIgnoreCase)
}

function Same-Dir([string]$a, [string]$b) {
    return [string]::Equals([IO.Path]::GetFullPath($a).TrimEnd('\', '/'), [IO.Path]::GetFullPath($b).TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)
}

function Extract-Rar([string]$rar, [string]$dest) {
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    # Every tool runs with the destination as its working directory and gets the archive as a single
    # quoted argument: Start-Process joins -ArgumentList arrays with bare spaces, which breaks paths
    # like "...\Telegram Desktop\..." or "...\Рабочий стол\...".
    $quotedRar = '"' + $rar + '"'
    $tools = @()
    foreach ($pf in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if (-not $pf) { continue }
        $tools += @{ Name = '7-Zip';  Exe = (Join-Path $pf '7-Zip\7z.exe');      Args = ('x -y ' + $quotedRar) }
        $tools += @{ Name = 'WinRAR'; Exe = (Join-Path $pf 'WinRAR\WinRAR.exe'); Args = ('x -y -ibck ' + $quotedRar) }
        $tools += @{ Name = 'UnRAR';  Exe = (Join-Path $pf 'WinRAR\UnRAR.exe');  Args = ('x -y ' + $quotedRar) }
    }
    # Windows 11 ships a libarchive-based tar.exe that reads RAR5; older ones fail harmlessly.
    $tar = Get-Command tar -ErrorAction SilentlyContinue
    if ($tar) { $tools += @{ Name = 'tar'; Exe = $tar.Source; Args = ('-xf ' + $quotedRar) } }

    foreach ($t in $tools) {
        if (-not (Test-Path -LiteralPath $t.Exe)) { continue }
        Log ("extracting with " + $t.Name + ": " + $t.Exe)
        try {
            $p = Start-Process -FilePath $t.Exe -ArgumentList $t.Args -WorkingDirectory $dest -Wait -PassThru -NoNewWindow -ErrorAction Stop
            Log ("  exit code " + $p.ExitCode)
        } catch { Log ("  failed: " + $_.Exception.Message); continue }
        $exe = @(Find-Files @($dest) 'GooseDesktop.exe' 6)
        if ($exe.Count -gt 0) { return $true }
    }
    return $false
}

function Move-Contents([string]$from, [string]$to) {
    New-Item -ItemType Directory -Path $to -Force | Out-Null
    $sameDrive = [string]::Equals([IO.Path]::GetPathRoot([IO.Path]::GetFullPath($from)),
                                  [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($to)),
                                  [StringComparison]::OrdinalIgnoreCase)
    foreach ($item in Get-ChildItem -LiteralPath $from -Force) {
        $target = Join-Path $to $item.Name
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
        if ($sameDrive) {
            Move-Item -LiteralPath $item.FullName -Destination $to -Force
        } else {
            # Move-Item refuses to move folders between drives (D:\ -> C:\)
            Copy-Item -LiteralPath $item.FullName -Destination $to -Recurse -Force
            Remove-Item -LiteralPath $item.FullName -Recurse -Force
        }
    }
}

function Remove-EmptyDirs([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    foreach ($d in Get-ChildItem -LiteralPath $path -Directory -Force) { Remove-EmptyDirs $d.FullName }
    if (-not (Get-ChildItem -LiteralPath $path -Force | Select-Object -First 1)) { Remove-Item -LiteralPath $path -Force }
}

$DefaultConfig = @"
Version_DoNotEdit=1
EnableMods=True
SilenceSounds=False
Task_CanAttackMouse=True
AttackRandomly=False
UseCustomColors=False
GooseDefaultWhite=#ffffff
GooseDefaultOrange=#ffa500
GooseDefaultOutline=#d3d3d3
MinWanderingTimeSeconds=20
MaxWanderingTimeSeconds=40
FirstWanderTimeSeconds=20
"@ -replace "`r`n", "`n"

function Enable-Mods([string]$gooseDir) {
    $cfg = Join-Path $gooseDir 'config.ini'
    if (-not (Test-Path -LiteralPath $cfg)) {
        Log 'config.ini missing, writing defaults with mods enabled'
        [IO.File]::WriteAllText($cfg, $DefaultConfig, [System.Text.Encoding]::ASCII)
        return
    }
    $text = [IO.File]::ReadAllText($cfg)
    if ($text -match '(?m)^EnableMods=') {
        $text = [regex]::Replace($text, '(?m)^EnableMods=.*$', 'EnableMods=True')
    } else {
        $text = $text.TrimEnd("`r", "`n") + "`nEnableMods=True`n"
    }
    # The goose's own parser needs plain key=value lines and no BOM.
    [IO.File]::WriteAllText($cfg, $text, [System.Text.Encoding]::ASCII)
}

# ------------------------------------------------------------------ where the goose is

function Get-RunningGeese {
    $list = @()
    foreach ($p in @(Get-Process -Name GooseDesktop -ErrorAction SilentlyContinue)) {
        $path = $null
        try { $path = $p.Path } catch { }
        $list += [pscustomobject]@{ Process = $p; Path = $path }
    }
    return $list
}

function Get-ShortcutTargets {
    # the goose the user actually starts may be behind a shortcut on the Desktop, in Start or on the taskbar
    $targets = @()
    $dirs = @($DesktopPath, (Get-KnownFolder 'CommonDesktopDirectory' ''), (Get-KnownFolder 'StartMenu' ''), (Get-KnownFolder 'CommonStartMenu' ''),
              [IO.Path]::Combine([string]$env:APPDATA, 'Microsoft', 'Internet Explorer', 'Quick Launch', 'User Pinned', 'TaskBar'))
    $shell = $null
    try { $shell = New-Object -ComObject WScript.Shell } catch { return $targets }
    foreach ($d in $dirs) {
        if (-not $d -or -not (Test-Path -LiteralPath $d)) { continue }
        foreach ($lnk in @(Get-ChildItem -LiteralPath $d -Filter '*.lnk' -File -Recurse -Depth 3 -ErrorAction SilentlyContinue)) {
            try {
                $t = $shell.CreateShortcut($lnk.FullName).TargetPath
                if ($t -and $t -like '*GooseDesktop.exe' -and (Test-Path -LiteralPath $t)) {
                    Log ("shortcut " + $lnk.FullName + " -> " + $t)
                    $targets += $t
                }
            } catch { }
        }
    }
    return $targets
}

# ------------------------------------------------------------------ install

function Install-Into([string]$gooseDir, [string]$dll, [string]$ini) {
    $modDir = [IO.Path]::Combine($gooseDir, 'Assets', 'Mods', $ModName)
    New-Item -ItemType Directory -Path $modDir -Force | Out-Null
    $target = Join-Path $modDir 'GooseDeluxe.dll'
    if (-not (Same-Dir (Split-Path -Parent $dll) $modDir)) {
        Copy-Item -LiteralPath $dll -Destination $target -Force
        if (-not (Test-Path -LiteralPath (Join-Path $modDir 'GooseDeluxe.ini'))) {
            Copy-Item -LiteralPath $ini -Destination (Join-Path $modDir 'GooseDeluxe.ini') -Force
        }
    }
    # 0.6.2 wrote EscHoldSeconds=1.5; the user asked for 3 s since, so the old default moves to the new one
    $userIni = Join-Path $modDir 'GooseDeluxe.ini'
    try {
        $iniText = [IO.File]::ReadAllText($userIni)
        if ($iniText -match '(?m)^EscHoldSeconds=1\.5\s*$') {
            [IO.File]::WriteAllText($userIni, [regex]::Replace($iniText, '(?m)^EscHoldSeconds=1\.5(\s*)$', 'EscHoldSeconds=3$1'), (New-Object System.Text.UTF8Encoding($true)))
        }
    } catch { }
    # 0.8 wrote PhraseVoice=Atomic (the Windows voice); the user wants the ready recordings instead
    try {
        $iniText = [IO.File]::ReadAllText($userIni)
        if ($iniText -match '(?m)^PhraseVoice=Atomic\s*$') {
            [IO.File]::WriteAllText($userIni, [regex]::Replace($iniText, '(?m)^PhraseVoice=Atomic(\s*)$', 'PhraseVoice=Records$1'), (New-Object System.Text.UTF8Encoding($true)))
        }
    } catch { }
    # ready voice lines travel in the zip's «Голос»: added next to the mod, the user's own files are kept
    $voiceFrom = Join-Path (Split-Path -Parent $dll) 'Голос'
    if ((Test-Path -LiteralPath $voiceFrom) -and -not (Same-Dir $voiceFrom (Join-Path $modDir 'Голос'))) {
        $voiceTo = Join-Path $modDir 'Голос'
        New-Item -ItemType Directory -Path $voiceTo -Force | Out-Null
        foreach ($f in Get-ChildItem -LiteralPath $voiceFrom -File) {
            $to = Join-Path $voiceTo $f.Name
            if (-not (Test-Path -LiteralPath $to)) { Copy-Item -LiteralPath $f.FullName -Destination $to -Force }
            try { Unblock-File -LiteralPath $to -ErrorAction Stop } catch { }
        }
    }
    # a downloaded zip marks its files as "from the internet"; the goose loads mods anyway, but clear it
    try { Unblock-File -LiteralPath $target -ErrorAction Stop } catch { }
    # the goose loads every DLL in a mod folder; a stray copy of the API there breaks the mod
    $strayApi = Join-Path $modDir 'GooseModdingAPI.dll'
    if (Test-Path -LiteralPath $strayApi) { Remove-Item -LiteralPath $strayApi -Force }
    Enable-Mods $gooseDir
    return $modDir
}

function Test-Installed([string]$modDir, [string]$wantHash) {
    $f = Join-Path $modDir 'GooseDeluxe.dll'
    if (-not (Test-Path -LiteralPath $f)) { return 'missing' }
    try { $h = (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash } catch { return ('unreadable: ' + $_.Exception.Message) }
    if ($h -ne $wantHash) { return 'old' }
    return 'ok'
}

# ------------------------------------------------------------------ diagnostics

function Get-AntivirusNames {
    try {
        $names = @(Get-CimInstance -Namespace 'root/SecurityCenter2' -ClassName AntiVirusProduct -ErrorAction Stop | ForEach-Object { $_.displayName })
        return ($names | Where-Object { $_ } | Sort-Object -Unique)
    } catch { return @() }
}

function Get-SystemInfo {
    $os = [Environment]::OSVersion.VersionString
    try { $os = (Get-CimInstance Win32_OperatingSystem -ErrorAction Stop).Caption + ' ' + $os } catch { }
    $net = '?'
    try { $net = [string](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction Stop).Release } catch { }
    return ($os + '; PowerShell ' + $PSVersionTable.PSVersion + '; .NET release ' + $net)
}

function Read-Status([string]$modDir) {
    $f = Join-Path $modDir 'GooseDeluxe.status'
    if (-not (Test-Path -LiteralPath $f)) { return $null }
    try { $line = [IO.File]::ReadAllText($f).Trim() } catch { return $null }
    $parts = $line.Split('|')
    if ($parts.Count -lt 3) { return $null }
    return @{ State = $parts[0]; Time = $parts[1]; Version = $parts[2]; Detail = $(if ($parts.Count -gt 3) { $parts[3] } else { '' }); Line = $line }
}

function Get-LogTail([string]$modDir, [int]$lines) {
    $f = Join-Path $modDir 'GooseDeluxe.log'
    if (-not (Test-Path -LiteralPath $f)) { return @('(журнала мода нет — мод ни разу не запускался в этой папке)') }
    try { return @(Get-Content -LiteralPath $f -Encoding UTF8 -Tail $lines) } catch { return @('(журнал не читается: ' + $_.Exception.Message + ')') }
}

function Copy-Report {
    $text = ($script:Report -join "`r`n")
    try { [IO.File]::WriteAllText($ReportPath, $text, (New-Object System.Text.UTF8Encoding($true))) } catch { }
    try { Set-Clipboard -Value $text -ErrorAction Stop; return $true } catch { }
    try { if ($script:GuiOk) { [System.Windows.Forms.Clipboard]::SetText($text); return $true } } catch { }
    return $false
}

function Get-SmartAppControl {
    try {
        $v = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -ErrorAction Stop).VerifiedAndReputablePolicyState
        switch ($v) { 0 { return 'выключен' } 1 { return 'ВКЛЮЧЁН' } 2 { return 'в режиме оценки' } default { return [string]$v } }
    } catch { return 'неизвестно' }
}

function Get-CrashEvents([datetime]$since) {
    # a .NET crash or a killed process leaves an entry in the Application log (readable without admin rights)
    $out = @()
    try {
        $evts = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $since } -ErrorAction Stop |
                  Where-Object { $_.Message -match 'GooseDesktop' } | Select-Object -First 3)
        foreach ($e in $evts) {
            $lines = @($e.Message -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -First 14)
            $out += ($e.ProviderName + ' ' + $e.Id + ': ' + ($lines -join ' | '))
        }
    } catch { }
    return $out
}

function Get-DefenderDetections {
    $out = @()
    try {
        $all = @(Get-MpThreatDetection -ErrorAction Stop | Where-Object { ($_.Resources -join ' ') -match 'Goose' })
        foreach ($x in ($all | Select-Object -Last 5)) {
            $name = ''
            try { $name = (Get-MpThreat -ThreatID $x.ThreatID -ErrorAction Stop).ThreatName } catch { }
            $out += ($name + ' ' + $x.InitialDetectionTime + ' ' + ($x.Resources -join ', '))
        }
    } catch { $out += ('(журнал Защитника не читается: ' + $_.Exception.Message + ')') }
    return $out
}

function Test-GooseWithoutMods([string]$gooseDir) {
    # the decisive check when the goose dies at start: does it live with mods switched off?
    $cfg = Join-Path $gooseDir 'config.ini'
    $orig = [IO.File]::ReadAllText($cfg)
    try {
        [IO.File]::WriteAllText($cfg, [regex]::Replace($orig, '(?m)^EnableMods=.*$', 'EnableMods=False'), [System.Text.Encoding]::ASCII)
        Log 'starting the goose without mods for a check'
        $p = Start-Process -FilePath (Join-Path $gooseDir 'GooseDesktop.exe') -WorkingDirectory $gooseDir -PassThru
        for ($i = 0; $i -lt 16 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 500 }
        if ($p.HasExited) { return ('закрылся сам, код ' + $p.ExitCode) }
        try { $p | Stop-Process -Force -ErrorAction SilentlyContinue } catch { }
        return 'работает'
    } catch { return ('не запустился: ' + $_.Exception.Message) }
    finally { [IO.File]::WriteAllText($cfg, $orig, [System.Text.Encoding]::ASCII) }
}

# ------------------------------------------------------------------ start the goose and wait for the mod

function Step-Wait($st) {
    $s = Read-Status $st.ModDir
    if ($s) {
        $st.Status = $s
        if ($s.State -eq 'hooked') { $st.Outcome = 'hooked'; return $true }
        if ($s.State -eq 'failed') { $st.Outcome = 'failed'; return $true }
        if ($s.State -eq 'started') { $st.Started = $true }
    }
    $p = $st.Process
    if ($p) {
        try { $p.Refresh() } catch { }
        if ($p.HasExited) { $st.Outcome = 'exited'; return $true }
        $title = ''
        try { $title = [string]$p.MainWindowTitle } catch { }
        if ($title -and -not $st.Titles.Contains($title)) { $st.Titles.Add($title) | Out-Null; Log ("goose window: " + $title) }
        $st.WarningOpen = ($title -eq 'Mod Enabler Warning')
        if ($st.WarningOpen) { $st.SawWarning = $true; $st.AnsweredAt = $null }
        if ($title -eq "Couldn't Load Mod") { $st.Outcome = 'loaderror'; return $true }
        if ($st.SawWarning -and -not $st.WarningOpen -and -not $st.Started) {
            # the question was answered, the goose runs, and our mod hasn't said a word
            if (-not $st.AnsweredAt) { $st.AnsweredAt = Get-Date }
            elseif (((Get-Date) - $st.AnsweredAt).TotalSeconds -gt 10) { $st.Outcome = 'noload'; return $true }
        }
    }
    $elapsed = ((Get-Date) - $st.StartedAt).TotalSeconds
    # The goose asks about mods before its own window appears, so on Windows the question is the
    # process's main window and we see it. Not seeing it for a minute means it never came.
    if (-not $st.SawWarning -and -not $st.Started -and $elapsed -gt 60 -and $p -and $st.CanSeeTitles) { $st.Outcome = 'noquestion'; return $true }
    # no time limit while the goose is asking about mods: the user may be reading it
    if (-not $st.WarningOpen -and $elapsed -gt $WaitSeconds) { $st.Outcome = 'timeout'; return $true }
    return $false
}

function Wait-Text($st) {
    $s = [int]((Get-Date) - $st.StartedAt).TotalSeconds
    if ($st.WarningOpen) {
        return "Гусь спрашивает про моды (окно «Mod Enabler Warning»).`n`nНажми в нём «Да» (Yes) — это разрешает мод."
    }
    if ($st.Started) { return "Мод загрузился, запускается… (" + $s + " с)" }
    return "Запускаю гуся и жду, пока мод отзовётся… (" + $s + " с)`n`nЕсли гусь спросит про моды — нажми «Да» (Yes)."
}

function Start-AndWait([string]$exePath, [string]$modDir) {
    $statusFile = Join-Path $modDir 'GooseDeluxe.status'
    if (Test-Path -LiteralPath $statusFile) { Remove-Item -LiteralPath $statusFile -Force -ErrorAction SilentlyContinue }
    $st = @{
        ModDir = $modDir; Process = $null; Outcome = ''; Status = $null; Started = $false
        SawWarning = $false; WarningOpen = $false; AnsweredAt = $null; StartedAt = (Get-Date)
        Titles = (New-Object System.Collections.ArrayList); CanSeeTitles = ($env:OS -eq 'Windows_NT')
    }
    Log ("starting " + $exePath)
    $st.Process = Start-Process -FilePath $exePath -WorkingDirectory (Split-Path -Parent $exePath) -PassThru

    $gui = $false
    if ($script:GuiOk) {
        try {
            $form = New-Object System.Windows.Forms.Form
            $form.Text = 'GooseDeluxe — проверка'
            $form.FormBorderStyle = 'FixedToolWindow'
            $form.StartPosition = 'Manual'
            $form.ClientSize = New-Object System.Drawing.Size(420, 110)
            $wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
            $form.Location = New-Object System.Drawing.Point(($wa.Right - 440), ($wa.Bottom - 150))
            $form.TopMost = $true
            $label = New-Object System.Windows.Forms.Label
            $label.Dock = 'Fill'
            $label.Padding = New-Object System.Windows.Forms.Padding(10)
            $label.Font = New-Object System.Drawing.Font('Segoe UI', 10)
            $label.Text = Wait-Text $st
            $form.Controls.Add($label)
            $timer = New-Object System.Windows.Forms.Timer
            $timer.Interval = 500
            $timer.Add_Tick({
                try {
                    if (Step-Wait $st) { $timer.Stop(); $form.Close() } else { $label.Text = Wait-Text $st }
                } catch { Log ("wait step failed: " + $_.Exception.Message); $st.Outcome = 'timeout'; $timer.Stop(); $form.Close() }
            })
            $form.Add_Shown({ $timer.Start() })
            [void]$form.ShowDialog()
            $timer.Dispose(); $form.Dispose()
            $gui = $true
        } catch { Log ("wait window failed, waiting without it: " + $_.Exception.Message) }
    }
    if (-not $gui -or -not $st.Outcome) {
        while (-not (Step-Wait $st)) { Start-Sleep -Milliseconds 500 }
    }
    Log ("wait result: " + $st.Outcome + $(if ($st.Status) { ' (' + $st.Status.Line + ')' } else { '' }))
    return $st
}

# ---------------------------------------------------------------------------------------------
try {
    Log '=== GooseDeluxe installer start ==='

    # 0. the mod files travel next to this script inside the zip
    if (-not $PayloadDir) { $PayloadDir = [IO.Path]::Combine($ScriptDir, 'Assets', 'Mods', $ModName) }
    $dll = Join-Path $PayloadDir 'GooseDeluxe.dll'
    $ini = Join-Path $PayloadDir 'GooseDeluxe.ini'
    if (-not (Test-Path -LiteralPath $dll)) {
        ShowError ("Рядом с установщиком нет файлов мода (" + $dll + ").`n`n" +
                   "Похоже, архив не распакован. Нажми на zip правой кнопкой → «Извлечь всё…», " +
                   "а потом запусти «Установить GooseDeluxe.bat» из распакованной папки.`n`n" +
                   "Если распаковал, а файла всё равно нет — его убрал антивирус.")
        exit 3
    }
    try { $want = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash }
    catch {
        ShowError ("Файл мода не читается: " + $_.Exception.Message + "`n`nЧаще всего так делает антивирус. Добавь распакованную папку в исключения антивируса и запусти установку ещё раз.")
        exit 3
    }
    $version = [string](Get-Item -LiteralPath $dll).VersionInfo.FileVersion
    if (-not $version) { $version = '?' }
    Note ("GooseDeluxe " + $version + ", отчёт установщика " + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
    Note ("Система: " + (Get-SystemInfo))
    $av = @(Get-AntivirusNames)
    Note ("Антивирус: " + $(if ($av.Count -gt 0) { $av -join ', ' } else { 'не определён' }))

    if (-not $DesktopPath) { $DesktopPath = Get-KnownFolder 'Desktop' (Join-Path $env:USERPROFILE 'Desktop') }
    $Downloads = Get-DownloadsPath
    $Documents = Get-KnownFolder 'MyDocuments' (Join-Path $env:USERPROFILE 'Documents')
    $GooseHome = Join-Path $DesktopPath $GooseFolderName
    Log ("desktop: " + $DesktopPath)

    # 1. find every goose: the running one, the ones shortcuts point at, the usual folders
    $running = @(Get-RunningGeese)
    $candidates = New-Object System.Collections.Generic.List[string]
    $mainHint = $null
    foreach ($r in $running) {
        if ($r.Path) {
            Note ("Гусь запущен отсюда: " + $r.Path)
            $candidates.Add($r.Path)
            if (-not $mainHint) { $mainHint = $r.Path }
        } else { Note "Гусь запущен, но путь к нему не виден" }
    }
    foreach ($t in @(Get-ShortcutTargets)) { $candidates.Add($t) }
    if ($SearchRoots.Count -eq 0) {
        $SearchRoots = @($GooseHome, $DesktopPath, $Downloads, $Documents, $ScriptDir, (Split-Path -Parent $ScriptDir), $env:USERPROFILE)
    }
    foreach ($root in $SearchRoots) { foreach ($f in @(Find-Files @($root) 'GooseDesktop.exe' 5)) { $candidates.Add($f.FullName) } }
    if ($candidates.Count -eq 0) {
        $drives = @()
        try { $drives = Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Root -match '^[A-Z]:\\$' } | ForEach-Object { $_.Root } } catch { }
        if ($drives.Count -gt 0) {
            Log 'not in the usual places, scanning drives (this can take a minute)'
            foreach ($f in @(Find-Files $drives 'GooseDesktop.exe' 4)) { $candidates.Add($f.FullName) }
        }
    }

    # 2. no goose yet? look for the friend's .rar and unpack it into Desktop\Гусь
    if ($candidates.Count -eq 0) {
        $rars = @(Find-Files @($Downloads, $DesktopPath, $Documents, $ScriptDir, (Split-Path -Parent $ScriptDir)) '*.rar' 2 |
                  Where-Object { $_.Name -match 'goose' } | Sort-Object LastWriteTime -Descending)
        if ($rars.Count -gt 0) {
            $rar = @($rars)[0].FullName
            Log ("found archive: " + $rar)
            if (Extract-Rar $rar $GooseHome) {
                foreach ($f in @(Find-Files @($GooseHome) 'GooseDesktop.exe' 6)) { $candidates.Add($f.FullName) }
            } else {
                New-Item -ItemType Directory -Path $GooseHome -Force | Out-Null
                Show ("Нашёл архив с гусём:`n" + $rar + "`n`nно распаковать его сам не смог (нет WinRAR/7-Zip). " +
                      "Создал папку «" + $GooseFolderName + "» на рабочем столе — распакуй архив в неё " +
                      "(правой кнопкой по .rar → «Извлечь…») и запусти установку ещё раз.")
                try { Start-Process explorer.exe -ArgumentList ('"' + $GooseHome + '"') } catch { }
                try { Start-Process explorer.exe -ArgumentList ('/select,"' + $rar + '"') } catch { }
                exit 0
            }
        }
    }

    # unique existing copies, as folder paths
    $dirs = New-Object System.Collections.Generic.List[string]
    foreach ($c in $candidates) {
        if (-not $c -or -not (Test-Path -LiteralPath $c)) { continue }
        $d = Split-Path -Parent ([IO.Path]::GetFullPath($c))
        $dup = $false
        foreach ($x in $dirs) { if (Same-Dir $x $d) { $dup = $true } }
        if (-not $dup) { $dirs.Add($d) }
    }
    if ($dirs.Count -eq 0) {
        New-Item -ItemType Directory -Path $GooseHome -Force | Out-Null
        Show ("Не нашёл гуся (GooseDesktop.exe) на этом компьютере.`n`n" +
              "Создал папку «" + $GooseFolderName + "» на рабочем столе. Распакуй в неё архив от друга " +
              "(Desktop_Goose_v0.31.rar: правой кнопкой → «Извлечь…») и запусти установку ещё раз.")
        try { Start-Process explorer.exe -ArgumentList ('"' + $GooseHome + '"') } catch { }
        exit 0
    }

    # the main copy: the one that was running, else Desktop\Гусь, else the best-looking one
    $gooseDir = $null
    if ($mainHint) { $gooseDir = Split-Path -Parent $mainHint }
    if (-not $gooseDir) { foreach ($d in $dirs) { if (Same-Dir $d $GooseHome) { $gooseDir = $d } } }
    if (-not $gooseDir) {
        $exes = @($dirs | ForEach-Object { Get-Item -LiteralPath (Join-Path $_ 'GooseDesktop.exe') })
        $gooseDir = (Pick-Goose $exes).DirectoryName
    }
    Log ("main goose: " + $gooseDir)

    # 3. a running goose holds its mod DLLs open: close every one
    if ($running.Count -gt 0) {
        Log 'closing the running goose'
        foreach ($r in $running) { try { $r.Process | Stop-Process -Force -ErrorAction SilentlyContinue } catch { } }
        Start-Sleep -Milliseconds 1000
    }

    # 4. bring the main goose to the Desktop folder (only if it lives somewhere odd)
    # never move the folder the installer itself is running from (zip extracted inside the goose folder)
    if ((-not (Test-Under $gooseDir $DesktopPath)) -and (-not (Test-Under $ScriptDir $gooseDir)) -and -not $mainHint) {
        $canUseHome = (-not (Test-Path -LiteralPath $GooseHome)) -or
                      (-not (Get-ChildItem -LiteralPath $GooseHome -Force | Select-Object -First 1))
        if ($canUseHome -and (Ask ("Гусь найден здесь:`n" + $gooseDir + "`n`nПеренести его в папку «" + $GooseFolderName + "» на рабочем столе?"))) {
            $oldDir = $gooseDir
            $oldParent = Split-Path -Parent $gooseDir
            Move-Contents $gooseDir $GooseHome
            Remove-EmptyDirs $gooseDir
            # the .rar unpacks into "Desktop Goose v0.31\DesktopGoose v0.31"; drop the empty shells too
            if ((Split-Path -Leaf $oldParent) -match 'goose') { Remove-EmptyDirs $oldParent }
            $gooseDir = $GooseHome
            for ($i = 0; $i -lt $dirs.Count; $i++) { if (Same-Dir $dirs[$i] $oldDir) { $dirs[$i] = $GooseHome } }
            Log ("moved goose to " + $gooseDir)
        }
    } elseif ((-not (Same-Dir $gooseDir $GooseHome)) -and (Test-Under $gooseDir $GooseHome)) {
        # unpacked straight from the .rar into Desktop\Гусь: flatten the nested folders
        $oldDir = $gooseDir
        Move-Contents $gooseDir $GooseHome
        Remove-EmptyDirs (Join-Path $GooseHome (Split-Path -Leaf (Split-Path -Parent $gooseDir)))
        Remove-EmptyDirs $gooseDir
        $gooseDir = $GooseHome
        for ($i = 0; $i -lt $dirs.Count; $i++) { if (Same-Dir $dirs[$i] $oldDir) { $dirs[$i] = $GooseHome } }
        Log ("flattened goose into " + $gooseDir)
    }

    # 5. install into every copy, the main one first: whichever the user starts, the mod is there
    $ordered = New-Object System.Collections.Generic.List[string]
    $ordered.Add($gooseDir)
    foreach ($d in $dirs) { if (-not (Same-Dir $d $gooseDir) -and (Test-Path -LiteralPath (Join-Path $d 'GooseDesktop.exe'))) { $ordered.Add($d) } }
    $modDirs = @{}
    foreach ($d in $ordered) {
        try {
            $modDirs[$d] = Install-Into $d $dll $ini
            Note ("Мод поставлен: " + $d)
        } catch {
            Note ("Не смог поставить мод в " + $d + ": " + $_.Exception.Message)
            if (Same-Dir $d $gooseDir) { throw }
        }
    }
    $modDir = $modDirs[$gooseDir]

    # 6. did the files stay? an antivirus may take a new DLL away a moment after it is written
    Start-Sleep -Seconds 2
    $check = Test-Installed $modDir $want
    Note ("Проверка файла мода: " + $check)
    if ($check -ne 'ok') {
        $why = if ($check -eq 'missing') { "Файл мода исчез из папки гуся сразу после установки — его удалил антивирус" }
               elseif ($check -eq 'old') { "В папке гуся осталась старая версия мода (файл занят)" }
               else { "Файл мода не читается (" + $check + ") — его заблокировал антивирус" }
        [void](Copy-Report)
        ShowError ($why + ".`n`n" + $modDir + "`n`n" +
                   $(if ($av.Count -gt 0) { "Антивирус: " + ($av -join ', ') + ". " } else { "" }) +
                   "Добавь папку гуся в исключения антивируса и запусти установку ещё раз.`n`nОтчёт скопирован — вставь его в чат (Ctrl+V).")
        exit 3
    }
    $cfgText = [IO.File]::ReadAllText((Join-Path $gooseDir 'config.ini'))
    if ($cfgText -notmatch '(?m)^EnableMods=True') {
        ShowError "Не получилось включить моды в config.ini гуся. Открой его Блокнотом и поставь EnableMods=True."
        exit 3
    }

    # 7. desktop shortcut to the main copy
    $shortcut = Join-Path $DesktopPath ($GooseFolderName + '.lnk')
    try {
        $ws = New-Object -ComObject WScript.Shell
        $sc = $ws.CreateShortcut($shortcut)
        $sc.TargetPath = (Join-Path $gooseDir 'GooseDesktop.exe')
        $sc.WorkingDirectory = $gooseDir
        $sc.IconLocation = (Join-Path $gooseDir 'GooseDesktop.exe') + ',0'
        $sc.Description = 'Desktop Goose + GooseDeluxe'
        $sc.Save()
        Log ("shortcut: " + $shortcut)
    } catch { Log ("shortcut failed: " + $_.Exception.Message) }

    $howTo = "Меню гуся: нажми на гуся ПРАВОЙ кнопкой мыши. Ещё: значок гуся у часов (может прятаться под стрелкой ^) или Ctrl+Alt+M.`nКучи листьев убираются кликом, а левый клик по гусю — гудок."
    if ($NoLaunch) {
        Show ("Готово! GooseDeluxe " + $version + " установлен в:`n" + ($ordered -join "`n") + "`n`n" + $howTo)
        Log '=== done (not started) ==='
        exit 0
    }

    # 8. start the goose and wait until the mod says it runs
    $exePath = Join-Path $gooseDir 'GooseDesktop.exe'
    $attempt = 0
    while ($true) {
        $attempt++
        $st = Start-AndWait $exePath $modDir
        if ($st.Outcome -eq 'noload' -and $attempt -lt 3) {
            if (Ask ("Гусь запустился, но без мода.`n`nСкорее всего, в окне «Mod Enabler Warning» нажали «Нет». Перезапустить гуся? В этот раз нажми «Да» (Yes).")) {
                try { $st.Process | Stop-Process -Force -ErrorAction SilentlyContinue } catch { }
                Start-Sleep -Milliseconds 800
                continue
            }
        }
        break
    }

    if ($st.Outcome -eq 'hooked') {
        Log ('=== done: the mod runs (' + $st.Status.Line + ') ===')
        Show ("Готово! Мод работает ✔  (GooseDeluxe " + $st.Status.Version + ")`n`nГусь: " + $gooseDir + "`n`n" + $howTo)
        exit 0
    }

    # 9. it didn't work: say why as well as we can, and hand over a report
    Note ("Итог ожидания: " + $st.Outcome + $(if ($st.Status) { ' — ' + $st.Status.Line } else { ' — статуса от мода нет' }))
    $withoutMods = ''
    if ($st.Outcome -eq 'exited') {
        try { Note ("Код выхода гуся: " + $st.Process.ExitCode + ", прошло " + [int]($st.Process.ExitTime - $st.StartedAt).TotalSeconds + " с") } catch { }
        Note ("GooseDesktop.exe на месте: " + (Test-Path -LiteralPath $exePath))
        Note ("Smart App Control: " + (Get-SmartAppControl))
        foreach ($e in @(Get-CrashEvents $st.StartedAt.AddSeconds(-5))) { Note ("Журнал Windows: " + $e) }
        foreach ($d in @(Get-DefenderDetections)) { Note ("Защитник: " + $d) }
        if (Test-Path -LiteralPath $exePath) {
            $withoutMods = Test-GooseWithoutMods $gooseDir
            Note ("Гусь без мода: " + $withoutMods)
        }
    }
    Note ("Окна гуся: " + $(if ($st.Titles.Count -gt 0) { ($st.Titles -join ', ') } else { 'не видно' }))
    Note ("Файл мода сейчас: " + (Test-Installed $modDir $want))
    try { Note ("Папка мода: " + ((Get-ChildItem -LiteralPath $modDir -Force | ForEach-Object { $_.Name + ' (' + $_.Length + ')' }) -join ', ')) } catch { }
    try { Note ("config.ini: " + (([IO.File]::ReadAllLines((Join-Path $gooseDir 'config.ini')) | Where-Object { $_ -match '^EnableMods' }) -join ' ')) } catch { }
    foreach ($r in @(Get-RunningGeese)) { Note ("Сейчас запущен: " + $r.Path) }
    Note "Журнал мода (последние строки):"
    foreach ($l in @(Get-LogTail $modDir 15)) { Note ("  " + $l) }
    $copied = Copy-Report
    $tail = "`n`n" + $(if ($copied) { "Отчёт скопирован — вставь его в чат со мной (Ctrl+V), я разберусь." } else { "Отчёт лежит здесь: " + $ReportPath })

    $msg = switch ($st.Outcome) {
        'failed'     { "Мод запустился, но споткнулся: " + $st.Status.Detail + "`nГусь работает по-старому." }
        'loaderror'  { "Гусь показал ошибку «Couldn't Load Mod» — он не смог загрузить мод." }
        'exited'     {
            if (-not (Test-Path -LiteralPath $exePath)) { "Гусь закрылся сразу после запуска, а файл GooseDesktop.exe пропал — его удалил Защитник Windows." }
            elseif ($withoutMods -eq 'работает') { "Гусь закрылся сразу после запуска с модом, а без мода работает — значит, дело в моде. Пришли отчёт, я починю." }
            elseif ($withoutMods) { "Гусь закрывается сразу после запуска даже без мода (" + $withoutMods + ") — его останавливает Windows или он сломан." }
            else { "Гусь закрылся сразу после запуска." }
        }
        'noload'     { "Гусь работает, но мод не загрузился (в окне про моды нажали «Нет», или гусь не видит мод)." }
        'noquestion' { "Гусь не спросил про моды (окна «Mod Enabler Warning» не было), и мод не отозвался." }
        default      { "Не дождался ответа от мода. Если гусь спрашивал про моды, а «Да» не нажато — запусти установку ещё раз." }
    }
    if ((Test-Installed $modDir $want) -eq 'missing') { $msg = "Файл мода пропал из папки гуся — его удалил антивирус" + $(if ($av.Count -gt 0) { " (" + ($av -join ', ') + ")" } else { "" }) + ". Добавь папку гуся в исключения." }
    ShowError ($msg + "`n`nГусь: " + $gooseDir + $tail)
    exit 2
}
catch {
    Note ("Ошибка: " + $_.Exception.Message)
    [void](Copy-Report)
    ShowError ("Что-то пошло не так:`n" + $_.Exception.Message + "`n`nОтчёт скопирован — вставь его в чат (Ctrl+V). Подробности в файле:`n" + $LogPath)
    Log $_.ScriptStackTrace
    exit 3
}
