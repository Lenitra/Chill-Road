p = "ShotDirector.cs"
src = open(p, encoding="utf-8").read()
def rep(old, new):
    global src
    assert old in src, old[:70]
    src = src.replace(old, new, 1)

# --- météo forcée
rep("        public bool sunLit;", "        public string weather = \"clear\";   // clear, storm\n        public bool sunLit;")
rep("            gm.Events.frequency = spec.events;", "            gm.Events.frequency = spec.events;\n            if (gm.Weather) gm.Weather.Force(spec.weather == \"storm\", true);")
rep("""                if (spec.police && gm.Events.Police == null && Time.time - lastShot > 3f) gm.Events.StartPolice("Patrouille de police !");""",
"""                if (spec.police && gm.Events.Police == null && Time.time - lastShot > 3f) gm.Events.StartPolice("Patrouille de police !");
                if (gm.Weather && gm.Weather.Stormy != (spec.weather == "storm")) gm.Weather.Force(spec.weather == "storm", true);""")

# --- garage : deux rangées au-delà de 5 voitures, les plus petites devant
old = src[src.index("            float spacing = road.KindAt(at) == RoadKind.Highway ? 3.4f : 2.7f;"):src.index("            rig.enabled = false;\n            shotsTaken = 0;\n            Vector3[] offsets")]
new = """            bool hw = road.KindAt(at) == RoadKind.Highway;
            float mid = hw ? road.LaneOffset(at, 1, 1) : 0f;
            float spacing = hw ? (cars.Length > 5 ? 3.1f : 3.4f) : 2.7f;
            var placed = new List<(GameObject go, float size, string name)>();
            for (int i = 0; i < cars.Length; i++)
            {
                var go = Instantiate(cars[i].prefab);
                go.transform.localScale = Vector3.one * cars[i].modelScale;
                foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
                Bounds b = new Bounds(go.transform.position, Vector3.zero);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                placed.Add((go, b.size.x * b.size.z, cars[i].displayName));
            }
            // Plus de 5 voitures : deux rangées, les plus petites devant (côté caméra)
            var order = new List<int>();
            for (int i = 0; i < placed.Count; i++) order.Add(i);
            int frontCount = placed.Count > 5 ? placed.Count / 2 : 0;
            if (frontCount > 0) order.Sort((a, c) => placed[a].size.CompareTo(placed[c].size));
            for (int k = 0; k < order.Count; k++)
            {
                var (go, _, name) = placed[order[k]];
                bool front = k < frontCount;
                int idx = front ? k : k - frontCount;
                int rowCount = front ? frontCount : order.Count - frontCount;
                float lat = mid + (idx - (rowCount - 1) * 0.5f) * spacing;
                float along = frontCount == 0 ? -Mathf.Abs(idx - (rowCount - 1) * 0.5f) * 1.2f : (front ? 3.8f : -3.8f);
                Vector3 p = road.LanePosition(at + along, lat, out var tg);
                float yaw = frontCount == 0 ? -8f + idx * 5f : -9f;
                go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(Vector3.ProjectOnPlane(tg, Vector3.up), Vector3.up) * Quaternion.Euler(0f, yaw, 0f));
                // Pose au sol : bas des roues sur la chaussée
                float minY = float.MaxValue;
                foreach (var r in go.GetComponentsInChildren<Renderer>()) minY = Mathf.Min(minY, r.bounds.min.y);
                float groundY = p.y;
                if (Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out var hit, 20f, ~(1 << 2))) groundY = hit.point.y;
                go.transform.position += Vector3.up * (groundY - minY);
                props.Add(go);
                Log($"lineup {name} lat {lat:0.0} along {along:0.0}");
            }
"""
src = src.replace(old, new)
open(p, "w", encoding="utf-8").write(src)
print("ok")
