// Standalone Windows Forms launcher. No Python or downloaded UI framework required.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Security.Cryptography;
using Microsoft.Win32;
using System.Reflection;

[assembly: AssemblyTitle("F1 Manager Engineer Setup")]
[assembly: AssemblyDescription("Local installer for the F1 Manager Engineer mod")]
[assembly: AssemblyCompany("Miniico")]
[assembly: AssemblyProduct("F1 Manager Engineer")]
[assembly: AssemblyVersion("0.6.16.0")]
[assembly: AssemblyFileVersion("0.6.16.0")]
[assembly: AssemblyInformationalVersion("0.6.16 beta")]

static class SetupCore {
    public const string Version = "0.6.16 beta";
    public static string Root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    // ProcessStartInfo.Arguments uses Windows argv quoting, not PowerShell source.
    // Double backslashes before quotes and before the final closing quote.
    public static string WindowsArgument(string value) {
        if(value==null)throw new ArgumentNullException("value");
        if(value.IndexOf('\0')>=0)throw new ArgumentException("Arguments cannot contain NUL");
        var quoted=new StringBuilder("\"");int slashes=0;
        foreach(char c in value) {
            if(c=='\\'){slashes++;continue;}
            if(c=='"'){quoted.Append('\\',slashes*2+1);quoted.Append(c);}
            else {quoted.Append('\\',slashes);quoted.Append(c);}
            slashes=0;
        }
        quoted.Append('\\',slashes*2);quoted.Append('"');return quoted.ToString();
    }
    static bool HasGame(string path) { return File.Exists(Path.Combine(path, "F1Manager24", "Binaries", "Win64", "F1Manager24.exe")); }
    public static string NormalizeGame(string path) {
        if(String.IsNullOrWhiteSpace(path))return "";
        try {
            string initial=Path.GetFullPath(path.Trim().Trim('"','\'').TrimEnd('\\','/'));
            if(Path.GetFileName(initial).Equals("F1Manager24.exe",StringComparison.OrdinalIgnoreCase))initial=Path.GetDirectoryName(initial);
            string candidate=initial;
            for(int level=0;level<6&&!String.IsNullOrEmpty(candidate);level++) {
                if(HasGame(candidate))return candidate;
                candidate=Path.GetDirectoryName(candidate);
            }
            foreach(string relative in new[]{@"steamapps\common\F1 Manager 2024",@"common\F1 Manager 2024","F1 Manager 2024"}) {
                candidate=Path.Combine(initial,relative);if(HasGame(candidate))return candidate;
            }
        }catch{}
        return "";
    }
    public static bool IsGame(string path) { return NormalizeGame(path)!=""; }
    public static string BundledPython {get{return Path.Combine(Root,"runtime","python","python.exe");}}
    static bool SamePath(string a,string b) {try{return !String.IsNullOrWhiteSpace(a)&&!String.IsNullOrWhiteSpace(b)&&String.Equals(Path.GetFullPath(a),Path.GetFullPath(b),StringComparison.OrdinalIgnoreCase);}catch{return false;}}
    static bool IsUnlinkedFile(string path,string bin) {
        string current=path;
        while(!String.IsNullOrEmpty(current)) {
            if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)return false;
            if(SamePath(current,bin))return true;
            current=Path.GetDirectoryName(current);
        }
        return false;
    }
    public static Dictionary<string,object> ReadObject(string path) {
        try { return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path)); }
        catch { return new Dictionary<string,object>(); }
    }
    public static string Get(Dictionary<string,object> obj,string key) { return obj.ContainsKey(key) ? Convert.ToString(obj[key]) : ""; }
    public static IEnumerable<string> SteamRoots() {
        var roots = new List<string>();
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        foreach (var hive in new[] {RegistryHive.CurrentUser,RegistryHive.LocalMachine})
        foreach (var view in new[] {RegistryView.Registry32,RegistryView.Registry64}) {
            try { using(var baseKey=RegistryKey.OpenBaseKey(hive,view)) using(var key=baseKey.OpenSubKey(@"Software\Valve\Steam")) {
                if(key!=null) foreach(var name in new[]{"SteamPath","InstallPath"}) { var v=key.GetValue(name) as string; if(!String.IsNullOrEmpty(v)) roots.Add(v); }
            }} catch {}
        }
        return roots.Where(p=>!String.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase);
    }
    public static IEnumerable<string> Libraries(string root) {
        var result = new List<string>{root};
        try {
            string vdf=File.ReadAllText(Path.Combine(root,"steamapps","libraryfolders.vdf"));
            foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(vdf,"\"path\"\\s+\"([^\"]+)\""))
                result.Add(match.Groups[1].Value.Replace(@"\\",@"\"));
        } catch {}
        return result;
    }
    public static string DetectGame() {
        var manifest=ReadObject(Path.Combine(Root,"installed-files.json"));
        string previous=NormalizeGame(Get(manifest,"gameDir")); if(previous!="") return previous;
        foreach(var library in SteamRoots().SelectMany(Libraries).Distinct(StringComparer.OrdinalIgnoreCase)) {
            string candidate=Path.Combine(library,"steamapps","common","F1 Manager 2024"); if(IsGame(candidate))return candidate;
        }
        return "";
    }
    public static IEnumerable<string> PythonCandidates() {
        var paths=new List<string>();
        paths.Add(BundledPython);
        string registered=Get(ReadObject(Path.Combine(Root,"installed-files.json")),"python"); if(registered!="")paths.Add(registered);
        foreach(var directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)) {
            try { paths.Add(Path.Combine(directory.Trim('"'),"python.exe")); } catch {}
        }
        foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})
        foreach(var view in new[]{RegistryView.Registry32,RegistryView.Registry64}) {
            try { using(var baseKey=RegistryKey.OpenBaseKey(hive,view)) using(var key=baseKey.OpenSubKey(@"Software\Python\PythonCore")) {
                if(key!=null) foreach(var version in key.GetSubKeyNames().OrderByDescending(x=>x)) using(var install=key.OpenSubKey(version+@"\InstallPath")) {
                    if(install!=null) {string path=install.GetValue("ExecutablePath") as string; if(String.IsNullOrEmpty(path))path=Path.Combine(Convert.ToString(install.GetValue("")),"python.exe"); paths.Add(path);}
                }
            }} catch {}
        }
        // The Python launcher resolves installations that are not on PATH.
        foreach(var directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)) {
            try { string launcher=Path.Combine(directory.Trim('"'),"py.exe"); if(!File.Exists(launcher))continue;
                var run=Run(launcher,"-3 -c \"import sys; print(sys.executable)\"",null,8000);
                if(run.Item1==0)paths.Add(run.Item2.Trim());
            } catch {}
        }
        return paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }
    public static Tuple<int,string> Run(string exe,string args,string stdin,int timeout) {
        var psi=new ProcessStartInfo(exe,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=Root};
        using(var p=new Process {StartInfo=psi}) {
            var output=new StringBuilder(); object gate=new object();
            DataReceivedEventHandler receive=(s,e)=>{if(e.Data!=null)lock(gate)output.AppendLine(e.Data);};
            p.OutputDataReceived+=receive;p.ErrorDataReceived+=receive;p.Start();p.BeginOutputReadLine();p.BeginErrorReadLine();
            if(stdin!=null)p.StandardInput.Write(stdin);p.StandardInput.Close();
            if(!p.WaitForExit(timeout)) {try{p.Kill();}catch{} return Tuple.Create(-1,"Operation timed out. No other processes were stopped.");}
            p.WaitForExit(); return Tuple.Create(p.ExitCode,output.ToString());
        }
    }
    public static string ProbePython(string path) {
        if(!File.Exists(path))return "Included runtime missing. Extract the complete ZIP again, including runtime\\python.";
        string check=Path.Combine(Root,"runtime_check.py");
        if(!File.Exists(check))return "Reader files missing. Extract the complete ZIP again.";
        try {
            var run=Run(path,"-B \""+check+"\"",null,15000);
            return run.Item1==0&&run.Item2.Trim().StartsWith("OK ") ? run.Item2.Trim() : "Reader check failed. Extract the complete ZIP again; Advanced can select another 64-bit Python 3.11+.";
        }catch{return "Reader runtime could not start. Extract the complete ZIP again, then use Check.";}
    }
    public static bool PackageReady(out string detail) {
        string listing=Path.Combine(Root,"FILE-HASHES.sha256");int verified=0;
        if(!File.Exists(listing)){detail="Package check: missing file list. Extract the COMPLETE release ZIP again.";return false;}
        string prefix=Path.GetFullPath(Root).TrimEnd('\\')+"\\";
        try {
            foreach(string line in File.ReadAllLines(listing)) {
                if(String.IsNullOrWhiteSpace(line))continue;
                if(line.Length<67||line.Substring(64,2)!="  ")throw new InvalidDataException();
                string file=Path.GetFullPath(Path.Combine(Root,line.Substring(66).Replace('/',Path.DirectorySeparatorChar)));
                if(!file.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!File.Exists(file))throw new InvalidDataException();
                using(var sha=SHA256.Create())using(var stream=File.OpenRead(file)) {
                    if(!BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").Equals(line.Substring(0,64),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException();
                }
                verified++;
            }
            if(verified<20)throw new InvalidDataException();
            detail="Package check: "+verified+" files verified";return true;
        }catch{detail="Package check failed: files are missing or changed. Extract the complete release ZIP again.";return false;}
    }
    static string FileHash(string path) {
        using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
    }
    public static string LoaderProfile(string bin) {
        // Profiles require a complete matched pair, independently of the hashes
        // in the installation record. Never infer compatibility from filenames.
        try {
            string proxy=Path.Combine(bin,"dwmapi.dll"),loader=Path.Combine(bin,@"ue4ss\UE4SS.dll");
            if(!IsUnlinkedFile(proxy,bin)||!IsUnlinkedFile(loader,bin))return "unknown";
            string proxyHash=FileHash(proxy),loaderHash=FileHash(loader),payload=Path.Combine(Root,"payload-v2");
            if(proxyHash.Equals(FileHash(Path.Combine(payload,"dwmapi.dll")),StringComparison.OrdinalIgnoreCase)
                &&loaderHash.Equals(FileHash(Path.Combine(payload,@"ue4ss\UE4SS.dll")),StringComparison.OrdinalIgnoreCase))return "bundled";
            if(proxyHash.Equals("598D95A170389A80CEEA52D9FA208DC55FC7A652A5283E440BC19A06BC17F31E",StringComparison.OrdinalIgnoreCase)
                &&loaderHash.Equals("DF9E6E9A2280972B1C28CE590700FEACC752B447204F8BAADEB95F5776957055",StringComparison.OrdinalIgnoreCase))return "season-2026-1.3";
        }catch{}
        return "unknown";
    }
    public static Tuple<bool,string> Readiness(string game,string python) {
        var lines=new StringBuilder();string package;bool valid=PackageReady(out package);lines.AppendLine(package);
        string normalized=NormalizeGame(game);if(normalized==""){valid=false;lines.AppendLine("Game folder missing. Use Detect or Browse; Steam > Manage > Browse local files shows the correct folder.");}
        string probe=ProbePython(python);lines.AppendLine(probe);if(!probe.StartsWith("OK "))valid=false;
        var record=ReadObject(Path.Combine(Root,"installed-files.json"));
        if(!record.ContainsKey("files")){lines.AppendLine("No installation record. Choose Install when the checks above pass.");return Tuple.Create(false,lines.ToString());}
        if(record.ContainsKey("schemaVersion")) {
            if(Get(record,"schemaVersion")!="2"||!new[]{"bundled","borrowed"}.Contains(Get(record,"loaderMode"))||!record.ContainsKey("borrowedDependencies"))valid=false;
        }else if(record.ContainsKey("borrowedDependencies")||record.ContainsKey("loaderMode"))valid=false;
        if(!String.Equals(normalized,NormalizeGame(Get(record,"gameDir")),StringComparison.OrdinalIgnoreCase)){valid=false;lines.AppendLine("The selected game differs from the installed game. Use Detect to select the registered game.");}
        if(!SamePath(python,Get(record,"python"))){valid=false;lines.AppendLine("The selected runtime differs from the registered reader. Update adopts the included runtime.");}
        var entries=record["files"] as System.Collections.IEnumerable;var owned=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int count=0;
        string bin=Path.Combine(normalized,"F1Manager24","Binaries","Win64");string binPrefix=Path.GetFullPath(bin).TrimEnd('\\')+"\\";
        if(entries==null)valid=false;else foreach(var item in entries) {
            var entry=item as Dictionary<string,object>;if(entry==null){valid=false;continue;}
            try {
                string path=Path.GetFullPath(Get(entry,"path"));
                if(!path.StartsWith(binPrefix,StringComparison.OrdinalIgnoreCase)||!IsUnlinkedFile(path,bin)||!owned.Add(path))throw new InvalidDataException();
                using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))if(!BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").Equals(Get(entry,"sha256"),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException();
                count++;
            }catch{valid=false;}
        }
        var dependencies=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int borrowedCount=0;
        if(record.ContainsKey("borrowedDependencies")) {
            var borrowed=record["borrowedDependencies"] as System.Collections.IEnumerable;
            var allowed=new HashSet<string>(new[]{"dwmapi.dll",@"ue4ss\UE4SS.dll",@"ue4ss\UE4SS-settings.ini",@"ue4ss\UE4SS_SDK_Backends\UE4SS.json",@"ue4ss\Mods\mods.txt",@"ue4ss\Mods\shared\UEHelpers\UEHelpers.lua",@"ue4ss\Mods\shared\ModMenu\ModMenu.lua"},StringComparer.OrdinalIgnoreCase);
            if(borrowed==null)valid=false;else foreach(var item in borrowed) {
                var entry=item as Dictionary<string,object>;
                try {
                    if(entry==null)throw new InvalidDataException();
                    string path=Path.GetFullPath(Get(entry,"path"));
                    if(!path.StartsWith(binPrefix,StringComparison.OrdinalIgnoreCase)||!allowed.Contains(path.Substring(binPrefix.Length))||!IsUnlinkedFile(path,bin)||owned.Contains(path)||!dependencies.Add(path))throw new InvalidDataException();
                    using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))if(!BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").Equals(Get(entry,"sha256"),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException();
                    borrowedCount++;
                }catch{valid=false;}
            }
            lines.AppendLine("Shared dependencies verified: "+borrowedCount+" (preserved by Update and Uninstall)");
        }
        foreach(string needed in new[]{"dwmapi.dll",@"ue4ss\UE4SS.dll"})if(!owned.Contains(Path.Combine(bin,needed))&&!dependencies.Contains(Path.Combine(bin,needed)))valid=false;
        foreach(string needed in new[]{@"ue4ss\Mods\F1ManagerEngineer\Scripts\main.lua",@"ue4ss\Mods\F1ManagerEngineer\Scripts\config.lua"})if(!owned.Contains(Path.Combine(bin,needed)))valid=false;
        if(Get(record,"loaderMode")=="borrowed")foreach(string needed in new[]{"dwmapi.dll",@"ue4ss\UE4SS.dll",@"ue4ss\UE4SS-settings.ini"})if(!dependencies.Contains(Path.Combine(bin,needed)))valid=false;
        if(Get(record,"loaderMode")=="bundled"&&new[]{"dwmapi.dll",@"ue4ss\UE4SS.dll"}.Any(p=>dependencies.Contains(Path.Combine(bin,p))))valid=false;
        string profile=LoaderProfile(bin);
        if(profile=="unknown") {valid=false;lines.AppendLine("Loader profile: unknown or mixed DLL pair. Restore a supported complete loader; existing files were preserved.");}
        else {
            lines.AppendLine((Get(record,"loaderMode")=="borrowed"?"Shared loader profile: ":"Loader profile: ")
                +(profile=="season-2026-1.3"?"2026 Season Update 1.3 (UE4SS 0196ef29)":"bundled pinned UE4SS pair"));
            if(profile!="bundled"&&Get(record,"loaderMode")!="borrowed") {valid=false;lines.AppendLine("The supported external loader must be recorded as shared dependencies. Choose Update to recheck ownership.");}
        }
        try {
            string payload=Path.Combine(Root,"payload-v2");
            foreach(string source in Directory.GetFiles(payload,"*",SearchOption.AllDirectories)) {
                string relative=source.Substring(payload.Length+1),target=Path.Combine(bin,relative);
                bool isMod=relative.StartsWith(@"ue4ss\Mods\F1ManagerEngineer\",StringComparison.OrdinalIgnoreCase);
                if(!owned.Contains(target)&&(isMod||!dependencies.Contains(target)))valid=false;
            }
        }catch{valid=false;}
        try {
            string config=File.ReadAllText(Path.Combine(bin,@"ue4ss\Mods\F1ManagerEngineer\Scripts\config.lua"));
            foreach(string localFile in new[]{@"data\snapshot.lua","StartReader.vbs"})if(!config.Contains(Path.Combine(Root,localFile).Replace('\\','/').Replace("'","\\'"))) {valid=false;lines.AppendLine("Reader paths point to another package folder. Choose Update from this permanent folder.");break;}
        }catch{valid=false;}
        lines.AppendLine("Installed files verified: "+count);
        foreach(string required in new[]{"StartReader.vbs","StartReader.ps1","bridge.py"})if(!File.Exists(Path.Combine(Root,required))){valid=false;lines.AppendLine("Reader launcher missing. Extract the complete ZIP again.");}
        string host=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","wscript.exe");
        if(!File.Exists(host)){valid=false;lines.AppendLine("Windows Script Host unavailable. The automatic reader cannot start; use StartReader.ps1 manually.");}
        lines.AppendLine(valid?"READY - loader, reader runtime and installation files verified. Open the game, load a career and press F8.":"NOT READY - resolve the checks above. Update rechecks shared dependencies and repairs unchanged Engineer files. Compatibility conflicts need review; do not delete another mod's loader.");
        lines.AppendLine("This offline check does not run the game or verify F8 on every game screen.");
        return Tuple.Create(valid,lines.ToString());
    }
    public static string PowerShellArguments(string action,string game,string python) {
        if(!new[]{"Install","Update","Uninstall"}.Contains(action))throw new ArgumentException("Unknown action");
        string arguments="-NoProfile -NonInteractive -InputFormat Text -OutputFormat Text -ExecutionPolicy Bypass -File "
            +WindowsArgument(Path.Combine(Root,"InstallerAction.ps1"))+" -Action "+WindowsArgument(action);
        if(action=="Install")arguments+=" -GameDir "+WindowsArgument(NormalizeGame(game))+" -Python "+WindowsArgument(python);
        return arguments;
    }
    public static Tuple<int,string> Execute(string action,string game,string python) {
        string ps=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe");
        return Run(ps,PowerShellArguments(action,game,python),null,120000);
    }
    public static string ReaderStatus(Dictionary<string,object> state,double now) {
        string code=Get(state,"code");
        if(code=="")return "no diagnostic status yet";
        double stamp;
        if(!Double.TryParse(Get(state,"time"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out stamp)
            ||Double.IsNaN(stamp)||Double.IsInfinity(stamp)||now-stamp<0||now-stamp>=15)
            return "inactive or stale (last status: "+code+")";
        return code+" (recent heartbeat)";
    }
    public static string Diagnostics(string game,string python) {
        var report=new StringBuilder("F1 Manager Engineer "+Version+Environment.NewLine);
        report.AppendLine("Reader runtime: "+(String.Equals(python,BundledPython,StringComparison.OrdinalIgnoreCase)?"included with this package":"external / advanced"));
        report.AppendLine("Game folder: "+(IsGame(game)?"valid":"not found"));
        report.AppendLine("Game process: "+(Process.GetProcessesByName("F1Manager24").Length>0?"running":"closed"));
        report.AppendLine(Readiness(game,python).Item2);
        var manifest=ReadObject(Path.Combine(Root,"installed-files.json"));
        report.AppendLine("Installation record: "+(manifest.ContainsKey("files")?"present":"not installed / unreadable"));
        int matching=0,changed=0,missing=0;
        if(manifest.ContainsKey("files")) {
            var entries=manifest["files"] as System.Collections.IEnumerable;
            if(entries!=null)foreach(var item in entries) {
                var entry=item as Dictionary<string,object>;if(entry==null)continue;
                string path=Get(entry,"path"),expected=Get(entry,"sha256");
                try {if(!File.Exists(path)){missing++;continue;}using(var hash=SHA256.Create())using(var stream=File.OpenRead(path)) {
                    string actual=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-",""); if(actual.Equals(expected,StringComparison.OrdinalIgnoreCase))matching++;else changed++;
                }} catch {changed++;}
            }
        }
        if(manifest.ContainsKey("files")&&matching==0&&changed==0&&missing>0)
            report.AppendLine("Installation is absent. The old record will be archived when you choose Install.");
        report.AppendLine(String.Format("Installed files: {0} verified, {1} changed/unreadable, {2} missing",matching,changed,missing));
        var state=ReadObject(Path.Combine(Root,"data","reader-state.json"));
        report.AppendLine("Reader status: "+ReaderStatus(state,(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds));
        report.AppendLine("Saved snapshot: "+(File.Exists(Path.Combine(Root,"data","snapshot.lua"))?"present (last successful output; freshness not confirmed)":"absent"));
        report.AppendLine("Report excludes paths, usernames, save contents and log excerpts. Nothing is uploaded.");
        return report.ToString();
    }
    public static void SelfTest() {
        if(WindowsArgument("a'b $()")!="\"a'b $()\"")throw new Exception("Windows argument quoting");
        if(WindowsArgument("a\"b\\")!="\"a\\\"b\\\\\"")throw new Exception("Windows quote and trailing backslash");
        bool rejected=false;try{PowerShellArguments("Erase","","");}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Action allowlist");
        if(ReadObject(Path.Combine(Root,"no-such-file.json")).Count!=0)throw new Exception("Missing manifest");
        var state=new Dictionary<string,object>{{"code","processing"},{"time",100}};
        if(ReaderStatus(state,114)!="processing (recent heartbeat)")throw new Exception("Recent processing state");
        if(!ReaderStatus(state,115).StartsWith("inactive or stale"))throw new Exception("Stale reader state");
        if(!ReaderStatus(state,99).StartsWith("inactive or stale"))throw new Exception("Future reader state");
        state["time"]="NaN";
        if(!ReaderStatus(state,100).StartsWith("inactive or stale"))throw new Exception("Invalid reader state");
        Console.WriteLine("PASS - setup quoting, action allowlist, missing-state handling and heartbeat freshness");
    }
}

class SetupWindow:Form {
    TextBox game=new TextBox(),python=new TextBox(),output=new TextBox();
    CheckBox advanced=new CheckBox();Control pythonRow;Label includedRuntime=new Label();
    Label state=new Label(); FlowLayoutPanel actions=new FlowLayoutPanel(); bool busy;
    Color bg=Color.FromArgb(24,28,42),card=Color.FromArgb(37,44,61),accent=Color.FromArgb(0,211,199);
    public SetupWindow() {
        try {Icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch{}
        Text="F1 Manager Engineer | Setup "+SetupCore.Version;ClientSize=new Size(820,680);MinimumSize=new Size(760,640);StartPosition=FormStartPosition.CenterScreen;
        BackColor=bg;ForeColor=Color.FromArgb(229,236,244);Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Dpi;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=9};Controls.Add(root);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,52));root.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,28));root.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,28));root.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,52));root.RowStyles.Add(new RowStyle(SizeType.Absolute,40));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.Controls.Add(new Label{Text="F1 MANAGER / ENGINEER",Dock=DockStyle.Fill,Font=new Font("Segoe UI",22,FontStyle.Bold),ForeColor=accent},0,0);
        root.Controls.Add(new Label{Text="Install, update and check your local mod. Close the game before making changes.",Dock=DockStyle.Fill},0,1);
        root.Controls.Add(new Label{Text="F1 Manager 2024 folder",Dock=DockStyle.Fill},0,2);root.Controls.Add(PathRow(game,false),0,3);
        advanced.Text="Advanced: use an external Python (included runtime is recommended)";advanced.Dock=DockStyle.Fill;advanced.ForeColor=ForeColor;root.Controls.Add(advanced,0,4);
        var runtimeHost=new Panel{Dock=DockStyle.Fill};root.Controls.Add(runtimeHost,0,5);
        includedRuntime.Text="Included Python 3.14.8 (64-bit) - selected automatically";includedRuntime.Dock=DockStyle.Fill;includedRuntime.ForeColor=accent;includedRuntime.Padding=new Padding(0,7,0,0);runtimeHost.Controls.Add(includedRuntime);
        pythonRow=PathRow(python,true);pythonRow.Enabled=false;pythonRow.Visible=false;runtimeHost.Controls.Add(pythonRow);
        advanced.CheckedChanged+=(s,e)=>{pythonRow.Enabled=pythonRow.Visible=advanced.Checked;includedRuntime.Visible=!advanced.Checked;if(!advanced.Checked)python.Text=SetupCore.BundledPython;};
        actions.Dock=DockStyle.Fill;actions.WrapContents=false;root.Controls.Add(actions,0,6);
        AddAction("Detect",async()=>await Detect());AddAction("Install",async()=>await Change("Install"));AddAction("Update",async()=>await Change("Update"));AddAction("Uninstall",async()=>await Change("Uninstall"));
        AddAction("Check",async()=>await Check());AddAction("Copy report",async()=>{string g=game.Text,p=python.Text;string report=await Task.Run(()=>SetupCore.Diagnostics(g,p));Clipboard.SetText(report);state.Text="Diagnostic summary copied";});
        state.Dock=DockStyle.Fill;state.ForeColor=accent;state.Text="Ready";root.Controls.Add(state,0,7);
        output.Multiline=true;output.ReadOnly=true;output.ScrollBars=ScrollBars.Vertical;output.Dock=DockStyle.Fill;output.BackColor=card;output.ForeColor=ForeColor;output.BorderStyle=BorderStyle.None;output.Font=new Font("Consolas",10);root.Controls.Add(output,0,8);
        Shown+=async(s,e)=>{busy=true;actions.Enabled=false;try{await Detect();}finally{busy=false;actions.Enabled=true;}};FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};
    }
    Control PathRow(TextBox box,bool file) {
        var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));row.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        box.Dock=DockStyle.Fill;box.BackColor=card;box.ForeColor=ForeColor;box.BorderStyle=BorderStyle.FixedSingle;box.AccessibleName=file?"Python executable":"Game folder";row.Controls.Add(box,0,0);
        var browse=Button("Browse...");browse.AutoSize=false;browse.Margin=new Padding(6,0,0,8);browse.Dock=DockStyle.Fill;row.Controls.Add(browse,1,0);
        browse.Click+=(s,e)=>{if(file){using(var dialog=new OpenFileDialog{Filter="Python executable|python.exe|Executable|*.exe",Title="Choose external Python"})if(dialog.ShowDialog()==DialogResult.OK)box.Text=dialog.FileName;}else{using(var dialog=new FolderBrowserDialog{Description="Choose F1 Manager 2024 (subfolders are recognised automatically)",ShowNewFolderButton=false})if(dialog.ShowDialog()==DialogResult.OK){string normal=SetupCore.NormalizeGame(dialog.SelectedPath);box.Text=normal==""?dialog.SelectedPath:normal;state.Text=normal==""?"Game not found in this folder - use Steam > Browse local files":"Game folder recognised";}}};
        return row;
    }
    Button Button(string text){return new Button{Text=text,AutoSize=true,Height=34,FlatStyle=FlatStyle.Flat,BackColor=card,ForeColor=ForeColor,Margin=new Padding(0,4,8,4),Padding=new Padding(7,0,7,0)};}
    void AddAction(string title,Func<Task> callback){var b=Button(title);actions.Controls.Add(b);b.Click+=async(s,e)=>{if(busy)return;busy=true;actions.Enabled=false;advanced.Enabled=false;game.Enabled=python.Enabled=false;try{await callback();}catch(Exception err){state.Text="Could not finish - see details";output.Text=err is UnauthorizedAccessException?"Windows denied access. Extract to a writable folder; if the game folder is protected, run Setup as administrator.":err.Message;}finally{busy=false;actions.Enabled=true;advanced.Enabled=true;game.Enabled=python.Enabled=true;pythonRow.Enabled=advanced.Checked;}};}
    async Task Detect(){state.Text="Detecting...";string detected=await Task.Run(()=>SetupCore.DetectGame());game.Text=detected;python.Text=SetupCore.BundledPython;advanced.Checked=false;state.Text="Detection finished - reader runtime included";output.Text="No separate Python installation or internet connection is needed.\r\nKeep the complete extracted folder in a permanent location.\r\n\r\n"+await Task.Run(()=>SetupCore.Diagnostics(detected,SetupCore.BundledPython));}
    async Task Check(){state.Text="Checking...";string normal=SetupCore.NormalizeGame(game.Text);if(normal!="")game.Text=normal;string g=game.Text,p=python.Text;output.Text=await Task.Run(()=>SetupCore.Diagnostics(g,p));state.Text="Check complete. Copy report includes no personal paths or saves.";}
    async Task Change(string action){
        if(Process.GetProcessesByName("F1Manager24").Length>0){state.Text="Close F1 Manager 2024 first";return;}
        if(!File.Exists(Path.Combine(SetupCore.Root,action+".ps1"))||!File.Exists(Path.Combine(SetupCore.Root,"InstallerAction.ps1"))){state.Text="Extract the complete release folder first";return;}
        if(action=="Install"){
            string normal=SetupCore.NormalizeGame(game.Text);if(normal==""){state.Text="Game folder not found";output.Text="In Steam, choose Manage > Browse local files, then select that folder with Browse. Subfolders are recognised automatically.";return;}game.Text=normal;
            string path=python.Text;string probe=await Task.Run(()=>SetupCore.ProbePython(path));if(!probe.StartsWith("OK ")){state.Text="Reader runtime needs attention";output.Text=probe;return;}
        }else if(!File.Exists(Path.Combine(SetupCore.Root,"installed-files.json"))){state.Text="No installation record in this folder";return;}
        if(action!="Uninstall"){string package;if(!SetupCore.PackageReady(out package)){state.Text="Incomplete package";output.Text=package;return;}}
        if(action=="Uninstall"&&MessageBox.Show(this,"Remove unchanged files owned by this installation? Career saves and modified files are preserved.","Uninstall Engineer",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        string g=game.Text,p=python.Text;state.ForeColor=accent;state.Text=action+" in progress...";
        output.Text="Running "+action+"...";var run=await Task.Run(()=>SetupCore.Execute(action,g,p));
        string details=String.IsNullOrWhiteSpace(run.Item2)?"No output was returned. Check folder permissions and use Check to inspect the installation.":run.Item2;
        output.Text=(action+" - exit code "+run.Item1+"\r\n\r\n"+details.Replace("\r\n","\n").Replace("\n","\r\n"));
        output.SelectionStart=0;output.SelectionLength=0;output.ScrollToCaret();output.Refresh();
        state.ForeColor=run.Item1==0?accent:Color.FromArgb(255,184,90);state.Text=run.Item1==0?action+" complete":action+" failed - see details below";
        if(run.Item1==0&&action!="Uninstall") {
            var record=SetupCore.ReadObject(Path.Combine(SetupCore.Root,"installed-files.json"));game.Text=SetupCore.Get(record,"gameDir");python.Text=SetupCore.Get(record,"python");
            string installedGame=game.Text,installedPython=python.Text;state.Text="Verifying installation...";
            var verified=await Task.Run(()=>SetupCore.Readiness(installedGame,installedPython));
            output.AppendText("\r\nFINAL CHECK\r\n"+verified.Item2.Replace("\r\n","\n").Replace("\n","\r\n"));
            state.ForeColor=verified.Item1?accent:Color.FromArgb(255,184,90);state.Text=verified.Item1?action+" complete - ready to launch":"Files copied - final check needs attention";
        }
    }
    [STAThread] static void Main(string[] args){
        if(args.Contains("--self-test")){SetupCore.SelfTest();return;}
        if(args.Contains("--diagnose")){Console.WriteLine(SetupCore.Diagnostics(SetupCore.DetectGame(),SetupCore.PythonCandidates().FirstOrDefault()??""));return;}
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new SetupWindow());
    }
}
