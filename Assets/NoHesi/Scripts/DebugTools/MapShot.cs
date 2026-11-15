using System.IO;
using UnityEngine;

namespace NoHesi
{
    /// <summary>Captures de la carte pendant le jeu (outil de mise au point) : rendu d'une caméra temporaire dans un PNG.</summary>
    public static class MapShot
    {
        public static string Folder = "E:/ChillRoad/Tools/Map/out/";

        /// <summary>Photo depuis « pos » vers « look ».</summary>
        public static void Take(string name, Vector3 pos, Vector3 look, float fov = 55f, int width = 1280, int height = 720)
        {
            var main = Camera.main;
            if (!main) return;
            var go = new GameObject("ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.fieldOfView = fov;
            cam.farClipPlane = 12000f;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Folder + name + ".png", tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }

        /// <summary>Photo d'un point de la carte, vu d'en haut de biais : distance, hauteur et cap de la caméra (degrés).</summary>
        public static void Over(string name, float x, float z, float distance, float height, float heading = 200f, float fov = 55f)
        {
            var gm = Object.FindFirstObjectByType<GameManager>();
            float y = gm && gm.Map != null ? gm.Map.Height(x, z) : 0f;
            Vector3 target = new Vector3(x, y, z);
            Vector3 dir = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            Take(name, target - dir * distance + Vector3.up * height, target, fov);
        }

        /// <summary>Place le joueur sur la route la plus proche d'un point, beau temps, à l'heure donnée.</summary>
        public static void Go(float x, float z, float hour = 14f)
        {
            var gm = Object.FindFirstObjectByType<GameManager>();
            if (!gm) return;
            Application.runInBackground = true;
            VegetationRenderer.NoFrustumCull = true;
            if (gm.State == GameManager.Phase.Title) gm.StartCountdown();
            gm.Weather.Force(false, true);
            gm.DayNight.timeOfDay = hour;
            gm.TeleportTo(new Vector3(x, 0f, z));
        }
    }
}
