p = "ShotDirector.cs"
src = open(p, encoding="utf-8").read()


def rep(old, new):
    global src
    assert old in src, "MISSING: " + old[:80]
    src = src.replace(old, new, 1)


# ---- champs du plan
rep('        public string weather = "clear";   // clear, storm\n',
    '        public string weather = "clear";   // clear, storm\n'
    '        public int car2 = -1;                // écran partagé : voiture du joueur 2\n'
    '        public string drive2 = "";          // écran partagé : conduite du joueur 2 (vide = comme le joueur 1, pair = colle au joueur 1)\n')
rep("    public class ShotPlan { public int seed = 1234; public ShotSpec[] shots; }",
    "    public class ShotPlan { public int seed = 1234; public bool split; public ShotSpec[] shots; }")

# ---- état de conduite par voiture
rep("""        bool driving;
        int lane;
        float laneOffset, laneTimer, s;""",
    """        bool driving;
        int lane;
        float laneOffset, laneTimer, s;
        class DriveState { public int lane; public float laneOffset, laneTimer; }
        readonly DriveState[] states = { new DriveState(), new DriveState() };
        // Autre joueur (écran partagé) : obstacle pour le slalom
        bool hasOther;
        float otherS, otherLat;
        int slotIndex;
        bool Split => gm != null && gm.Slots.Count > 1;
        Camera Cam2 => Split && gm.Slots[1].Rig ? gm.Slots[1].Rig.GetComponent<Camera>() : null;
        void ProtectAll() { foreach (var sl in gm.Slots) sl.Score.Protect(1e6f); }""")

# ---- démarrage : écran partagé avec deux manettes virtuelles
rep("""            gm.StartCountdown();
            while (gm.State != GameManager.Phase.Playing) yield return null;
            yield return new WaitForSeconds(0.5f);
            HookHud();
            gm.Score.Protect(1e6f);""",
    """            if (plan.split)
            {
                UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>("ShotPad1");
                UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>("ShotPad2");
                yield return null;
                bool ok = gm.StartSplitScreen();
                Log($"split start={ok} slots={gm.Slots.Count}");
                if (!ok) gm.StartCountdown();
                if (Split) gm.Slots[1].Score.NearMiss += (p, g, o) => { pendingNearMiss = true; nearMissAt = Time.time; };
            }
            else gm.StartCountdown();
            while (gm.State != GameManager.Phase.Playing) yield return null;
            yield return new WaitForSeconds(0.5f);
            HookHud();
            ProtectAll();""")
rep('            gm.Score.Protect(1e6f);\n            driving = spec.drive != "none";',
    '            ProtectAll();\n            driving = spec.drive != "none";')

# ---- saut : les deux voitures
rep("""            var car = gm.Player;
            gm.TeleportTo(at);
            car.Body.isKinematic = true;
            gm.Traffic.ClearAll();
            for (float t = 0f; t < 3f; t += Time.deltaTime) yield return null;
            s = gm.PlayerS;
            int lanes = road.LanesAt(s, 1);
            lane = spec.road == "Highway" || lanes >= 3 ? 1 : lanes - 1;
            laneOffset = road.LaneOffset(s, lane, 1);
            Vector3 p = road.LanePosition(s, laneOffset, out var tangent);
            car.Body.isKinematic = false;
            car.Teleport(p + Vector3.up * 0.4f, Quaternion.LookRotation(tangent, Vector3.up), spec.kmh / 3.6f * 0.8f);""",
    """            var car = gm.Player;
            gm.TeleportTo(at);
            car.Body.isKinematic = true;
            if (Split) gm.Slots[1].Car.Body.isKinematic = true;
            gm.Traffic.ClearAll();
            for (float t = 0f; t < 3f; t += Time.deltaTime)
            {
                if (Split) gm.Slots[1].Car.Body.isKinematic = true;
                yield return null;
            }
            s = gm.PlayerS;
            int lanes = road.LanesAt(s, 1);
            int lane1 = spec.road == "Highway" || lanes >= 3 ? 1 : lanes - 1;
            float off1 = road.LaneOffset(s, lane1, 1);
            Vector3 p = road.LanePosition(s, off1, out var tangent);
            car.Body.isKinematic = false;
            car.Teleport(p + Vector3.up * 0.4f, Quaternion.LookRotation(tangent, Vector3.up), spec.kmh / 3.6f * 0.8f);
            states[0].lane = lane1; states[0].laneOffset = off1; states[0].laneTimer = 0f;
            if (Split)
            {
                // Joueur 2 : voie voisine un peu en retrait (nationale : même voie, derrière)
                int lane2 = lanes >= 2 ? (lane1 + 1 < lanes ? lane1 + 1 : lane1 - 1) : lane1;
                float back = lanes >= 2 ? 7f : 16f;
                float s2 = s - back;
                float off2 = road.LaneOffset(s2, lane2, 1);
                Vector3 p2 = road.LanePosition(s2, off2, out var tan2);
                var c2 = gm.Slots[1].Car;
                c2.Body.isKinematic = false;
                c2.Teleport(p2 + Vector3.up * 0.4f, Quaternion.LookRotation(tan2, Vector3.up), spec.kmh / 3.6f * 0.8f);
                gm.Slots[1].S = s2;
                states[1].lane = lane2; states[1].laneOffset = off2; states[1].laneTimer = 0f;
            }""")

