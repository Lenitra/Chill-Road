<#
.SYNOPSIS
  Build et publication d'une version de Chill Road : release GitHub (zip téléchargé par le site) + mise à jour du site docs/.

.DESCRIPTION
  1. Récupère origin/main dans un worktree séparé (E:\ChillRoad_Release par défaut) : le build contient exactement ce qui
     est poussé sur GitHub, sans les modifications locales non commitées, et l'éditeur Unity du projet peut rester ouvert.
  2. Compile le jeu avec Unity en -batchmode (ChillRoadBuild.ReleaseBuild) et le zippe en Builds\ChillRoad-Windows.zip.
     Le premier build importe tout le projet (long), les suivants réutilisent le dossier Library du worktree.
  3. Demande confirmation, crée la release GitHub vX.Y.Z (marquée « latest ») avec le zip et la liste des commits.
  4. Met à jour la version, la taille du zip et la date dans docs/index.html, commit et push (GitHub Pages se met à jour).

.EXAMPLE
  .\Tools\Release\release.ps1 -Version 0.2.0
  .\Tools\Release\release.ps1 -Version 0.2.0 -DryRun        # build + zip seulement, rien n'est publié
  .\Tools\Release\release.ps1 -Version 0.2.0 -Yes -Notes "Nouvelle voiture et météo revue"
#>
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [switch]$DryRun,                 # build + zip, sans release ni modification du site
    [switch]$Yes,                    # publie sans demander confirmation après le build
    [string]$Notes = '',             # texte ajouté en tête des notes de version
    [string]$WorktreePath = 'E:\ChillRoad_Release'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$tag = "v$Version"
$siteFile = Join-Path $repo 'docs\index.html'

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Warn($text) { Write-Host "Attention : $text" -ForegroundColor Yellow }
function Fail($text) { Write-Host "ERREUR : $text" -ForegroundColor Red; exit 1 }
function GitIn($dir) {
    $out = & git -C $dir @args
    if ($LASTEXITCODE -ne 0) { Fail "git $($args -join ' ') (dans $dir)" }
    return $out
}

# ---------------------------------------------------------------- Vérifications
Step "Vérifications"
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { Fail "gh (GitHub CLI) introuvable." }
$ErrorActionPreference = 'Continue'   # PowerShell 5.1 : un stderr natif redirigé deviendrait une erreur bloquante
& gh auth status *> $null
$ErrorActionPreference = 'Stop'
if ($LASTEXITCODE -ne 0) { Fail "gh n'est pas connecté : lancer « gh auth login »." }

GitIn $repo fetch origin --tags --quiet | Out-Null
if (& git -C $repo ls-remote --tags origin "refs/tags/$tag") { Fail "le tag $tag existe déjà sur GitHub." }
if (-not $DryRun -and (& git -C $repo status --porcelain -- docs/index.html)) {
    Fail "docs/index.html a des modifications non commitées : les commiter ou les annuler avant de publier."
}

$sha = (GitIn $repo rev-parse origin/main).Trim()
$short = $sha.Substring(0, 7)
$unpushed = [int](GitIn $repo rev-list --count origin/main..HEAD)
$dirty = @(& git -C $repo status --porcelain).Count
Write-Host "Version $tag, build de origin/main ($short)."
if ($unpushed -gt 0) { Warn "$unpushed commit(s) local(aux) non poussé(s) : absents du build." }
if ($dirty -gt 0) { Warn "$dirty fichier(s) modifié(s) non commité(s) dans le projet : absents du build." }

# ---------------------------------------------------------------- Worktree
Step "Worktree $WorktreePath"
GitIn $repo worktree prune | Out-Null
if (Test-Path (Join-Path $WorktreePath '.git')) {
    GitIn $WorktreePath checkout --detach --force --quiet $sha | Out-Null
    GitIn $WorktreePath clean -fd --quiet | Out-Null      # sans -x : Library (ignoré par git) est conservé
} else {
    Write-Host "Création du worktree (le premier build importera tout le projet, compter 20 à 30 min)."
    GitIn $repo worktree add --detach $WorktreePath $sha | Out-Null
}

