using System;
using UnityEngine;

namespace ChillRoad
{
    /// <summary>
    /// Carte générée par Tools/Map (map_bake.py) et lue depuis Resources/Map : relief (grille de hauteurs), masques du
    /// sol, plans d'eau, réseau routier, rivières, objets posés et lieux-dits.
    /// Repère : x = est, z = nord, carte centrée sur l'origine.
    /// </summary>
    public class MapData
    {
        [Serializable] public class WaterJson { public string id; public float level, x, z, r; }
        [Serializable] public class DamJson { public float ax, az, bx, bz, crest, width; }
        [Serializable]
        public class NodeJson
        {
            public string id; public float x, y, z;
            /// <summary>1 = point de l'autoroute où s'accroche une bretelle (pas de carrefour : l'autoroute continue).</summary>
            public int merge;
        }
        /// <summary>
        /// Voie d'insertion ou de décélération accolée à une chaussée d'autoroute : pleine largeur à l'abscisse sa
        /// (au nœud de la bretelle), biseau jusqu'à sb. side : +1 = à droite de l'axe (sens a → b), -1 = à gauche.
        /// </summary>
        [Serializable] public class AuxJson { public float sa, sb; public int side; public float width; }
        /// <summary>Ouvrage entre les abscisses s0 et s1 (pont, tunnel, barrage). roof : épaisseur de la coque d'un
        /// tunnel au-dessus du gabarit, donnée pour un passage inférieur (dalle mince) ; 0 = RoadNetwork.TunnelRoof.</summary>
        [Serializable] public class SpanJson { public float s0, s1; public string kind; public float roof; }
        [Serializable]
        public class EdgeJson
        {
            public string id, kind, a, b;
            public float hw, lane, sidewalk, speed;
            public int dirt;
            /// <summary>Voies par sens (1, ou 3 sur l'autoroute) et demi-largeur du terre-plein central.</summary>
            public int lanes = 1;
            public float median;
            /// <summary>1 = sens unique (bretelle), toujours parcourue de a vers b.</summary>
            public int oneway;
            public AuxJson[] aux;
            public float[] pts;
            public int[] rail;
            public SpanJson[] spans;
        }
        [Serializable] public class RiverJson { public string id; public float width; public float[] pts; }
        [Serializable] public class PlaceJson { public string item; public float x, y, z, yaw, scale = 1f; public int flags; }
        /// <summary>Lieu-dit : side = côté de l'étiquette sur la carte globale (-1 : à gauche du repère, sinon à droite).</summary>
        [Serializable] public class PoiJson { public string id, fr, en; public float x, z, r; public int side; }
        /// <summary>
        /// Cascade : tremplin au bord d'une route. (x, y, z) = pied du nez du tremplin (au sol), yaw = sens de la
        /// course (degrés), length / width = tablier ; target = distance à parcourir en l'air (m), reward = prime.
        /// </summary>
        [Serializable] public class StuntJson { public string id, fr, en, pack; public float x, y, z, yaw, length, width, target; public int reward; }
        /// <summary>
        /// Radar de vitesse : (x, y, z) = point de l'axe de la route, yaw = sens de la route (degrés), halfWidth = demi-largeur
        /// contrôlée, flash* = position des lampes ; target = vitesse à atteindre (km/h), reward = prime.
        /// </summary>
        [Serializable] public class RadarJson { public string id, fr, en, pack; public float x, y, z, yaw, halfWidth, flashX, flashY, flashZ, target; public int reward; }
        /// <summary>
        /// Aéroport (Tools/Map/map_airport.py, Airfield.cs). Repère local : origine (x, y = sol aplani, z) au milieu de la
        /// piste, u le long de la piste (ax, az), v à gauche de u. half / hw : demi-longueur et demi-largeur de la piste ;
        /// taxi* : voie de circulation parallèle (axe v, demi-largeur, demi-longueur) ; links : abscisses des bretelles ;
        /// apron, park, gate : aires en béton (u0, v0, u1, v1) ; parkU : abscisse de l'allée d'arrivée du parking ;
        /// heli : hélisurface (u, v, rayon) ; stands : postes (u, v, du, dv, longueur) ; west / east : numéros des seuils ;
        /// bays : rangées de places du parking (u0, v0, u1, v1, profondeur), ligne de fond de (u0, v0) à (u1, v1), les
        /// places s'ouvrent à gauche de ce sens.
        /// </summary>
        [Serializable]
        public class AirfieldJson
        {
            public float x, y, z, ax, az, half, hw, taxiV, taxiHw, taxiHalf, parkU;
            public float[] links, apron, park, gate, heli, stands, bays;
            public string west, east;
        }
        [Serializable]
        public class MapJson
        {
            public int n;
            public float step, half, hmin, hmax;
            public WaterJson[] waters;
            public DamJson dam;
            public NodeJson[] nodes;
            public EdgeJson[] edges;
            public RiverJson[] rivers;
            public PlaceJson[] places;
            public PoiJson[] pois;
            public StuntJson[] stunts;
            public RadarJson[] radars;
            public AirfieldJson airfield;
        }

