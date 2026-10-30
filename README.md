# F1 Manager Engineer — installer and launcher source

Source for the two components referenced by the antivirus false-positive notice:

- **Setup.cs**: the source of the unsigned .NET Windows installer, Setup.exe. It detects the game folder and invokes the local installation scripts included with the download.
- **StartReader.vbs**: starts the local Python reader in the background using the runtime path registered by the installation.

Current package: **0.6.17 beta**. [Download the complete mod and installation guide on OverTake](https://www.overtake.gg/downloads/f1-manager-engineer-in-game-development-and-setup-assistant-ue4ss.87653/).

This public repository contains these two sources for inspection. The full development repository is private. The complete release includes the scripts, runtime, mod files and third-party notices needed to install and use it.

The installer can be compiled with the Windows .NET Framework C# compiler, referencing System.Windows.Forms.dll, System.Drawing.dll and System.Web.Extensions.dll. The other release files are required to run it.

## Installer action runner

Setup.exe now invokes this local script with explicit arguments. It is included in the ZIP; the source is reproduced here to keep this repository limited to its three approved files. Metadata is not a digital signature.

```powershell
# Visible, local entry point used by Setup.exe with powershell.exe -File.
# No source expressions or encoded commands are constructed from arguments.
param(
    [string]$Action,
    [string]$GameDir,
    [string]$Python
)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding
$OutputEncoding=[Console]::OutputEncoding
try {
    if(@('Install','Update','Uninstall') -cnotcontains $Action) { throw 'Unknown action' }
    $script=Join-Path $PSScriptRoot ($Action+'.ps1')
    if($Action -ceq 'Install') {
        & $script -GameDir $GameDir -Python $Python
        $succeeded=$?
    } else {
        & $script
        $succeeded=$?
    }
    if(-not $succeeded) { exit 1 }
    exit 0
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}

```