# ---------------------------------------------------------------- Build Unity
Step "Build Unity"
$versionFile = Join-Path $WorktreePath 'ProjectSettings\ProjectVersion.txt'
$editorVersion = ((Select-String -Path $versionFile -Pattern '^m_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value)
$unity = "C:\Program Files\Unity\Hub\Editor\$editorVersion\Editor\Unity.exe"
if (-not (Test-Path $unity)) { Fail "Unity $editorVersion introuvable ($unity)." }

$buildDir = Join-Path $WorktreePath 'Builds'
New-Item -ItemType Directory -Force $buildDir | Out-Null
$zip = Join-Path $buildDir 'ChillRoad-Windows.zip'
$log = Join-Path $buildDir 'release_build.log'
if (Test-Path $zip) { Remove-Item $zip -Force }

Write-Host "Unity $editorVersion en arrière-plan, journal : $log"
$timer = [Diagnostics.Stopwatch]::StartNew()
$unityArgs = "-batchmode -nographics -projectPath `"$WorktreePath`" " +
             "-executeMethod ChillRoad.EditorTools.ChillRoadBuild.ReleaseBuild -releaseVersion $Version -logFile `"$log`""
$process = Start-Process -FilePath $unity -ArgumentList $unityArgs -Wait -PassThru
$timer.Stop()
if ($process.ExitCode -ne 0 -or -not (Test-Path $zip)) {
    Write-Host "`nDernières erreurs du journal :" -ForegroundColor Red
    Select-String -Path $log -Pattern 'error|exception|failed' -ErrorAction SilentlyContinue |
        Select-Object -Last 15 | ForEach-Object { Write-Host "  $($_.Line)" }
    Fail "build échoué (code $($process.ExitCode)), voir $log"
}
$sizeMo = [math]::Round((Get-Item $zip).Length / 1MB)
Write-Host ("Build réussi en {0:mm\:ss} : {1} ({2} Mo)" -f $timer.Elapsed, $zip, $sizeMo) -ForegroundColor Green

if ($DryRun) {
    Write-Host "`n-DryRun : rien n'a été publié. Le jeu compilé est dans $buildDir\Windows" -ForegroundColor Yellow
    exit 0
}
if (-not $Yes) {
    $answer = Read-Host "`nPublier $tag ($sizeMo Mo) sur GitHub et mettre le site à jour ? (o/N)"
    if ($answer -notmatch '^(o|oui|y|yes)$') { Write-Host "Annulé, rien n'a été publié."; exit 0 }
}

# ---------------------------------------------------------------- Release GitHub
Step "Release GitHub $tag"
$ErrorActionPreference = 'Continue'
$previous = & git -C $repo describe --tags --abbrev=0 $sha 2>$null
$hasPrevious = ($LASTEXITCODE -eq 0 -and $previous)
$ErrorActionPreference = 'Stop'
$range = if ($hasPrevious) { "$previous..$sha" } else { $sha }
$changes = @(& git -C $repo log --no-merges --pretty=format:'- %s' $range)
$body = @()
if ($Notes) { $body += $Notes; $body += '' }
$body += "## Changements"
$body += if ($changes.Count) { $changes } else { '- (aucun commit depuis la version précédente)' }
$notesFile = Join-Path $env:TEMP "chillroad_notes_$Version.md"
[IO.File]::WriteAllText($notesFile, ($body -join "`n"), (New-Object Text.UTF8Encoding($false)))

Push-Location $repo
try {
    & gh release create $tag $zip --target $sha --title "Chill Road $Version" --notes-file $notesFile --latest
    if ($LASTEXITCODE -ne 0) { Fail "création de la release refusée par gh." }
} finally { Pop-Location; Remove-Item $notesFile -ErrorAction SilentlyContinue }

# ---------------------------------------------------------------- Site
Step "Site docs/index.html"
$utf8 = New-Object Text.UTF8Encoding($false)
$html = [IO.File]::ReadAllText($siteFile, $utf8)
$date = (Get-Date).ToString('d MMMM yyyy', [Globalization.CultureInfo]::GetCultureInfo('fr-FR'))
$replacements = @(
    @{ Pattern = 'zip de \d+ Mo';                       Value = "zip de $sizeMo Mo" },                 # en-tête
    @{ Pattern = 'Version \d+\.\d+\.\d+ · \d+ Mo';      Value = "Version $Version · $sizeMo Mo" },     # bloc téléchargement
    @{ Pattern = 'v\d+\.\d+\.\d+ · \d{1,2} [^\s<]+ \d{4}'; Value = "v$Version · $date" }               # pied de page
)
foreach ($r in $replacements) {
    if ($html -match $r.Pattern) { $html = [regex]::Replace($html, $r.Pattern, $r.Value) }
    else { Warn "motif « $($r.Pattern) » absent de docs/index.html, à corriger à la main." }
}
[IO.File]::WriteAllText($siteFile, $html, $utf8)

GitIn $repo commit --quiet -m "Site: version $Version ($sizeMo Mo)" -- docs/index.html | Out-Null
& git -C $repo push --quiet origin HEAD:main
if ($LASTEXITCODE -ne 0) {
    Warn "push refusé (la branche locale est sans doute en retard) : faire « git pull --rebase --autostash » puis « git push »."
} else {
    Write-Host "Site poussé : https://lenitra.github.io/Chill-Road/ se met à jour d'ici une minute."
}

Write-Host "`nVersion $tag publiée : https://github.com/Lenitra/Chill-Road/releases/tag/$tag" -ForegroundColor Green
