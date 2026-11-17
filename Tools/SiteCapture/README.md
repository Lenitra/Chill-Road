# Captures du site (docs/)

`SiteShots.cs` : directeur de prises de vues, à copier **dans une copie du projet uniquement**
(`E:\ChillRoad_Capture\Assets\ChillRoad\Scripts\DebugTools\`), avec `Tools/ItchCapture/CaptureBuild.cs` dans `Scripts/Editor/`.

1. Copier Assets, Packages, ProjectSettings et Library vers `E:\ChillRoad_Capture` (sans `Library/ArtifactDB-lock` ni `EditorInstance.json`).
2. Build : `Unity.exe -batchmode -nographics -projectPath E:/ChillRoad_Capture -executeMethod ChillRoad.EditorTools.CaptureBuild.Build`.
3. Prises : `Capture/Capture.exe -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -siteshots plan.json -out dossier` (PNG 3840×2160, interface en français).

Chaque prise du plan JSON : `car` (morceau du nom), `x`/`z` (point de la carte), `kind` (Highway, Main, Mountain, Lane, Track, Ramp),
`span` (bridge/tunnel) + `spanOffset`, `hour`, `storm`, `speed` (km/h, pilote automatique), `drive` (s), `traffic`,
`hud` (vraie caméra + HUD), sinon caméra `cam`/`look` dans le repère de la voiture, `world` (repère monde) ou `useEye` + `eye` (point fixe).
