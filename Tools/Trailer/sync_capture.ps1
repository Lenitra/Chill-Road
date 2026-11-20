# Copie de capture de la bande-annonce : miroir du projet vers E:\ChillRoad_Capture, puis les scripts de tournage
# (hors du jeu) et l'ajout du frein a main au pilote automatique, dans la copie seulement.
#   powershell -File Tools\Trailer\sync_capture.ps1 [-Build]
param([switch]$Build)
$src = Split-Path (Split-Path $PSScriptRoot)
$dst = 'E:\ChillRoad_Capture'
$keep = @('SiteShots.cs', 'SiteShots.cs.meta', 'CaptureBuild.cs', 'CaptureBuild.cs.meta', 'TrailerDirector.cs', 'TrailerDirector.cs.meta')
foreach ($d in 'Assets', 'Packages', 'ProjectSettings') {
    robocopy "$src\$d" "$dst\$d" /MIR /XF $keep /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
}
Copy-Item "$src\Tools\SiteCapture\SiteShots.cs" "$dst\Assets\ChillRoad\Scripts\DebugTools\SiteShots.cs" -Force
Copy-Item "$src\Tools\Trailer\TrailerDirector.cs" "$dst\Assets\ChillRoad\Scripts\DebugTools\TrailerDirector.cs" -Force
Copy-Item "$src\Tools\ItchCapture\CaptureBuild.cs" "$dst\Assets\ChillRoad\Scripts\Editor\CaptureBuild.cs" -Force
# Frein a main du pilote automatique (drift de la bande-annonce) : le jeu ne le lit que sur les commandes du joueur
$vc = "$dst\Assets\ChillRoad\Scripts\Vehicle\VehicleController.cs"
$text = [IO.File]::ReadAllText($vc)
$a = 'public float AutoThrottle, AutoBrake, AutoSteer;'
$b = 'HandbrakeOn = InputEnabled && GameInput.Handbrake;'
if (-not $text.Contains($a) -or -not $text.Contains($b)) { throw 'VehicleController : lignes du pilote automatique introuvables' }
$text = $text.Replace($a, $a + ' public bool AutoHandbrake;').Replace($b, 'HandbrakeOn = InputEnabled ? GameInput.Handbrake : Autopilot && AutoHandbrake;')
[IO.File]::WriteAllText($vc, $text, (New-Object Text.UTF8Encoding $false))
Remove-Item "$dst\Library\ArtifactDB-lock", "$dst\Temp\UnityLockfile" -ErrorAction SilentlyContinue
if ($Build) {
    $unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.11f1\Editor\Unity.exe'
    $p = Start-Process $unity -ArgumentList '-batchmode', '-nographics', '-projectPath', $dst, '-executeMethod', 'ChillRoad.EditorTools.CaptureBuild.Build', '-logFile', "$dst\build_trailer.log" -Wait -PassThru
    Get-Content "$dst\Capture\build.txt"
    if ($p.ExitCode -ne 0) { Select-String -Path "$dst\build_trailer.log" -Pattern 'error CS' | Select-Object -First 20; throw "build : code $($p.ExitCode)" }
}
