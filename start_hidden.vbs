' Start the executable without a visible console window.
Set fso = CreateObject("Scripting.FileSystemObject")
base = fso.GetParentFolderName(WScript.ScriptFullName)
Set sh = CreateObject("WScript.Shell")
sh.CurrentDirectory = base
sh.Run Chr(34) & base & "\CodexQuotaPinger.exe" & Chr(34), 0, False
