using System.Collections.Generic;
using UnityEngine;

namespace ShibuyaRush
{
    /// <summary>道路中心線から作る歩行者グラフ。人混みの経路(ハブへの最短経路表)とBot用の経路探索に使う。</summary>
    public class PedGraph
    {
        public struct Edge { public int a, b; public float len, width; public Vector2 dir; public int sig; }  // sig: 0=信号なし、1以上=信号つき横断歩道(位相オフセット秒+1)
        public readonly List<Vector2> nodes = new List<Vector2>();
        public readonly List<Edge> edges = new List<Edge>();
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
            id = nodes.Count; nodes.Add(p); adj.Add(new List<int>()); keyToNode[k] = id; return id;
        }

        void AddEdge(int na, int nb, float width, bool signal = false)
        {
            var a = nodes[na]; var b = nodes[nb]; float len = (b - a).magnitude;
            int id = edges.Count;
            int sig = 0;
            if (signal)
            {   // 近くの横断歩道は同じ位相にそろえる(80mの格子ごと)。大勢が同時に待ち、同時に渡る
                var m = (a + b) * 0.5f; int cx = Mathf.FloorToInt(m.x / 80f), cz = Mathf.FloorToInt(m.y / 80f);
                sig = 1 + (int)((uint)((cx * 73856093) ^ (cz * 19349663)) % (uint)SignalCycle);
            }
            edges.Add(new Edge { a = na, b = nb, len = len, width = width, dir = len > 1e-4f ? (b - a) / len : Vector2.up, sig = sig });
            adj[na].Add(id); adj[nb].Add(id);
        }

        public PedGraph(CityData city)
        {
            foreach (var r in city.roads)
            {
                if (!r.walk || r.tunnel || r.cls == "steps") continue;
                for (int i = 0; i + 3 < r.p.Length; i += 2)
                {
                    var a = new Vector2(r.p[i], r.p[i + 1]); var b = new Vector2(r.p[i + 2], r.p[i + 3]);
                    if ((b - a).magnitude < 0.5f) continue;
                    int na = Node(a), nb = Node(b); if (na == nb) continue;
                    AddEdge(na, nb, r.w, r.sig == 1);
                }
            }
            BridgeNearNodes(2.5f);
            FindMainComponent();
            BuildEdgeGrid();
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
                            if (j > i && (nodes[i] - nodes[j]).sqrMagnitude < r * r && !Connected(i, j)) AddEdge(i, j, 3f);
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

        /// <summary>(x,z)が歩行者グラフの道の上(幅の半分+余裕以内)か。建物の下をくぐる道の通過に使う。</summary>
        public bool OnWalkway(float x, float z)
        {
            if (!edgeGrid.TryGetValue(GK(Mathf.FloorToInt(x / GridCell), Mathf.FloorToInt(z / GridCell)), out var l)) return false;
            var p = new Vector2(x, z);
            foreach (int id in l)
            {
                var e = edges[id]; var a = nodes[e.a]; var ab = nodes[e.b] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                if ((p - (a + ab * t)).magnitude < Mathf.Max(e.width * 0.5f, 1.5f) + 0.8f) return true;
            }
            return false;
        }

        public int Other(int edge, int node) => edges[edge].a == node ? edges[edge].b : edges[edge].a;

        public int NearestNode(Vector2 p)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!inMain[i]) continue;
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
        public List<Vector2> Path(Vector2 from, Vector2 to)
        {
            int s = NearestNode(from), g = NearestNode(to);
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
            var path = new List<Vector2>();
            if (!prev.ContainsKey(g) && s != g) return path;
            for (int n = g; ; n = prev[n]) { path.Add(nodes[n]); if (n == s) break; }
            path.Reverse(); return path;
        }
    }
}
