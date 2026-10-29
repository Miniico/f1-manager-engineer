# F1 Manager Engineer — installer and launcher source

Source for the two components referenced by the antivirus false-positive notice:

- **Setup.cs**: the source of the unsigned .NET Windows installer, Setup.exe. It detects the game folder and invokes the local installation scripts included with the download.
- **StartReader.vbs**: starts the local Python reader in the background using the runtime path registered by the installation.

Current package: **0.6.15 beta**. [Download the complete mod and installation guide on OverTake](https://www.overtake.gg/downloads/f1-manager-engineer-in-game-development-and-setup-assistant-ue4ss.87653/).

This public repository contains these two sources for inspection. The full development repository is private. The complete release includes the scripts, runtime, mod files and third-party notices needed to install and use it.

The installer can be compiled with the Windows .NET Framework C# compiler, referencing System.Windows.Forms.dll, System.Drawing.dll and System.Web.Extensions.dll. The other release files are required to run it.
