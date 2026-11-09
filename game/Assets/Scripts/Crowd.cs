using System.Collections.Generic;
using UnityEngine;

namespace ShibuyaRush
{
    /// <summary>
    /// 道路網の上を歩く人混み。一人ひとりが任意の地点(ハブ)を目的地に持ち、着いたら次の目的地を選んで永遠に歩き続ける。
    /// プレイヤーとの当たり(押しのけ・減速)も扱う。
    /// </summary>
    public class Crowd
    {
        struct Agent
        {
            public int edge, toNode, hub; public float t;     // 辺上の位置(fromNodeからの距離)
            public float speed, lateral, phase, scale; public int tint;
            public Vector2 pos, off, vel; public float yaw;
        }
        readonly PedGraph g; readonly Agent[] a; readonly HumanoidRenderer hr;
        readonly int[] cellHead = new int[1 << 17]; readonly int[] next;
        System.Random rng;
        public int Count => a.Length;
        public int Trips { get; private set; }               // 目的地に着いた回数(動作確認用)
        public float NearDist = 55f, FarDist = 230f;

        public Crowd(PedGraph graph, HumanoidRenderer renderer, int count, int seed)
        {
            g = graph; hr = renderer; a = new Agent[count]; next = new int[count]; rng = new System.Random(seed);
            var cum = new List<float>(); var ids = new List<int>(); float tot = 0;
            for (int i = 0; i < g.edges.Count; i++)
            {
                if (!g.inMain[g.edges[i].a]) continue;
                float dc = g.nodes[g.edges[i].a].magnitude;   // 中心(ハチ公前)付近ほど混む
                tot += g.edges[i].len * Mathf.Min(g.edges[i].width, 14f) * (0.35f + 2.5f * Mathf.Exp(-dc / 260f));
                cum.Add(tot); ids.Add(i);
            }
            for (int i = 0; i < count; i++)
            {
                float r = (float)rng.NextDouble() * tot; int lo = 0, hi = cum.Count - 1;
                while (lo < hi) { int mid = (lo + hi) / 2; if (cum[mid] < r) lo = mid + 1; else hi = mid; }
                int eid = ids[lo]; var e = g.edges[eid]; bool fwd = rng.NextDouble() < 0.5;
                a[i].edge = eid; a[i].toNode = fwd ? e.b : e.a; a[i].t = (float)rng.NextDouble() * e.len;
                a[i].hub = rng.Next(g.hubNodes.Length);
                a[i].speed = 0.9f + (float)rng.NextDouble() * 0.9f;
                a[i].lateral = Lane(e.width);
                a[i].phase = (float)rng.NextDouble() * 6.28f; a[i].tint = rng.Next(0, hr.TintCount);
                a[i].scale = 0.88f + (float)rng.NextDouble() * 0.22f;
                a[i].pos = Target(i);
            }
        }

        float Lane(float width)
        {
            float half = Mathf.Max(0.3f, (width - 1f) * 0.5f);
            return half * ((float)rng.NextDouble() * 1.6f - 0.8f);
        }

        Vector2 Target(int i)
        {
            ref var ag = ref a[i]; var e = g.edges[ag.edge];
            bool fwd = ag.toNode == e.b;
            var from = g.nodes[fwd ? e.a : e.b]; var d = fwd ? e.dir : -e.dir;
            return from + d * ag.t + new Vector2(-d.y, d.x) * ag.lateral;
        }

        static int Hash(int cx, int cz) => ((cx * 73856093) ^ (cz * 19349663)) & 0x1FFFF;

        void Rebuild()
        {
            System.Array.Fill(cellHead, -1);
            for (int i = 0; i < a.Length; i++)
            {
                int h = Hash(Mathf.FloorToInt(a[i].pos.x / 2f), Mathf.FloorToInt(a[i].pos.y / 2f));
                next[i] = cellHead[h]; cellHead[h] = i;
            }
        }

        public void Query(Vector2 c, float r, List<int> res)
        {
            res.Clear(); float r2 = r * r;
            int x0 = Mathf.FloorToInt((c.x - r) / 2f), x1 = Mathf.FloorToInt((c.x + r) / 2f);
            int z0 = Mathf.FloorToInt((c.y - r) / 2f), z1 = Mathf.FloorToInt((c.y + r) / 2f);
            for (int cx = x0; cx <= x1; cx++) for (int cz = z0; cz <= z1; cz++)
                for (int i = cellHead[Hash(cx, cz)]; i >= 0; i = next[i])
                    if ((a[i].pos - c).sqrMagnitude <= r2) res.Add(i);
        }
        public Vector2 PosOf(int i) => a[i].pos;
        public void Push(int i, Vector2 impulse) { a[i].vel += impulse; }
        public void Shift(int i, Vector2 d) { a[i].off += d; }