# ---- réglages : voiture du joueur 2, caméras
rep("""            SetFx(spec.fx);
            bool rigCam = spec.cam == "chase" || spec.cam == "chasefar" || spec.cam == "hood" || spec.cam == "bumper";
            rig.enabled = rigCam;
            if (rigCam) rig.SetView(spec.cam == "chase" ? 0 : spec.cam == "chasefar" ? 1 : spec.cam == "hood" ? 2 : 3);""",
    """            if (Split && spec.car2 >= 0)
            {
                var p2 = gm.Slots[1];
                int n = gm.assets.playerCars.Length;
                int delta = ((spec.car2 - p2.CarIndex) % n + n) % n;
                if (delta != 0)
                {
                    var m = typeof(GameManager).GetMethod("CycleCar", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(PlayerSlot), typeof(int), typeof(bool) }, null);
                    if (m != null) m.Invoke(gm, new object[] { p2, delta, false });
                    else Log("CycleCar(slot) introuvable");
                }
            }
            SetFx(spec.fx);
            bool rigCam = spec.cam == "chase" || spec.cam == "chasefar" || spec.cam == "hood" || spec.cam == "bumper";
            int view = spec.cam == "chase" ? 0 : spec.cam == "chasefar" ? 1 : spec.cam == "hood" ? 2 : 3;
            rig.enabled = rigCam;
            if (rigCam) rig.SetView(view);
            var cam2 = Cam2;
            if (cam2)
            {
                // Plans de cinéma : une seule caméra plein cadre ; sinon l'écran partagé du jeu
                cam2.enabled = rigCam;
                gm.Slots[1].Rig.enabled = rigCam;
                if (rigCam) gm.Slots[1].Rig.SetView(view);
                cam.rect = rigCam ? new Rect(0f, 0.5f, 1f, 0.5f) : new Rect(0f, 0f, 1f, 1f);
            }""")

# ---- conduite : boucle sur les voitures
rep("""        void Update()
        {
            if (!driving || gm == null || gm.State != GameManager.Phase.Playing) return;
            var car = gm.Player;
            var road = gm.Road;
            car.InputEnabled = false;
            car.Autopilot = true;
            float dt = Time.deltaTime;
            float kmh = car.SpeedKmh;
            s = gm.PlayerS;""",
    """        void Update()
        {
            if (!driving || gm == null || gm.State != GameManager.Phase.Playing) return;
            for (int i = 0; i < gm.Slots.Count && i < 2; i++)
            {
                var st = states[i];
                lane = st.lane; laneOffset = st.laneOffset; laneTimer = st.laneTimer;
                slotIndex = i;
                s = gm.Slots[i].S;
                hasOther = Split;
                if (hasOther)
                {
                    var o = gm.Slots[1 - i];
                    otherS = o.S;
                    otherLat = o.Lateral;
                }
                DriveOne(gm.Slots[i].Car);
                st.lane = lane; st.laneOffset = laneOffset; st.laneTimer = laneTimer;
            }
        }

        void DriveOne(VehicleController car)
        {
            var road = gm.Road;
            car.InputEnabled = false;
            car.Autopilot = true;
            float dt = Time.deltaTime;
            float kmh = car.SpeedKmh;
            string mode = slotIndex == 1 && !string.IsNullOrEmpty(spec.drive2) ? spec.drive2 : spec.drive;
            float targetKmh = spec.kmh;""")

# mode pair : le joueur 2 colle au joueur 1 dans la voie voisine
rep("""            else
            {
                // Slalom agressif : voie la plus dégagée parmi les voisines libres sur le côté""",
    """            else if (mode == "pair" && Split)
            {
                var lead = gm.Slots[0];
                int lanesHere = road.LanesAt(s, 1);
                int leadLane = Mathf.Clamp(states[0].lane, 0, lanesHere - 1);
                lane = lanesHere >= 2 ? (leadLane + 1 < lanesHere ? leadLane + 1 : leadLane - 1) : leadLane;
                float gapWanted = lanesHere >= 2 ? -6f : -16f;
                float err = lead.S + gapWanted - s;
                targetKmh = Mathf.Clamp(lead.Car.SpeedKmh + err * 1.8f, 30f, spec.kmh + 40f);
                float baseOff = road.LaneOffset(s, lane, 1);
                laneOffset = Mathf.MoveTowards(laneOffset, baseOff, 6f * dt);
                float cur = Free(lane, false);
                closeAhead = cur < car.Speed * 0.9f + 6f;
                liftAhead = cur < car.Speed * 1.5f + 8f;
            }
            else
            {
                // Slalom agressif : voie la plus dégagée parmi les voisines libres sur le côté""")
rep('                if (spec.drive == "weave") baseOff += NearBias(baseOff);',
    '                if (mode == "weave") baseOff += NearBias(baseOff);')
rep('            if (spec.drive == "oncoming" && road.KindAt(s) != RoadKind.Highway)',
    '            if (mode == "oncoming" && road.KindAt(s) != RoadKind.Highway)')
rep('            if (spec.drive == "oncoming") shape = 0f;',
    '            if (mode == "oncoming") shape = 0f;')
rep("            float safeKmh = curve > 1f ? Mathf.Clamp(3600f / curve, 90f, spec.kmh) : spec.kmh;",
    "            float safeKmh = curve > 1f ? Mathf.Clamp(3600f / curve, 90f, targetKmh) : targetKmh;")

# ---- l'autre joueur compte comme un obstacle
rep("""                if (ds < best) best = ds;
            }
            return best;""",
    """                if (ds < best) best = ds;
            }
            if (hasOther && Mathf.Abs(otherLat - off) < 2.4f)
            {
                float ds = otherS - s;
                if (checkSide && ds > -8f && ds < 9f) return -1f;
                if (ds >= 3f && ds < best) best = ds;
            }
            return best;""")
open(p, "w", encoding="utf-8").write(src)
print("patched")
