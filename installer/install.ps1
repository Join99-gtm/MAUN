# GooseDeluxe installer.
# Finds Desktop Goose on this PC (or unpacks its .rar), moves it into a "Гусь" folder on the
# Desktop, installs the mod, enables mods in config.ini and creates a Desktop shortcut.
# Runs on Windows PowerShell 5.1 (the one every Windows 10/11 has). No admin rights needed.
#
# Parameters exist only so the logic can be tested outside Windows; a user never passes them.
param(
    [string]$DesktopPath = "",
    [string[]]$SearchRoots = @(),
    [string]$PayloadDir = "",
    [switch]$NoGui,
    [switch]$AutoYes,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

$ScriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$LogPath = Join-Path $ScriptDir 'install-log.txt'
$GooseFolderName = 'Гусь'
$ModName = 'GooseDeluxe'

function Log([string]$msg) {
    $line = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '  ' + $msg
    Write-Host $line
    try { Add-Content -Path $LogPath -Value $line -Encoding UTF8 } catch { }
}

$script:GuiOk = $false
if (-not $NoGui) {
    try { Add-Type -AssemblyName System.Windows.Forms; $script:GuiOk = $true } catch { $script:GuiOk = $false }
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
    Log 'EnableMods=True set in config.ini'
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
                   "а потом запусти «Установить GooseDeluxe.bat» из распакованной папки.")
        exit 1
    }

    if (-not $DesktopPath) { $DesktopPath = Get-KnownFolder 'Desktop' (Join-Path $env:USERPROFILE 'Desktop') }
    $Downloads = Get-DownloadsPath
    $Documents = Get-KnownFolder 'MyDocuments' (Join-Path $env:USERPROFILE 'Documents')
    $GooseHome = Join-Path $DesktopPath $GooseFolderName
    Log ("desktop: " + $DesktopPath)

    # 1. find the goose
    if ($SearchRoots.Count -eq 0) {
        $SearchRoots = @($GooseHome, $DesktopPath, $Downloads, $Documents, $ScriptDir, (Split-Path -Parent $ScriptDir), $env:USERPROFILE)
    }
    $exes = @()
    foreach ($root in $SearchRoots) {
        $exes = @(Find-Files @($root) 'GooseDesktop.exe' 5)
        if ($exes.Count -gt 0) { break }
    }
    if ($exes.Count -eq 0) {
        $drives = @()
        try { $drives = Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Root -match '^[A-Z]:\\$' } | ForEach-Object { $_.Root } } catch { }
        if ($drives.Count -gt 0) {
            Log 'not in the usual places, scanning drives (this can take a minute)'
            $exes = @(Find-Files $drives 'GooseDesktop.exe' 4)
        }
    }

    # 2. no goose yet? look for the friend's .rar and unpack it into Desktop\Гусь
    if ($exes.Count -eq 0) {
        $rars = @(Find-Files @($Downloads, $DesktopPath, $Documents, $ScriptDir, (Split-Path -Parent $ScriptDir)) '*.rar' 2 |
                  Where-Object { $_.Name -match 'goose' } | Sort-Object LastWriteTime -Descending)
        if ($rars.Count -gt 0) {
            $rar = @($rars)[0].FullName
            Log ("found archive: " + $rar)
            if (Extract-Rar $rar $GooseHome) {
                $exes = @(Find-Files @($GooseHome) 'GooseDesktop.exe' 6)
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

    if ($exes.Count -eq 0) {
        New-Item -ItemType Directory -Path $GooseHome -Force | Out-Null
        Show ("Не нашёл гуся (GooseDesktop.exe) на этом компьютере.`n`n" +
              "Создал папку «" + $GooseFolderName + "» на рабочем столе. Распакуй в неё архив от друга " +
              "(Desktop_Goose_v0.31.rar: правой кнопкой → «Извлечь…») и запусти установку ещё раз.")
        try { Start-Process explorer.exe -ArgumentList ('"' + $GooseHome + '"') } catch { }
        exit 0
    }

    $exe = Pick-Goose $exes
    $gooseDir = $exe.DirectoryName
    Log ("goose: " + $exe.FullName)

    # 3. a running goose holds its mod DLLs open
    $running = @(Get-Process -Name GooseDesktop -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        if (Ask "Гусь сейчас запущен. Закрыть его, чтобы установить мод?") {
            $running | Stop-Process -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 800
        } else {
            Show 'Тогда закрой гуся сам (подержи ESC несколько секунд) и запусти установку ещё раз.'
            exit 0
        }
    }

    # 4. bring the goose to the Desktop folder
    if (-not (Test-Under $gooseDir $DesktopPath)) {
        $canUseHome = (-not (Test-Path -LiteralPath $GooseHome)) -or
                      (-not (Get-ChildItem -LiteralPath $GooseHome -Force | Select-Object -First 1))
        if ($canUseHome -and (Ask ("Гусь найден здесь:`n" + $gooseDir + "`n`nПеренести его в папку «" + $GooseFolderName + "» на рабочем столе?"))) {
            $oldParent = Split-Path -Parent $gooseDir
            Move-Contents $gooseDir $GooseHome
            Remove-EmptyDirs $gooseDir
            # the .rar unpacks into "Desktop Goose v0.31\DesktopGoose v0.31"; drop the empty shells too
            if ((Split-Path -Leaf $oldParent) -match 'goose') { Remove-EmptyDirs $oldParent }
            $gooseDir = $GooseHome
            Log ("moved goose to " + $gooseDir)
        }
    } elseif ($gooseDir -ne $GooseHome -and (Test-Under $gooseDir $GooseHome)) {
        # unpacked straight from the .rar into Desktop\Гусь: flatten the nested folders
        Move-Contents $gooseDir $GooseHome
        Remove-EmptyDirs (Join-Path $GooseHome (Split-Path -Leaf (Split-Path -Parent $gooseDir)))
        Remove-EmptyDirs $gooseDir
        $gooseDir = $GooseHome
        Log ("flattened goose into " + $gooseDir)
    }

    # 5. install the mod
    $modDir = [IO.Path]::Combine($gooseDir, 'Assets', 'Mods', $ModName)
    New-Item -ItemType Directory -Path $modDir -Force | Out-Null
    Copy-Item -LiteralPath $dll -Destination (Join-Path $modDir 'GooseDeluxe.dll') -Force
    if (-not (Test-Path -LiteralPath (Join-Path $modDir 'GooseDeluxe.ini'))) {
        Copy-Item -LiteralPath $ini -Destination (Join-Path $modDir 'GooseDeluxe.ini') -Force
    }
    # the goose loads every DLL in a mod folder; a stray copy of the API there breaks the mod
    $strayApi = Join-Path $modDir 'GooseModdingAPI.dll'
    if (Test-Path -LiteralPath $strayApi) { Remove-Item -LiteralPath $strayApi -Force }
    Enable-Mods $gooseDir
    Log ("mod installed to " + $modDir)

    # 6. desktop shortcut
    $shortcut = Join-Path $DesktopPath ($GooseFolderName + '.lnk')
    $shortcutOk = $false
    try {
        $ws = New-Object -ComObject WScript.Shell
        $sc = $ws.CreateShortcut($shortcut)
        $sc.TargetPath = (Join-Path $gooseDir 'GooseDesktop.exe')
        $sc.WorkingDirectory = $gooseDir
        $sc.IconLocation = (Join-Path $gooseDir 'GooseDesktop.exe') + ',0'
        $sc.Description = 'Desktop Goose + GooseDeluxe'
        $sc.Save()
        $shortcutOk = $true
        Log ("shortcut: " + $shortcut)
    } catch { Log ("shortcut failed: " + $_.Exception.Message) }

    # 7. done
    $summary = "Готово! Гусь лежит здесь:`n" + $gooseDir + "`n`nМод установлен, моды в config.ini включены"
    if ($shortcutOk) { $summary += ", ярлык «" + $GooseFolderName + "» на рабочем столе создан" }
    $summary += ".`n`nПри запуске гусь спросит про моды — нажми «Да» (Yes)."
    if (-not $NoLaunch -and (Ask ($summary + "`n`nЗапустить гуся сейчас?"))) {
        Start-Process -FilePath (Join-Path $gooseDir 'GooseDesktop.exe') -WorkingDirectory $gooseDir
    } else {
        if ($NoLaunch) { Show $summary }
        try { Start-Process explorer.exe -ArgumentList ('"' + $gooseDir + '"') } catch { }
    }
    Log '=== done ==='
    exit 0
}
catch {
    ShowError ("Что-то пошло не так:`n" + $_.Exception.Message + "`n`nПодробности в файле:`n" + $LogPath)
    Log $_.ScriptStackTrace
    exit 1
}
