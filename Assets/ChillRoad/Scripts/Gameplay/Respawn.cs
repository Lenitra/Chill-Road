using UnityEngine;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Replacement de la voiture sur la route (bouton Y / R, voiture retournée, tombée à l'eau, sous la carte ou
    /// sortie des limites). La seule pénalité est le temps (rien à payer, combo gardé) : commandes coupées près de
    /// trois secondes, et l'on repart arrêté. En course, retour juste après la dernière porte franchie, chrono en marche.
    /// Transition : la voiture freine pendant un fondu au noir, elle est reposée dans le noir (le trafic s'écarte),
    /// puis l'image revient pendant que la caméra descend de haut derrière elle ; on reprend la main un instant après.
    /// Sens de la voiture reposée : celui du parcours en course, celui de l'itinéraire si le GPS en affiche un,
    /// sinon celui où elle regardait.
    /// </summary>
    public class Respawn : MonoBehaviour
    {
        /// <summary>Fondu au noir, noir complet (voiture reposée au début), retour de l'image, attente à l'arrêt (s).</summary>
        const float FadeOut = 0.5f, Black = 0.5f, FadeIn = 0.6f, Hold = 1.2f;
        /// <summary>Itinéraire du GPS plus loin que ceci (m) : on se replace sur la route la plus proche, sans lui.</summary>
        const float RouteReach = 400f;
        static readonly Color Night = new Color(0.015f, 0.02f, 0.04f);
        static readonly Color Tint = new Color(1f, 0.45f, 0.18f);

        enum Step { Idle, Out, Dark, In }

        GameManager game;
        Step step;
        float t;
        bool racing;
        CanvasGroup veil;

        /// <summary>Replacement en cours (de l'appui jusqu'au retour des commandes).</summary>
        public bool Busy => step != Step.Idle;

        public void Init(GameManager g)
        {
            game = g;
            if (GameManager.DedicatedServer) return;
            // Voile plein écran au-dessus du HUD (il le cache pendant le noir), sous les menus
            var go = new GameObject("RespawnVeil");
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 3;
            veil = go.AddComponent<CanvasGroup>();
            veil.alpha = 0f;
            veil.blocksRaycasts = false;
            veil.interactable = false;
            var img = new GameObject("Night", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
            img.transform.SetParent(go.transform, false);
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            img.color = Night;
            img.raycastTarget = false;
            go.SetActive(false);
        }

        /// <summary>Lance un replacement (sans effet s'il y en a déjà un, ou pendant le décompte d'une course).</summary>
        public void Begin()
        {
            if (Busy || !game || game.State != GameManager.Phase.Playing) return;
            var races = game.Races;
            if (races && races.Counting) return;
            racing = races && races.Running;
            step = Step.Out;
            t = 0f;
            game.SetInputEnabled(false);
            if (game.Local.Audio) game.Local.Audio.RespawnOut();
            GamepadRumble.Kick(0.25f, 0.1f, 0.25f);
            if (veil) veil.gameObject.SetActive(true);
        }

        void Update()
        {
            if (step == Step.Idle) return;
            // Retour au menu, nouvelle partie : on abandonne le replacement
            if (game.State != GameManager.Phase.Playing) { Finish(false); return; }
            bool menu = game.Hud && game.Hud.Paused;
            t += Time.deltaTime;
            // Commandes tenues coupées (un menu refermé pendant le replacement les rendrait)
            if (!menu && game.Player.InputEnabled) game.SetInputEnabled(false);

            float dark;
            switch (step)
            {
                case Step.Out:
                    dark = t / FadeOut;
                    dark *= dark;
                    if (t >= FadeOut)
                    {
                        Place();
                        step = Step.Dark;
                        t = 0f;
                        dark = 1f;
                    }
                    break;
                case Step.Dark:
                    dark = 1f;
                    if (t >= Black)
                    {
                        step = Step.In;
                        t = 0f;
                        game.Camera.Respawn(FadeIn + Hold * 0.8f);
                        Announce();
                    }
                    break;
                default:
                    dark = 1f - Mathf.SmoothStep(0f, 1f, t / FadeIn);
                    if (t >= FadeIn + Hold) { Finish(!menu); return; }
                    break;
            }
            // Un menu ouvert passe devant : le voile s'efface le temps qu'il reste ouvert
            if (veil) veil.alpha = menu ? 0f : Mathf.Clamp01(dark);
        }

        void Finish(bool giveBack)
        {
            step = Step.Idle;
            if (veil) { veil.alpha = 0f; veil.gameObject.SetActive(false); }
            if (!giveBack) return;
            game.SetInputEnabled(true);
            if (game.Local.Audio) game.Local.Audio.RespawnIn();
        }

        void Announce()
        {
            if (!game.Hud) return;
            game.Hud.Announce(Loc.T("REPLACEMENT", "RESPAWN"),
                racing ? Loc.T("Retour à la dernière porte", "Back to the last gate") : Loc.T("Retour sur la route", "Back on the road"), Tint);
        }

        /// <summary>
        /// Repose la voiture : en course après la dernière porte franchie ; à l'aéroport (hors du réseau routier),
        /// redressée sur place ; ailleurs sur la route la plus proche (de l'itinéraire du GPS s'il y en a un), dans sa
        /// voie, dans le sens du parcours, de l'itinéraire ou de la voiture.
        /// </summary>
        void Place()
        {
            var car = game.Player;
            Vector3 from = car.transform.position;
            if (game.Races && game.Races.RespawnPoint(out var gatePos, out var gateDir))
            {
                Put(gatePos, gateDir, 18f);
                return;
            }
            if (Airfield.Inside(from))
            {
                Vector3 facing = car.transform.forward;
                facing.y = 0f;
                if (facing.sqrMagnitude < 0.01f) facing = Vector3.forward;
                Put(new Vector3(from.x, game.Map.Height(from.x, from.z) + 0.1f, from.z), facing, 10f);
                return;
            }
            Vector3 heading = car.transform.forward;
            heading.y = 0f;
            Vector3 routePoint = default, routeDir = default;
            if (game.Nav && game.Nav.RespawnHint(from, RouteReach, out routePoint, out routeDir))
            {
                // L'itinéraire est déjà dans la voie : la voiture y est posée telle quelle, dans son sens
                Put(routePoint, routeDir, 14f);
                return;
            }
            var hit = game.Road.Nearest(from, 4000f);
            if (!hit.Found) { Put(from + Vector3.up * 1.5f, heading.sqrMagnitude > 0.01f ? heading : Vector3.forward, 10f); return; }
            var e = hit.edge;
            float s = Mathf.Clamp(hit.s, Mathf.Min(e.trimA + 3f, e.Length * 0.5f), Mathf.Max(e.Length - e.trimB - 3f, e.Length * 0.5f));
            e.Evaluate(s, out var tangent, out _);
            // Bretelle à sens unique : toujours dans le sens de la circulation (de a vers b)
            int direction = e.oneway || Vector3.Dot(tangent, heading) >= 0f ? 1 : -1;
            Vector3 pos = e.LanePosition(s, direction, out var forward);
            Put(pos, forward, 14f);
        }

        void Put(Vector3 pos, Vector3 forward, float clear)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            pos += Vector3.up * 0.5f;
            game.Traffic.ClearAround(pos, clear);
            game.Player.Teleport(pos, Quaternion.LookRotation(forward.normalized, Vector3.up), 0f);
            game.Local.UpsideDown = 0f;
        }
    }
}