        public MapJson Json { get; private set; }
        /// <summary>Nombre de nœuds par côté, pas de la grille (m), demi-côté de la carte (m).</summary>
        public int N { get; private set; }
        public float Step { get; private set; }
        public float Half { get; private set; }
        /// <summary>Hauteurs aux nœuds de la grille, indexées [iz * N + ix].</summary>
        public float[] H { get; private set; }
        /// <summary>Masques du sol (4 octets par nœud) : sous-bois / forêt, herbe sèche, roche, terre.</summary>
        public byte[] Masks { get; private set; }
        /// <summary>Annexes (4 octets par nœud) : (distance au bord de route + 1 m) × 4, eau (bits 0-2), zone
        /// occupée (bit 3) et trou du terrain (bit 4 : entrée de tunnel), pavé, neige.</summary>
        public byte[] Info { get; private set; }

        public static MapData Load()
        {
            var json = Resources.Load<TextAsset>("Map/map");
            var heights = Resources.Load<TextAsset>("Map/map_heights");
            var masks = Resources.Load<TextAsset>("Map/map_masks");
            var info = Resources.Load<TextAsset>("Map/map_info");
            if (!json || !heights || !masks || !info)
            {
                Debug.LogError("Carte introuvable dans Resources/Map (lancer Tools/Map/map_bake.py)");
                return null;
            }
            var m = new MapData { Json = JsonUtility.FromJson<MapJson>(json.text) };
            m.N = m.Json.n;
            m.Step = m.Json.step;
            m.Half = m.Json.half;
            var raw = heights.bytes;
            int count = m.N * m.N;
            m.H = new float[count];
            float k = (m.Json.hmax - m.Json.hmin) / 65535f;
            for (int i = 0; i < count; i++) m.H[i] = m.Json.hmin + (raw[i * 2] | raw[i * 2 + 1] << 8) * k;
            m.Masks = masks.bytes;
            m.Info = info.bytes;
            Resources.UnloadAsset(heights);
            return m;
        }

        public int Index(int ix, int iz) => Mathf.Clamp(iz, 0, N - 1) * N + Mathf.Clamp(ix, 0, N - 1);

        /// <summary>Nœud de grille le plus proche d'un point.</summary>
        public int Nearest(float x, float z) => Index(Mathf.RoundToInt((x + Half) / Step), Mathf.RoundToInt((z + Half) / Step));

        /// <summary>Hauteur du sol : celle de la surface maillée au plus fin (même découpage en triangles).</summary>
        public float Height(float x, float z)
        {
            float fx = Mathf.Clamp((x + Half) / Step, 0f, N - 1.001f), fz = Mathf.Clamp((z + Half) / Step, 0f, N - 1.001f);
            int ix = (int)fx, iz = (int)fz;
            float u = fx - ix, v = fz - iz;
            int i = iz * N + ix;
            float h00 = H[i], h10 = H[i + 1], h01 = H[i + N], h11 = H[i + N + 1];
            // Diagonale de (1,0) à (0,1), comme les tuiles du terrain
            if (u + v <= 1f) return h00 + (h10 - h00) * u + (h01 - h00) * v;
            return h11 + (h01 - h11) * (1f - u) + (h10 - h11) * (1f - v);
        }

