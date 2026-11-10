using System.Collections.Generic;
using UnityEngine;

namespace ShibuyaRush
{
    /// <summary>道路中心線から作る歩行者グラフ。人混みの経路(ハブへの最短経路表)とBot用の経路探索に使う。</summary>
    public class PedGraph
    {
        public struct Edge { public int a, b; public float len, width; public Vector2 dir; public int sig; public float lA, lB; public bool car; }  // sig: 0=信号なし、1以上=信号つき横断歩道(位相オフセット秒+1)
        public readonly List<Vector2> nodes = new List<Vector2>();
        public readonly List<Edge> edges = new List<Edge>();
        public readonly List<float> nodeLift = new List<float>();
        public readonly List<List<int>> adj = new List<List<int>>(); // node -> edge ids
        readonly Dictionary<long, int> keyToNode = new Dictionary<long, int>();
        public bool[] inMain;                       // 最大連結成分に属するノード
        public int[] hubNodes; public int[][] nextEdge; // nextEdge[hub][node] = hubへ向かうために進む辺
        readonly Dictionary<long, List<int>> edgeGrid = new Dictionary<long, List<int>>();
        const float GridCell = 20f;
        public const int SignalCycle = 90; public const float GreenSec = 38f;
        /// <summary>この辺(信号つき横断歩道)が、時刻tに歩行者青か。</summary>
        public bool Green(int edge, float t) { int s = edges[edge].sig; return s == 0 || ((t + s - 1) % SignalCycle) < GreenSec; }

