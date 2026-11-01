$desktop = [Environment]::GetFolderPath('Desktop')
$lnk = (New-Object -ComObject WScript.Shell).CreateShortcut("$desktop\Arden's Solver.lnk")
$lnk.TargetPath = "$PSScriptRoot\ArdensSolver.exe"
$lnk.WorkingDirectory = $PSScriptRoot
$lnk.IconLocation = "$PSScriptRoot\ArdensSolver.exe,0"
$lnk.Description = "Arden's Theorem Solver"
$lnk.Save()