        void Advance(int i)
        {
            ref var ag = ref a[i]; int node = ag.toNode; int eid;
            if (node == g.hubNodes[ag.hub])
            {   // 目的地に着いた: 別の目的地を選び直す
                Trips++; int nh; do { nh = rng.Next(g.hubNodes.Length); } while (nh == ag.hub && g.hubNodes.Length > 1);
                ag.hub = nh;
            }
            eid = g.nextEdge[ag.hub][node];
            if (eid < 0) { var opts = g.adj[node]; eid = opts[rng.Next(opts.Count)]; ag.hub = rng.Next(g.hubNodes.Length); }
            var ne = g.edges[eid]; ag.edge = eid; ag.toNode = g.Other(eid, node); ag.t = 0;
            float half = Mathf.Max(0.3f, (ne.width - 1f) * 0.5f); ag.lateral = Mathf.Clamp(ag.lateral, -half, half);
        }

        public void Step(float dt, Vector2 player, bool playerSolid)
        {
            Rebuild();
            for (int i = 0; i < a.Length; i++)
            {
                ref var ag = ref a[i];
                ag.t += ag.speed * dt;
                if (ag.t >= g.edges[ag.edge].len) Advance(i);
                var tgt = Target(i);
                ag.off += ag.vel * dt; ag.vel *= Mathf.Exp(-4f * dt);
                ag.off *= Mathf.Exp(-1.6f * dt);
                if (ag.off.sqrMagnitude > 9f) ag.off = ag.off.normalized * 3f;
                var d = ag.pos - player;
                if (playerSolid && d.sqrMagnitude < 0.49f)
                {
                    float dd = d.magnitude; if (dd > 1e-3f) ag.off += d / dd * (0.7f - dd) * 18f * dt;
                }
                var delta = tgt + ag.off - ag.pos;
                ag.pos += delta * Mathf.Min(1f, 8f * dt);
                if (delta.sqrMagnitude > 1e-4f) ag.yaw = Mathf.LerpAngle(ag.yaw, Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg, 8f * dt);
                ag.phase += ag.speed * dt * 5.2f;
            }
            for (int i = 0; i < a.Length; i++)    // 近すぎる者どうしを少し離す
            {
                int h = Hash(Mathf.FloorToInt(a[i].pos.x / 2f), Mathf.FloorToInt(a[i].pos.y / 2f));
                for (int j = cellHead[h]; j >= 0; j = next[j])
                {
                    if (j <= i) continue;
                    var d = a[i].pos - a[j].pos; float dd = d.sqrMagnitude;
                    if (dd < 0.16f && dd > 1e-6f)
                    {
                        var push = d / Mathf.Sqrt(dd) * (0.4f - Mathf.Sqrt(dd)) * 2.5f * dt;
                        a[i].off += push; a[j].off -= push;
                    }
                }
            }
        }

        /// <summary>カメラの前方にいる人だけ描く。近くは関節つき、遠くは簡易形状。</summary>
        public void Render(Vector3 camPos, Vector3 camFwd)
        {
            var f = new Vector2(camFwd.x, camFwd.z).normalized; var c = new Vector2(camPos.x, camPos.z);
            float near2 = NearDist * NearDist, far2 = FarDist * FarDist; var hf = Heightfield.Current;
            for (int i = 0; i < a.Length; i++)
            {
                var d = a[i].pos - c; float d2 = d.sqrMagnitude;
                if (d2 > far2) continue;
                if (d2 > 100f && Vector2.Dot(d, f) < 0.25f * Mathf.Sqrt(d2)) continue; // 視野外(約±75度の外)
                var p = new Vector3(a[i].pos.x, hf.Height(a[i].pos.x, a[i].pos.y), a[i].pos.y);
                if (d2 < near2) hr.Add(p, a[i].yaw, a[i].phase, 1f, a[i].tint, a[i].scale);
                else hr.AddFar(p, a[i].tint, a[i].scale);
            }
        }
    }
}