        static long K(Vector2 p) => ((long)Mathf.RoundToInt(p.x * 2f) << 32) ^ (uint)Mathf.RoundToInt(p.y * 2f);
        static long GK(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        int Node(Vector2 p)
        {
            long k = K(p);
            if (keyToNode.TryGetValue(k, out int id)) return id;
            id = nodes.Count; nodes.Add(p); nodeLift.Add(0f); adj.Add(new List<int>()); keyToNode[k] = id; return id;
        }

        void AddEdge(int na, int nb, float width, bool signal = false, float lA = 0f, float lB = 0f, bool car = false)
        {
            var a = nodes[na]; var b = nodes[nb]; float len = (b - a).magnitude;
            int id = edges.Count;
            int sig = 0;
            if (signal)
            {   // 近くの横断歩道は同じ位相にそろえる(80mの格子ごと)。大勢が同時に待ち、同時に渡る
                var m = (a + b) * 0.5f; int cx = Mathf.FloorToInt(m.x / 80f), cz = Mathf.FloorToInt(m.y / 80f);
                sig = 1 + (int)((uint)((cx * 73856093) ^ (cz * 19349663)) % (uint)SignalCycle);
            }
            edges.Add(new Edge { a = na, b = nb, len = len, width = width, dir = len > 1e-4f ? (b - a) / len : Vector2.up, sig = sig, lA = lA, lB = lB, car = car });
            nodeLift[na] = Mathf.Max(nodeLift[na], lA); nodeLift[nb] = Mathf.Max(nodeLift[nb], lB);
            adj[na].Add(id); adj[nb].Add(id);
        }

        /// <summary>車が走る道(自動車道・一般道)。</summary>
        public static bool IsCarRoad(RoadData r)
        {
            switch (r.cls)
            {
                case "motorway": case "trunk": case "primary": case "secondary": case "tertiary": case "unclassified": return true;
                case "residential": return r.w >= 4.5f;
                default: return false;
            }
        }

        public PedGraph(CityData city, bool vehicles = false)
        {
            foreach (var r in city.roads)
            {
                if (vehicles ? (!IsCarRoad(r) || r.tunnel) : (!r.walk || r.tunnel || r.cls == "steps")) continue;
                float L = CityData.PolyLen(r.p), run = 0f;
                for (int i = 0; i + 3 < r.p.Length; i += 2)
                {
                    var a = new Vector2(r.p[i], r.p[i + 1]); var b = new Vector2(r.p[i + 2], r.p[i + 3]);
                    float seg = (b - a).magnitude; float s0 = run; run += seg;
                    if (seg < 0.5f) continue;
                    if (r.lift > 0f && seg > 2.6f)
                    {   // 高架は、描かれる床(なめらかな坂)と人の高さがずれないよう、2.5m以下に刻んで高さを合わせる
                        int pc = Mathf.CeilToInt(seg / 2.5f); int prevN = Node(a); float prevS = s0;
                        for (int k = 1; k <= pc; k++)
                        {
                            var pk = Vector2.Lerp(a, b, (float)k / pc); float sk = s0 + seg * k / pc; int nk = k == pc ? Node(b) : Node(pk);
                            if (nk != prevN) AddEdge(prevN, nk, r.w, !vehicles && r.sig == 1, CityData.Lift(r, prevS, L), CityData.Lift(r, sk, L), false);
                            prevN = nk; prevS = sk;
                        }
                        continue;
                    }
                    int na = Node(a), nb = Node(b); if (na == nb) continue;
                    AddEdge(na, nb, r.w, !vehicles && r.sig == 1, CityData.Lift(r, s0, L), CityData.Lift(r, run, L), !vehicles && IsCarRoad(r) && r.w >= 6f && r.lift <= 0f);
                }
            }
            // 階段: 両端がすでにグラフにあるものだけ、両端の高さをつなぐ斜面として加える
            foreach (var r in city.roads)
            {
                if (vehicles) break;
                if (r.cls != "steps" || r.tunnel || r.p.Length < 4) continue;
                if (!keyToNode.TryGetValue(K(new Vector2(r.p[0], r.p[1])), out int sa)) continue;
                if (!keyToNode.TryGetValue(K(new Vector2(r.p[r.p.Length - 2], r.p[r.p.Length - 1])), out int sb)) continue;
                if (sa == sb) continue;
                AddEdge(sa, sb, Mathf.Max(r.w, 2.5f), false, nodeLift[sa], nodeLift[sb]); stairEdges.Add(edges.Count - 1);
            }
            BridgeNearNodes(2.5f);
            FindMainComponent();
            BuildEdgeGrid();
        }

        /// <summary>
        /// 建物の中を通ってしまう道の辺を取り除く(車道が建物の下を通るデータなど)。人も車も建物を通り抜けて歩かなくなる。
        /// 高架の辺は対象外。onlyCar=trueなら歩行者グラフの車道の辺だけ(歩行者通路・駅構内の通路は残す)。
        /// </summary>
        public int Prune(System.Func<float, float, bool> insideBuilding, bool onlyCar)
        {
            int removed = 0;
            for (int id = 0; id < edges.Count; id++)
            {
                var e = edges[id]; if (e.width <= 0f || e.lA > 0.05f || e.lB > 0.05f || (onlyCar && !e.car)) continue;
                var a = nodes[e.a]; int inside = 0;
                for (float t = 1.5f; t < e.len - 1.5f; t += 1.5f) { var p = a + e.dir * t; if (insideBuilding(p.x, p.y)) inside++; }
                if (inside < 2 && !(e.len < 6f && inside >= 1)) continue;   // 建物の角をかすめる程度は残す
                adj[e.a].Remove(id); adj[e.b].Remove(id); e.width = 0f; edges[id] = e; removed++;
            }
            if (removed > 0) { FindMainComponent(); edgeGrid.Clear(); BuildEdgeGrid(); }
            return removed;
        }

        /// <summary>ほぼ接している別ノードどうしを短い辺でつなぐ(OSMの座標ずれで道が切れるのを防ぐ)。</summary>
        void BridgeNearNodes(float r)
        {
            var g = new Dictionary<long, List<int>>();
            for (int i = 0; i < nodes.Count; i++)
            {
                long k = GK(Mathf.FloorToInt(nodes[i].x / r), Mathf.FloorToInt(nodes[i].y / r));
                if (!g.TryGetValue(k, out var l)) g[k] = l = new List<int>(); l.Add(i);
            }
            int n0 = nodes.Count;
            for (int i = 0; i < n0; i++)
            {
                int cx = Mathf.FloorToInt(nodes[i].x / r), cz = Mathf.FloorToInt(nodes[i].y / r);
                for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                    if (g.TryGetValue(GK(cx + dx, cz + dz), out var l))
                        foreach (int j in l)
                            if (j > i && (nodes[i] - nodes[j]).sqrMagnitude < r * r && !Connected(i, j) && Mathf.Abs(nodeLift[i] - nodeLift[j]) < 1f) AddEdge(i, j, 3f, false, nodeLift[i], nodeLift[j]);
            }
        }
        bool Connected(int i, int j) { foreach (int e in adj[i]) if (Other(e, i) == j) return true; return false; }

        void FindMainComponent()
        {
            var comp = new int[nodes.Count]; System.Array.Fill(comp, -1);
            int best = -1, bestSize = 0, c = 0; var stack = new Stack<int>();
            for (int s = 0; s < nodes.Count; s++)
            {
                if (comp[s] >= 0 || adj[s].Count == 0) continue;
                int size = 0; stack.Push(s); comp[s] = c;
                while (stack.Count > 0)
                {
                    int u = stack.Pop(); size++;
                    foreach (int e in adj[u]) { int v = Other(e, u); if (comp[v] < 0) { comp[v] = c; stack.Push(v); } }
                }
                if (size > bestSize) { bestSize = size; best = c; }
                c++;
            }
            inMain = new bool[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) inMain[i] = comp[i] == best;
            MainCount = bestSize;
        }
        public int MainCount { get; private set; }

        void BuildEdgeGrid()
        {
            for (int id = 0; id < edges.Count; id++)
            {
                if (!inMain[edges[id].a]) continue;
                var a = nodes[edges[id].a]; var b = nodes[edges[id].b];
                int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / GridCell), x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / GridCell);
                int z0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) / GridCell), z1 = Mathf.FloorToInt(Mathf.Max(a.y, b.y) / GridCell);
                for (int cx = x0; cx <= x1; cx++) for (int cz = z0; cz <= z1; cz++)
                {
                    long k = GK(cx, cz); if (!edgeGrid.TryGetValue(k, out var l)) edgeGrid[k] = l = new List<int>(); l.Add(id);
                }
            }
        }

        /// <summary>
        /// 建物の中を通れる場所か。駅のデッキや階段(高架)の下をくぐる所と、建物の中を抜ける細い歩行者通路(中心線から1.6m以内)だけ。
        /// 車道や路地の幅に合わせて広く許すと、細い道に面した建物の壁をすり抜けられてしまう。
        /// </summary>
        bool OnCarRoad(Vector2 p)
        {
            if (!edgeGrid.TryGetValue(GK(Mathf.FloorToInt(p.x / GridCell), Mathf.FloorToInt(p.y / GridCell)), out var l)) return false;
            foreach (int id in l)
            {
                var e = edges[id]; if (!e.car || e.width <= 0f) continue; var a = nodes[e.a]; var ab = nodes[e.b] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f)); if ((p - (a + ab * t)).magnitude < e.width * 0.5f) return true;
            }
            return false;
        }

        /// <summary>
        /// 道のつなぎ目の隙間をふさぐ: 近い(maxDist以内)のに辺でつながっていない地上のノードどうしを、
        /// 間がずっと歩ける範囲(建物なし・車道を横切らない)なら辺でつなぐ。道の座標ずれや広場のせいで、通れる所なのに経路が遠回りやUターンになるのを防ぐ。
        /// </summary>
        public int Heal(float maxDist, System.Func<float, float, bool> blocked)
        {
            var cells = new Dictionary<long, List<int>>(); float cs = maxDist;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!inMain[i] || nodeLift[i] > 0.3f) continue;
                long k = GK(Mathf.FloorToInt(nodes[i].x / cs), Mathf.FloorToInt(nodes[i].y / cs)); if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<int>(); l.Add(i);
            }
            int added = 0; int n0 = nodes.Count;
            for (int i = 0; i < n0; i++)
            {
                if (!inMain[i] || nodeLift[i] > 0.3f) continue; int cx = Mathf.FloorToInt(nodes[i].x / cs), cz = Mathf.FloorToInt(nodes[i].y / cs);
                for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                    if (cells.TryGetValue(GK(cx + dx, cz + dz), out var l))
                        foreach (int j in l)
                        {
                            if (j <= i) continue; var a = nodes[i]; var b = nodes[j]; float d = (b - a).magnitude; if (d < 2.5f || d > maxDist || Connected(i, j)) continue;
                            bool near = false; foreach (int e in adj[i]) if (Connected(Other(e, i), j)) { near = true; break; }   // 1つ挟んでつながっているなら不要
                            if (near) continue;
                            bool ok = true;
                            for (float s = 0.5f; s < d && ok; s += 1f) { var p = Vector2.Lerp(a, b, s / d); if (blocked(p.x, p.y) || !InCorridor(p.x, p.y, 0f) || OnCarRoad(p)) ok = false; }
                            if (ok) { AddEdge(i, j, 3f, false, 0f, 0f, false); added++; }
                        }
            }
            if (added > 0) { FindMainComponent(); edgeGrid.Clear(); BuildEdgeGrid(); }
            return added;
        }

        public float[] freeL, freeR;   // 各辺の中心から左右の壁までの空き幅(最大12m)。人もプレイヤーも、この範囲の中だけを歩く

        /// <summary>各辺について、中心から左右に建物へぶつかるまでの距離を測る。高架の辺は道幅のまま。</summary>
        public void ComputeFree(System.Func<float, float, bool> blocked)
        {
            freeL = new float[edges.Count]; freeR = new float[edges.Count];
            for (int id = 0; id < edges.Count; id++)
            {
                var e = edges[id]; if (e.width <= 0f || !inMain[e.a]) continue;
                float half = Mathf.Max(0.3f, (e.width - 1f) * 0.5f); freeL[id] = freeR[id] = half;
                if (blocked == null || e.lA > 0.05f || e.lB > 0.05f) continue;
                var a = nodes[e.a]; var n = new Vector2(-e.dir.y, e.dir.x);
                for (int side = 0; side < 2; side++)
                {
                    float ext = 12f; float sg = side == 0 ? 1f : -1f;
                    foreach (float f in new[] { 0.2f, 0.5f, 0.8f })
                    {
                        var c = a + e.dir * (e.len * f);
                        for (float d = 0.5f; d <= ext; d += 1f) { var q = c + n * sg * d; if (blocked(q.x, q.y)) { ext = Mathf.Max(0.3f, d - 1f); break; } }
                    }
                    if (side == 0) freeL[id] = Mathf.Max(ext, half * 0.6f); else freeR[id] = Mathf.Max(ext, half * 0.6f);
                }
            }
        }

        /// <summary>(x,z)が、道のまわりの歩ける範囲(道の左右の空き幅)の中か。範囲の外は、人が出ない場所なのでプレイヤーも入れない。</summary>
        public bool InCorridor(float x, float z, float lift)
        {
            if (freeL == null) return true;
            if (!edgeGrid.TryGetValue(GK(Mathf.FloorToInt(x / GridCell), Mathf.FloorToInt(z / GridCell)), out var l)) return false;
            var p = new Vector2(x, z);
            foreach (int id in l)
            {
                var e = edges[id]; if (e.width <= 0f) continue; var a = nodes[e.a]; var ab = nodes[e.b] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f)); var q = a + ab * t;
                if (Mathf.Abs(Mathf.Lerp(e.lA, e.lB, t) - lift) > 2.2f) continue;
                float side = Vector2.Dot(p - q, new Vector2(-e.dir.y, e.dir.x)) >= 0f ? freeL[id] : freeR[id];
                if ((p - q).magnitude <= side + 0.3f) return true;
            }
            return false;
        }

        /// <summary>(x,z)の頭上にある高架の床までの余裕(m)。床の下をくぐる坂や階段で、頭が突き抜けないよう縮めるために使う。</summary>
        public float Clearance(float x, float z, float lift)
        {
            float best = 99f;
            if (!edgeGrid.TryGetValue(GK(Mathf.FloorToInt(x / GridCell), Mathf.FloorToInt(z / GridCell)), out var l)) return best;
            var p = new Vector2(x, z);
            foreach (int id in l)
            {
                var e = edges[id]; if (e.width <= 0f || (e.lA < 0.05f && e.lB < 0.05f)) continue;
                var a = nodes[e.a]; var ab = nodes[e.b] - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                if ((p - (a + ab * t)).magnitude > Mathf.Max(e.width * 0.5f, 1.5f) + 0.6f) continue;
                float lv = Mathf.Lerp(e.lA, e.lB, t); if (lv > lift + 1.0f) best = Mathf.Min(best, lv - 0.7f - lift);   // 床の厚み分を引く
            }
            return best;
        }

        public float walkLift;   // いま判定している人の高さ(2階にいる人にだけ、2階の床の下の建物の例外を認める)

        public bool OnWalkway(float x, float z)
        {
            if (!edgeGrid.TryGetValue(GK(Mathf.FloorToInt(x / GridCell), Mathf.FloorToInt(z / GridCell)), out var l)) return false;
            var p = new Vector2(x, z);
            foreach (int id in l)
            {
                var e = edges[id]; var a = nodes[e.a]; var ab = nodes[e.b] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                float d = (p - (a + ab * t)).magnitude;
                bool deck = e.lA > 0.05f || e.lB > 0.05f;
                if (deck && d < Mathf.Max(e.width * 0.5f, 1.5f) + 0.5f && Mathf.Abs(Mathf.Lerp(e.lA, e.lB, t) - walkLift) < 2.2f) return true;   // 地上の道は、壁の通り抜けを一切許さない
            }
            return false;
        }

        /// <summary>歩ける高さ。(x,z)付近の道のうち、今の高さcurから1.2m以内で登れる最も高い面(なければ地面0)。</summary>
        public float ReachableLift(float x, float z, float cur)
        {
            float best = 0f;
            if (!edgeGrid.TryGetValue(GK(Mathf.FloorToInt(x / GridCell), Mathf.FloorToInt(z / GridCell)), out var l)) return best;
            var p = new Vector2(x, z);
            foreach (int id in l)
            {
                var e = edges[id]; if (e.lA < 0.05f && e.lB < 0.05f) continue;
                var a = nodes[e.a]; var ab = nodes[e.b] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                if ((p - (a + ab * t)).magnitude > Mathf.Max(e.width * 0.5f, 1.5f) + 0.3f) continue;
                float lv = Mathf.Lerp(e.lA, e.lB, t);
                if (lv <= cur + 1.2f && lv > best) best = lv;
            }
            return best;
        }

        /// <summary>点pから半径r(最大20m)以内に歩行者グラフの道があるか。</summary>
        public bool NearEdge(Vector2 p, float r)
        {
            int cx0 = Mathf.FloorToInt((p.x - r) / GridCell), cx1 = Mathf.FloorToInt((p.x + r) / GridCell), cz0 = Mathf.FloorToInt((p.y - r) / GridCell), cz1 = Mathf.FloorToInt((p.y + r) / GridCell);
            for (int cx = cx0; cx <= cx1; cx++) for (int cz = cz0; cz <= cz1; cz++)
            {
                if (!edgeGrid.TryGetValue(GK(cx, cz), out var l)) continue;
                foreach (int id in l)
                {
                    var e = edges[id]; var a = nodes[e.a]; var ab = nodes[e.b] - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                    if ((p - (a + ab * t)).magnitude < r) return true;
                }
            }
            return false;
        }

        public int Other(int edge, int node) => edges[edge].a == node ? edges[edge].b : edges[edge].a;

        /// <summary>最寄りのノード。maxLiftを指定すると、その高さ以下(地上)のノードだけを対象にする。</summary>
        public int NearestNode(Vector2 p, float maxLift = 1000f)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!inMain[i] || nodeLift[i] > maxLift) continue;
                float d = (nodes[i] - p).sqrMagnitude; if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>ハブ(目的地候補)を選び、全ノードからハブへの最短経路表を作る。</summary>
        public void BuildHubs(int count, int seed, IList<Vector2> landmarks)
        {
            var rng = new System.Random(seed); var hubs = new List<int>();
            foreach (var lm in landmarks) hubs.Add(NearestNode(lm));
            var cand = new List<int>(); var w = new List<float>(); float tot = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!inMain[i]) continue; float s = 0; foreach (int e in adj[i]) s += Mathf.Min(edges[e].width, 14f);
                cand.Add(i); tot += s; w.Add(tot);
            }
            while (hubs.Count < count)
            {
                float r = (float)rng.NextDouble() * tot; int lo = 0, hi = w.Count - 1;
                while (lo < hi) { int m = (lo + hi) / 2; if (w[m] < r) lo = m + 1; else hi = m; }
                if (!hubs.Contains(cand[lo])) hubs.Add(cand[lo]);
            }
            hubNodes = hubs.ToArray(); nextEdge = new int[hubs.Count][];
            for (int h = 0; h < hubs.Count; h++) nextEdge[h] = Dijkstra(hubNodes[h]);
        }

        int[] Dijkstra(int src)
        {
            var dist = new float[nodes.Count]; System.Array.Fill(dist, float.MaxValue);
            var toward = new int[nodes.Count]; System.Array.Fill(toward, -1);
            var pq = new PriorityQueue<int, float>(); dist[src] = 0; pq.Enqueue(src, 0);
            while (pq.TryDequeue(out int u, out float du))
            {
                if (du > dist[u]) continue;
                foreach (int e in adj[u])
                {
                    int v = Other(e, u); float nd = du + edges[e].len;
                    if (nd < dist[v]) { dist[v] = nd; toward[v] = e; pq.Enqueue(v, nd); }  // vからはeを通ればuに近づく
                }
            }
            return toward;
        }

        /// <summary>A*。ノード列を返す(経路なしなら空)。</summary>
        public readonly List<int> stairEdges = new List<int>();   // 階段の辺(斜面の床を描く)
        public readonly List<float> lastPathLift = new List<float>();   // 直前に求めた経路の各点の高さ

        /// <summary>高さ lift の階にある、最寄りのノード(2階にいる人が、真下の1階のノードを選ばないように)。</summary>
        public int NearestNodeAt(Vector2 p, float lift)
        {
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!inMain[i] || Mathf.Abs(nodeLift[i] - lift) > 2.6f) continue;
                float d = (nodes[i] - p).sqrMagnitude; if (d < bd) { bd = d; best = i; }
            }
            return best >= 0 ? best : NearestNode(p);
        }

        public List<Vector2> Path(Vector2 from, Vector2 to, float fromLift = 0f)
        {
            int s = fromLift > 0.3f ? NearestNodeAt(from, fromLift) : NearestNode(from), g = NearestNode(to, 0.5f);
            var dist = new Dictionary<int, float> { [s] = 0 }; var prev = new Dictionary<int, int>();
            var pq = new PriorityQueue<int, float>(); pq.Enqueue(s, 0); var closed = new HashSet<int>();
            while (pq.TryDequeue(out int cur, out _))
            {
                if (!closed.Add(cur)) continue;
                if (cur == g) break;
                foreach (int e in adj[cur])
                {
                    int nx = Other(e, cur); float nd = dist[cur] + edges[e].len;
                    if (!dist.TryGetValue(nx, out float od) || nd < od)
                    { dist[nx] = nd; prev[nx] = cur; pq.Enqueue(nx, nd + (nodes[nx] - nodes[g]).magnitude); }
                }
            }
            var path = new List<Vector2>(); lastPathLift.Clear();
            if (!prev.ContainsKey(g) && s != g) return path;
            for (int n = g; ; n = prev[n]) { path.Add(nodes[n]); lastPathLift.Add(nodeLift[n]); if (n == s) break; }
            path.Reverse(); lastPathLift.Reverse(); return path;
        }
    }
}
