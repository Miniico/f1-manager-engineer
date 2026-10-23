' Start the read-only reader invisibly when the game opens the mod.
' All paths come from this local installation; no terminal or network is used.
Option Explicit
Dim files, shell, root, stream, config, pattern, matches, python, pythonw, bridge
On Error Resume Next
Set files = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
root = files.GetParentFolderName(WScript.ScriptFullName)
Set stream = CreateObject("ADODB.Stream")
stream.Type = 2
stream.Charset = "utf-8"
stream.Open
stream.LoadFromFile root & "\installed-files.json"
config = stream.ReadText
stream.Close
If Err.Number <> 0 Then WScript.Quit 1
Set pattern = New RegExp
pattern.Pattern = """python""\s*:\s*""([^""]+)"""
Set matches = pattern.Execute(config)
If matches.Count <> 1 Then WScript.Quit 2
python = Replace(matches(0).SubMatches(0), "\\", "\")
pythonw = files.BuildPath(files.GetParentFolderName(python), "pythonw.exe")
If files.FileExists(pythonw) Then python = pythonw
bridge = files.BuildPath(root, "bridge.py")
If Not files.FileExists(python) Or Not files.FileExists(bridge) Then WScript.Quit 3
shell.CurrentDirectory = root
shell.Run Chr(34) & python & Chr(34) & " -B " & Chr(34) & bridge & Chr(34) & " --watch --game-bound", 0, False
If Err.Number <> 0 Then WScript.Quit 4