        /// <summary>Pente locale (tangente de l'angle).</summary>
        public float Slope(float x, float z)
        {
            float d = Step;
            float gx = (Height(x + d, z) - Height(x - d, z)) / (2f * d), gz = (Height(x, z + d) - Height(x, z - d)) / (2f * d);
            return Mathf.Sqrt(gx * gx + gz * gz);
        }

        /// <summary>Distance au bord de la chaussée la plus proche (m, de -1 à 62).</summary>
        public float RoadEdge(float x, float z) => Info[Nearest(x, z) * 4] * 0.25f - 1f;
        /// <summary>Plan d'eau au point (0 = aucun, sinon indice + 1 dans la liste des plans d'eau).</summary>
        public int Water(float x, float z) => Info[Nearest(x, z) * 4 + 1] & 7;
        public bool Blocked(float x, float z) => (Info[Nearest(x, z) * 4 + 1] & 8) != 0;
        /// <summary>Trou du terrain (entrée de tunnel) : la maille dont ce nœud est le coin sud-ouest n'est pas dessinée.</summary>
        public bool Hole(int ix, int iz) => (Info[Index(ix, iz) * 4 + 1] & HoleFlag) != 0;
        public const int HoleFlag = 16;
        public const int TunnelFlag = 32;

        /// <summary>Un tunnel passe-t-il sous ce nœud (ou à moins de « reach » nœuds) ? Les tuiles n'y ont pas de jupe.</summary>
        public bool OverTunnel(int ix, int iz, int reach)
        {
            for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
                if ((Info[Index(ix + dx, iz + dz) * 4 + 1] & TunnelFlag) != 0) return true;
            return false;
        }

        /// <summary>
        /// Point sous le relief, au-dessus duquel passe un tunnel (dans le tube, ou enterré à côté) : la hauteur du
        /// terrain n'y est pas celle du sol, c'est celle de la montagne au-dessus.
        /// </summary>
        public bool UnderRelief(Vector3 p)
        {
            if (p.y > Height(p.x, p.z) - 1f) return false;
            return OverTunnel(Mathf.RoundToInt((p.x + Half) / Step), Mathf.RoundToInt((p.z + Half) / Step), 1);
        }

        /// <summary>
        /// Attributs du sol en un point quelconque, interpolés entre les nœuds : masques (couleur de sommet des tuiles)
        /// et annexes (pavé, neige : UV2 des tuiles). Sert aux maillages qui doivent se fondre dans le terrain
        /// (accotements des routes).
        /// </summary>
        public void Ground(float x, float z, out Color32 masks, out Vector2 extra)
        {
            float fx = Mathf.Clamp((x + Half) / Step, 0f, N - 1.001f), fz = Mathf.Clamp((z + Half) / Step, 0f, N - 1.001f);
            int ix = (int)fx, iz = (int)fz;
            float u = fx - ix, v = fz - iz;
            int i00 = (iz * N + ix) * 4, i10 = i00 + 4, i01 = i00 + N * 4, i11 = i01 + 4;
            float w00 = (1f - u) * (1f - v), w10 = u * (1f - v), w01 = (1f - u) * v, w11 = u * v;
            byte M(int k) => (byte)(Masks[i00 + k] * w00 + Masks[i10 + k] * w10 + Masks[i01 + k] * w01 + Masks[i11 + k] * w11 + 0.5f);
            float I(int k) => (Info[i00 + k] * w00 + Info[i10 + k] * w10 + Info[i01 + k] * w01 + Info[i11 + k] * w11) / 255f;
            masks = new Color32(M(0), M(1), M(2), M(3));
            extra = new Vector2(I(2), I(3));
        }
        public float Paved(float x, float z) => Info[Nearest(x, z) * 4 + 2] / 255f;
        /// <summary>Enneigement du sol (0-1) : sommets, et station de ski où les routes sont enneigées.</summary>
        public float Snow(float x, float z) => Info[Nearest(x, z) * 4 + 3] / 255f;
        public float Forest(float x, float z) => Masks[Nearest(x, z) * 4] / 255f;
        public float Dry(float x, float z) => Masks[Nearest(x, z) * 4 + 1] / 255f;
        public float Rock(float x, float z) => Masks[Nearest(x, z) * 4 + 2] / 255f;
    }
}
