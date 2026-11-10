using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShibuyaRush
{
    /// <summary>
    /// 街の小物。街路樹・街灯・歩行者用信号(青赤が変わる)・ネオン看板・ハチ公像。
    /// 木と街灯はOSMの位置に、足りない分は大通りの歩道沿いに補う。
    /// </summary>
    public class Props
    {
        struct Item { public Vector3 pos; public float yaw, scale; public int variant; }
        readonly List<Item> trees = new List<Item>(), lamps = new List<Item>(), signals = new List<Item>(); readonly List<int> signalEdge = new List<int>();
        readonly Mesh trunk, crown, pole, lampHead, sigHead, sigLamp; readonly Material trunkM, poleM, lampM, headM, greenM, redM; readonly Material[] leafM;
        readonly List<Matrix4x4>[] crownL; readonly List<Matrix4x4> trunkL = new List<Matrix4x4>(), poleL = new List<Matrix4x4>(), lampL = new List<Matrix4x4>(), headL = new List<Matrix4x4>(), greenL = new List<Matrix4x4>(), redL = new List<Matrix4x4>();
        CityBuilder cityRef; readonly Matrix4x4[] buf = new Matrix4x4[1000]; readonly PedGraph graph; public float DrawDist = 130f;
        public int TreeCount => trees.Count; public int LampCount => lamps.Count; public int SignalCount => signals.Count;

        static Material M(string n) => Resources.Load<Material>("Mats/" + n);

        public Props(CityData data, CityBuilder city, PedGraph graph, Transform parent, bool mobile)
        {
            this.graph = graph; cityRef = city; var hf = Heightfield.Current;
            trunk = HumanoidMesh.Capsule(0.13f, 2.6f, false, 8, 2); crown = HumanoidMesh.Sphere(0.5f); pole = HumanoidMesh.Capsule(0.07f, 5.2f, false, 8, 2);
            lampHead = HumanoidMesh.Sphere(0.22f); sigHead = HumanoidMesh.Box(new Vector3(0.34f, 0.62f, 0.26f), false); sigLamp = HumanoidMesh.Sphere(0.13f);
            trunkM = M("TreeTrunk"); poleM = M("Pole"); lampM = M("LampWarm"); headM = M("Pole"); greenM = M("SigGreen"); redM = M("SigRed");
            leafM = new[] { M("TreeLeaf0"), M("TreeLeaf1"), M("TreeLeaf2") }; crownL = new[] { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
            var rng = new System.Random(21);
            // OSMの木・街灯
            if (data.trees != null) for (int i = 0; i + 1 < data.trees.Length; i += 2) AddTree(new Vector2(data.trees[i], data.trees[i + 1]), rng, city);
            if (data.lamps != null) for (int i = 0; i + 1 < data.lamps.Length; i += 2) AddLamp(new Vector2(data.lamps[i], data.lamps[i + 1]), rng);
            // 大通り(車道に面した歩道)に木と街灯を補う
            float stepTree = mobile ? 34f : 17f, stepLamp = mobile ? 50f : 26f;
            foreach (var e in graph.edges)
            {
                if (!e.car || e.lA > 0.05f || e.lB > 0.05f || !graph.inMain[e.a]) continue;
                var a = graph.nodes[e.a]; var b = graph.nodes[e.b]; var d = e.dir; var n = new Vector2(-d.y, d.x);
                for (float s = 6f; s < e.len - 4f; s += stepTree)
                    foreach (float side in new[] { 1f, -1f })
                        if (rng.NextDouble() < (e.width >= 9f ? 0.55 : 0.3)) { var p = a + d * (s + (float)rng.NextDouble() * 4f) + n * side * (e.width * 0.5f + 0.4f); AddTree(p, rng, city); }
                for (float s = 10f; s < e.len - 4f; s += stepLamp)
                    foreach (float side in new[] { 1f, -1f })
                        if (rng.NextDouble() < 0.6) { var p = a + d * s + n * side * (e.width * 0.5f + 0.15f); if (!city.InsideBuilding(p.x, p.y)) AddLamp(p, rng); }
            }
            // 歩行者用信号: 信号つき横断歩道の両端に1本ずつ
            for (int id = 0; id < graph.edges.Count; id++)
            {
                var e = graph.edges[id]; if (e.sig == 0) continue;
                var a = graph.nodes[e.a]; var b = graph.nodes[e.b]; var n = new Vector2(-e.dir.y, e.dir.x);
                foreach (var (p, yaw) in new[] { (a + n * 1.6f - e.dir * 0.3f, Mathf.Atan2(e.dir.x, e.dir.y) * Mathf.Rad2Deg), (b + n * 1.6f + e.dir * 0.3f, Mathf.Atan2(-e.dir.x, -e.dir.y) * Mathf.Rad2Deg) })
                { if (city.InsideBuilding(p.x, p.y)) continue; city.AddSolid(p, 0.25f); signals.Add(new Item { pos = new Vector3(p.x, hf.Height(p.x, p.y) + 0, p.y), yaw = yaw, scale = 1f }); signalEdge.Add(id); }
            }
            BuildNeon(data, city, parent, mobile); BuildHachiko(city, parent);
        }

        void AddTree(Vector2 p, System.Random rng, CityBuilder city)
        {
            if (city.InsideBuilding(p.x, p.y) || city.Blocked(p.x, p.y, 0.5f)) return;
            city.AddSolid(p, 0.38f);
            trees.Add(new Item { pos = new Vector3(p.x, Heightfield.Current.Height(p.x, p.y), p.y), yaw = (float)rng.NextDouble() * 360f, scale = 0.85f + (float)rng.NextDouble() * 0.6f, variant = rng.Next(3) });
        }
        void AddLamp(Vector2 p, System.Random rng) { cityRef?.AddSolid(p, 0.2f); lamps.Add(new Item { pos = new Vector3(p.x, Heightfield.Current.Height(p.x, p.y), p.y), yaw = (float)rng.NextDouble() * 360f, scale = 1f }); }

        // ---------------- ネオン看板 ----------------
        void BuildNeon(CityData data, CityBuilder city, Transform parent, bool mobile)
        {
            const int NC = 8; var v = new List<Vector3>[NC]; var t = new List<int>[NC]; for (int i = 0; i < NC; i++) { v[i] = new List<Vector3>(); t[i] = new List<int>(); }
            var hf = Heightfield.Current; int placed = 0, max = mobile ? 700 : 3500;
            foreach (var b in data.buildings)
            {
                int n = b.p.Length / 2; if (n < 3 || b.h < 9f) continue;
                var poly = new Vector2[n]; for (int i = 0; i < n; i++) poly[i] = new Vector2(b.p[i * 2], b.p[i * 2 + 1]);
                float area = 0; for (int i = 0; i < n; i++) { var q = poly[(i + 1) % n]; area += poly[i].x * q.y - q.x * poly[i].y; } if (area < 0) System.Array.Reverse(poly);
                for (int i = 0; i < n && placed < max; i++)
                {
                    var a = poly[i]; var c = poly[(i + 1) % n]; var d = c - a; float L = d.magnitude; if (L < 5f) continue; d /= L; var outw = new Vector2(d.y, -d.x);
                    uint h = (uint)(Mathf.Abs(a.x * 73.1f) * 131f + Mathf.Abs(a.y * 51.7f) * 977f); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                    if (h % 100 >= 42) continue;
                    var mid = (a + c) * 0.5f; if (!graph.NearEdge(mid + outw * 3f, 5f)) continue;     // 道に面した壁だけ
                    float gy = hf.Height(mid.x, mid.y); float top = Mathf.Min(b.h - 1.5f, 34f);
                    float sh = 1.2f + (h % 7) * 0.75f, sw = Mathf.Min(L - 1f, 1.4f + (h % 5) * 0.9f); bool vertical = (h >> 3) % 3 == 0; if (vertical) { float tmp = sh; sh = sw * 0.5f + 1.5f; sw = Mathf.Min(1.3f, tmp); }
                    float cy = gy + 3.4f + ((h >> 5) % 100) / 100f * Mathf.Max(0.5f, top - 5.5f - sh * 0.5f); if (cy + sh / 2 > gy + top) continue;
                    float off = ((h >> 9) % 100) / 100f * Mathf.Max(0f, L - sw - 1f) + sw / 2 + 0.5f; var ctr = a + d * off + outw * 0.07f;
                    int col = (int)((h >> 11) % NC); var bl = new Vector3(ctr.x - d.x * sw / 2, cy - sh / 2, ctr.y - d.y * sw / 2); var br = new Vector3(ctr.x + d.x * sw / 2, cy - sh / 2, ctr.y + d.y * sw / 2);
                    int s0 = v[col].Count; v[col].Add(bl); v[col].Add(br); v[col].Add(br + Vector3.up * sh); v[col].Add(bl + Vector3.up * sh);
                    t[col].Add(s0); t[col].Add(s0 + 3); t[col].Add(s0 + 2); t[col].Add(s0); t[col].Add(s0 + 2); t[col].Add(s0 + 1); placed++;
                }
            }
            for (int i = 0; i < NC; i++)
            {
                if (v[i].Count == 0) continue;
                var m = new Mesh { indexFormat = IndexFormat.UInt32 }; m.SetVertices(v[i]); m.SetTriangles(t[i], 0); m.RecalculateNormals(); m.RecalculateBounds();
                var go = new GameObject("Neon" + i); go.transform.SetParent(parent); go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = M("Neon" + i); r.shadowCastingMode = ShadowCastingMode.Off;
            }
            NeonCount = placed;
        }
        public int NeonCount { get; private set; }

        // ---------------- ハチ公像 ----------------
        void BuildHachiko(CityBuilder city, Transform parent)
        {
            var stone = new List<CombineInstance>(); var bronze = new List<CombineInstance>();
            void Add(List<CombineInstance> l, Mesh m, Vector3 pos, Quaternion rot, Vector3 sc) => l.Add(new CombineInstance { mesh = m, transform = Matrix4x4.TRS(pos, rot, sc) });
            float top = 1.1f;
            Add(stone, HumanoidMesh.Box(new Vector3(2.4f, 0.28f, 2.4f), false), Vector3.zero, Quaternion.identity, Vector3.one);
            Add(stone, HumanoidMesh.Box(new Vector3(1.5f, top - 0.28f, 1.5f), false), new Vector3(0, 0.28f, 0), Quaternion.identity, Vector3.one);
            var body = HumanoidMesh.Capsule(0.23f, 1.0f, false, 12, 4); Add(bronze, body, new Vector3(0, top + 0.62f, -0.5f), Quaternion.Euler(90, 0, 0), Vector3.one);
            var leg = HumanoidMesh.Capsule(0.075f, 0.55f, true, 8, 3);
            foreach (var p in new[] { new Vector3(-0.13f, 0, 0.18f), new Vector3(0.13f, 0, 0.18f), new Vector3(-0.13f, 0, -0.4f), new Vector3(0.13f, 0, -0.4f) }) Add(bronze, leg, new Vector3(p.x, top + 0.62f, p.z), Quaternion.identity, Vector3.one);
            Add(bronze, HumanoidMesh.Sphere(0.22f), new Vector3(0, top + 0.95f, 0.38f), Quaternion.identity, Vector3.one);
            Add(bronze, HumanoidMesh.Capsule(0.09f, 0.26f, false, 8, 3), new Vector3(0, top + 0.88f, 0.42f), Quaternion.Euler(90, 0, 0), Vector3.one);
            var ear = HumanoidMesh.Capsule(0.055f, 0.2f, false, 8, 3);
            Add(bronze, ear, new Vector3(-0.13f, top + 1.1f, 0.34f), Quaternion.Euler(-5, 0, 38), Vector3.one); Add(bronze, ear, new Vector3(0.13f, top + 1.1f, 0.34f), Quaternion.Euler(-5, 0, -38), Vector3.one);
            Add(bronze, HumanoidMesh.Capsule(0.06f, 0.5f, false, 8, 3), new Vector3(0, top + 0.78f, -1.0f), Quaternion.Euler(-40, 0, 0), Vector3.one);
            foreach (var (list, mat) in new[] { (stone, "Stone"), (bronze, "Bronze") })
            {
                var m = new Mesh { indexFormat = IndexFormat.UInt32 }; m.CombineMeshes(list.ToArray(), true, true); m.RecalculateBounds();
                var go = new GameObject("Hachiko_" + mat); go.transform.SetParent(parent); go.transform.position = new Vector3(0, Heightfield.Current.Height(0, 0), 0); go.transform.rotation = Quaternion.Euler(0, 180, 0);
                go.AddComponent<MeshFilter>().sharedMesh = m; go.AddComponent<MeshRenderer>().sharedMaterial = M(mat);
            }
            city.AddObstacle(Vector2.zero, 1.25f);
        }

        // ---------------- 描画 ----------------
        public void Render(Vector3 camPos, float clock)
        {
            float d2max = DrawDist * DrawDist; var c = new Vector2(camPos.x, camPos.z);
            foreach (var t in trees)
            {
                if ((new Vector2(t.pos.x, t.pos.z) - c).sqrMagnitude > d2max) continue;
                var rot = Quaternion.Euler(0, t.yaw, 0);
                trunkL.Add(Matrix4x4.TRS(t.pos, rot, new Vector3(1, t.scale, 1)));
                crownL[t.variant].Add(Matrix4x4.TRS(t.pos + new Vector3(0, 2.7f * t.scale, 0), rot, new Vector3(3.0f, 2.5f, 3.0f) * t.scale));
            }
            foreach (var l in lamps)
            {
                if ((new Vector2(l.pos.x, l.pos.z) - c).sqrMagnitude > d2max) continue;
                poleL.Add(Matrix4x4.TRS(l.pos, Quaternion.identity, Vector3.one)); lampL.Add(Matrix4x4.TRS(l.pos + new Vector3(0, 5.2f, 0), Quaternion.identity, new Vector3(1f, 0.7f, 1f)));
            }
            for (int i = 0; i < signals.Count; i++)
            {
                var s = signals[i]; if ((new Vector2(s.pos.x, s.pos.z) - c).sqrMagnitude > d2max) continue;
                var rot = Quaternion.Euler(0, s.yaw, 0); float lf = graph.edges[signalEdge[i]].lA;
                poleL.Add(Matrix4x4.TRS(s.pos + new Vector3(0, lf, 0), rot, new Vector3(1f, 0.6f, 1f)));
                headL.Add(Matrix4x4.TRS(s.pos + new Vector3(0, 2.7f + lf, 0), rot, Vector3.one));
                var lamp = Matrix4x4.TRS(s.pos + new Vector3(0, 3.05f + lf, 0) + rot * new Vector3(0, 0, 0.14f), rot, Vector3.one);
                (graph.Green(signalEdge[i], clock) ? greenL : redL).Add(lamp);
            }
            for (int v = 0; v < 3; v++) Draw(crown, leafM[v], crownL[v], true);
            Draw(trunk, trunkM, trunkL, false); Draw(pole, poleM, poleL, false); Draw(lampHead, lampM, lampL, false); Draw(sigHead, headM, headL, false);
            Draw(sigLamp, greenM, greenL, false); Draw(sigLamp, redM, redL, false);
        }

        void Draw(Mesh m, Material mat, List<Matrix4x4> list, bool shadow)
        {
            var rp = new RenderParams(mat) { shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true, layer = 0 };
            for (int i = 0; i < list.Count; i += buf.Length) { int n = Mathf.Min(buf.Length, list.Count - i); list.CopyTo(i, buf, 0, n); Graphics.RenderMeshInstanced(rp, m, 0, buf, n); }
            list.Clear();
        }
    }
}
