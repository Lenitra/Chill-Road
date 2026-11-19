namespace ChillRoad
{
    /// <summary>
    /// Packs de contenu : des ajouts thématiques (voitures, activités, régions de la carte) livrés avec les mises à
    /// jour du jeu. Un pack n'est pas un téléchargement à part ni un achat : il regroupe et date ce qui arrive ensemble,
    /// pour le présenter au joueur (« Pack Rallye ») et pour vérifier qu'en réseau tout le monde a le même contenu.
    ///
    /// Ajouter un pack :
    /// 1. une entrée dans <see cref="All"/> (identifiant court et stable, jamais renommé ensuite) ;
    /// 2. voitures : fiche dans Editor/PlayerCarsImport.cs avec <c>pack = "&lt;id&gt;"</c>, prix dans <see cref="Wallet"/>,
    ///    import (ChillRoad > Voitures jouables) : elles s'ajoutent en fin de garage ;
    /// 3. courses : <c>pack = "&lt;id&gt;"</c> sur leur Def dans Gameplay/Races.cs ;
    /// 4. tremplins et radars : clé <c>"pack"</c> dans les tables de Tools/Map/map_stunts.py et map_radars.py ;
    /// 5. région de la carte : agrandir HALF dans Tools/Map/map_def.py, y tracer la région, puis relancer map_bake.py.
    /// Le contenu sans pack (champ vide) appartient au jeu de base.
    /// </summary>
    public static class ContentPacks
    {
        public sealed class Pack
        {
            public string id, fr, en;
            /// <summary>Date de sortie (AAAA-MM-JJ), pour l'ordre d'affichage.</summary>
            public string date;
            public string Name => Loc.T(fr, en);
        }

        public const string Base = "base";

        /// <summary>Packs connus, du plus ancien au plus récent ; le premier est le jeu de base.</summary>
        public static readonly Pack[] All =
        {
            new Pack { id = Base, fr = "Jeu de base", en = "Base game", date = "2026-11-17" },
        };

        /// <summary>Pack d'un identifiant (vide ou inconnu : le jeu de base).</summary>
        public static Pack Find(string id)
        {
            if (!string.IsNullOrEmpty(id))
                foreach (var p in All)
                    if (p.id == id) return p;
            return All[0];
        }

        /// <summary>Vrai pour du contenu ajouté par un pack (pas le jeu de base).</summary>
        public static bool IsExtra(string id) => Find(id) != All[0];

        /// <summary>
        /// Empreinte du contenu qui voyage par indice sur le réseau (voitures du garage, dans l'ordre) et des packs
        /// installés : deux jeux qui ne l'ont pas égale ne peuvent pas jouer ensemble (une voiture serait prise pour
        /// une autre). FNV-1a 32 bits, stable d'une plateforme à l'autre.
        /// </summary>
        public static uint Signature(GameAssets assets)
        {
            if (assets == null) return 0;
            if (signature != 0 && signedCars == assets.playerCars) return signature;
            uint h = 2166136261u;
            void Mix(string s)
            {
                foreach (char c in s ?? "") { h ^= c; h *= 16777619u; }
                h ^= 0x1f; h *= 16777619u; // séparateur
            }
            foreach (var p in All) Mix(p.id);
            if (assets.playerCars != null) foreach (var c in assets.playerCars) Mix(c != null ? c.id : "");
            signedCars = assets.playerCars;
            return signature = h == 0 ? 1u : h;
        }

        static uint signature;
        static VehicleSpec[] signedCars;

        /// <summary>Indice d'une voiture du garage d'après son identifiant (-1 si absente).</summary>
        public static int CarIndex(VehicleSpec[] cars, string id)
        {
            if (cars == null || string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < cars.Length; i++)
                if (cars[i] != null && cars[i].id == id) return i;
            return -1;
        }
    }
}
