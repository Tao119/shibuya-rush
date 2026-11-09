using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShibuyaRush
{
    /// <summary>
    /// 道路網を走る車。左側通行で、交差点と横断歩道の信号(歩行者青の間は停止)に従い、前の車と歩行者を避けて止まる。
    /// プレイヤーに当たると+3秒のペナルティ。
    /// </summary>
    public class Traffic
    {
        struct Car
        {
            public int edge, toNode, hub, pending; public float t, speed, vmax, lane, lift, yaw; public int color;
            public Vector2 pos;
        }
        public const float PedGreen = 38f, Clearance = 2f;
        readonly PedGraph g; readonly Crowd crowd; readonly Car[] c; System.Random rng = new System.Random(99);
        readonly float[] nodeOff;       // 横断歩道に近いノードの位相オフセット(なければ-1)
        readonly Dictionary<int, List<int>> cell = new Dictionary<int, List<int>>();
        readonly Mesh body, cabin; readonly Material[] bodyMats; readonly Material cabinMat;
        readonly List<Matrix4x4>[] bodyL; readonly List<Matrix4x4> cabinL = new List<Matrix4x4>(); readonly Matrix4x4[] buf = new Matrix4x4[1000];
        readonly List<int> tmp = new List<int>();
        public float Clock; public int Stopped { get; private set; }
        public int Count => c.Length;

        public Traffic(PedGraph carGraph, PedGraph pedGraph, Crowd crowd, int count, Material[] bodyMats, Material cabinMat)
        {
            g = carGraph; this.crowd = crowd; this.bodyMats = bodyMats; this.cabinMat = cabinMat;
            body = HumanoidMesh.Box(new Vector3(1.8f, 0.8f, 4.2f), false); cabin = HumanoidMesh.Box(new Vector3(1.6f, 0.62f, 2.3f), false);
            bodyL = new List<Matrix4x4>[bodyMats.Length]; for (int i = 0; i < bodyL.Length; i++) bodyL[i] = new List<Matrix4x4>();
            // 信号つき横断歩道の中点を集め、近い車道ノードに位相を割り当てる
            var mids = new List<(Vector2 p, int off)>();
            foreach (var e in pedGraph.edges) if (e.sig > 0) mids.Add(((pedGraph.nodes[e.a] + pedGraph.nodes[e.b]) * 0.5f, e.sig - 1));
            nodeOff = new float[g.nodes.Count];
            for (int i = 0; i < nodeOff.Length; i++)
            {
                nodeOff[i] = -1f; float best = 14f * 14f;
                foreach (var m in mids) { float d = (m.p - g.nodes[i]).sqrMagnitude; if (d < best) { best = d; nodeOff[i] = m.off; } }
            }
            c = new Car[count];
            var cum = new List<float>(); var ids = new List<int>(); float tot = 0;
            for (int i = 0; i < g.edges.Count; i++) { if (!g.inMain[g.edges[i].a]) continue; tot += g.edges[i].len * (g.edges[i].width >= 9f ? 2f : 1f); cum.Add(tot); ids.Add(i); }
            for (int i = 0; i < count && ids.Count > 0; i++)
            {
                float r = (float)rng.NextDouble() * tot; int lo = 0, hi = cum.Count - 1;
                while (lo < hi) { int mid = (lo + hi) / 2; if (cum[mid] < r) lo = mid + 1; else hi = mid; }
                int eid = ids[lo]; var e = g.edges[eid]; bool fwd = rng.NextDouble() < 0.5;
                c[i].edge = eid; c[i].toNode = fwd ? e.b : e.a; c[i].t = (float)rng.NextDouble() * e.len;
                c[i].hub = rng.Next(g.hubNodes.Length); c[i].pending = -1; c[i].color = rng.Next(bodyMats.Length);
                c[i].vmax = 7f + (float)rng.NextDouble() * 5f; c[i].speed = c[i].vmax * 0.5f;
                Place(i); c[i].pos = Target(i);
            }
        }

        void Place(int i) { ref var k = ref c[i]; var e = g.edges[k.edge]; k.lane = Mathf.Clamp(e.width * 0.25f, 1.2f, 3.2f); }

        Vector2 Dir(int i) { var e = g.edges[c[i].edge]; return c[i].toNode == e.b ? e.dir : -e.dir; }

        Vector2 Target(int i)
        {
            ref var k = ref c[i]; var e = g.edges[k.edge]; var d = Dir(i);
            var from = g.nodes[k.toNode == e.b ? e.a : e.b];
            return from + d * k.t + new Vector2(-d.y, d.x) * k.lane;     // 進行方向の左側(左側通行)
        }

        /// <summary>ノードnodeから方向dirへ出る車に対して赤か。</summary>
        bool Red(int node, Vector2 dir)
        {
            float ph;
            if (nodeOff[node] >= 0f)                       // 横断歩道の近く: 歩行者青の間は止まる
                return ((Clock + nodeOff[node]) % PedGreen2()) < PedGreen + Clearance;
            if (g.adj[node].Count < 3) return false;
            var p = g.nodes[node]; int off = (int)((uint)((Mathf.FloorToInt(p.x / 80f) * 73856093) ^ (Mathf.FloorToInt(p.y / 80f) * 19349663)) % 90u);
            ph = (Clock + off) % 90f; bool axisA = Mathf.Abs(dir.x) >= Mathf.Abs(dir.y);
            return axisA ? !(ph >= 40f && ph < 65f) : !(ph >= 67f && ph < 90f);
        }
        static float PedGreen2() => PedGraph.SignalCycle;

        void Advance(int i)
        {
            ref var k = ref c[i]; int node = k.toNode; int eid;
            if (node == g.hubNodes[k.hub]) { int nh; do { nh = rng.Next(g.hubNodes.Length); } while (nh == k.hub && g.hubNodes.Length > 1); k.hub = nh; }
            eid = g.nextEdge[k.hub][node];
            if (eid < 0) { var o = g.adj[node]; eid = o[rng.Next(o.Count)]; k.hub = rng.Next(g.hubNodes.Length); }
            var ne = g.edges[eid]; var dir = g.Other(eid, node) == ne.b ? ne.dir : -ne.dir;
            if (Red(node, dir)) { k.pending = eid; return; }
            Enter(i, eid, node);
        }
        void Enter(int i, int eid, int node) { ref var k = ref c[i]; k.pending = -1; k.edge = eid; k.toNode = g.Other(eid, node); k.t = 0; Place(i); }

        public void Step(float dt)
        {
            Clock += dt; Stopped = 0; cell.Clear();
            for (int i = 0; i < c.Length; i++) { int h = Mathf.FloorToInt(c[i].pos.x / 8f) * 7919 + Mathf.FloorToInt(c[i].pos.y / 8f); if (!cell.TryGetValue(h, out var l)) cell[h] = l = new List<int>(); l.Add(i); }
            for (int i = 0; i < c.Length; i++)
            {
                ref var k = ref c[i]; var e = g.edges[k.edge];
                if (k.pending >= 0)
                {
                    var pe = g.edges[k.pending]; var dir = g.Other(k.pending, k.toNode) == pe.b ? pe.dir : -pe.dir;
                    if (!Red(k.toNode, dir)) Enter(i, k.pending, k.toNode);
                    k.speed = Mathf.MoveTowards(k.speed, 0f, 9f * dt); Stopped++;
                }
                else
                {
                    float want = k.vmax;
                    var d = Dir(i); float dist = e.len - k.t;
                    if (dist < 12f && Red(k.toNode, NextDir(i))) want = Mathf.Min(want, Mathf.Max(0f, dist - 3f) * 1.2f); // 赤信号の手前で減速
                    want = Mathf.Min(want, Lead(i, d));
                    k.speed = Mathf.MoveTowards(k.speed, want, (want > k.speed ? 3.5f : 9f) * dt);
                    if (k.speed < 0.2f) Stopped++;
                    k.t += k.speed * dt;
                    if (k.t >= e.len) Advance(i);
                }
                var tgt = Target(i); k.pos += (tgt - k.pos) * Mathf.Min(1f, 10f * dt);
                var dd = Dir(i); k.yaw = Mathf.LerpAngle(k.yaw, Mathf.Atan2(dd.x, dd.y) * Mathf.Rad2Deg, 6f * dt);
                var ee = g.edges[k.edge]; float fr = Mathf.Clamp01(k.t / Mathf.Max(ee.len, 0.01f));
                k.lift = k.toNode == ee.b ? Mathf.Lerp(ee.lA, ee.lB, fr) : Mathf.Lerp(ee.lB, ee.lA, fr);
            }
        }

        Vector2 NextDir(int i)
        {
            ref var k = ref c[i]; int node = k.toNode; int eid = g.nextEdge[k.hub][node];
            if (eid < 0) return Dir(i);
            var ne = g.edges[eid]; return g.Other(eid, node) == ne.b ? ne.dir : -ne.dir;
        }

        /// <summary>前の車・歩行者に合わせた上限速度。</summary>
        float Lead(int i, Vector2 d)
        {
            ref var k = ref c[i]; float lim = k.vmax; var right = new Vector2(d.y, -d.x);
            int cx = Mathf.FloorToInt(k.pos.x / 8f), cz = Mathf.FloorToInt(k.pos.y / 8f);
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                if (cell.TryGetValue((cx + dx) * 7919 + (cz + dz), out var l))
                    foreach (int j in l)
                    {
                        if (j == i) continue; var r = c[j].pos - k.pos; float f = Vector2.Dot(r, d), s = Vector2.Dot(r, right);
                        if (f > 0.5f && f < 10f && Mathf.Abs(s) < 1.7f && Mathf.Abs(c[j].lift - k.lift) < 2f) lim = Mathf.Min(lim, Mathf.Max(0f, (f - 5.5f)) * 1.1f);
                    }
            var front = k.pos + d * 4f; crowd.Query(front, 3.2f, tmp);
            foreach (int p in tmp)
            {
                var r = crowd.PosOf(p) - k.pos; float f = Vector2.Dot(r, d), s = Vector2.Dot(r, right);
                if (f > 1.5f && f < 7f && Mathf.Abs(s) < 1.3f && Mathf.Abs(crowd.LiftOf(p) - k.lift) < 1.5f) lim = Mathf.Min(lim, Mathf.Max(0f, f - 3f));
            }
            return lim;
        }

        /// <summary>走行中(時速7km超)の車にプレイヤーが重なっていれば、押し出しベクトルを返す。</summary>
        public bool Collide(Vector2 p, float lift, out Vector2 push, out bool fast)
        {
            push = Vector2.zero; fast = false;
            int cx = Mathf.FloorToInt(p.x / 8f), cz = Mathf.FloorToInt(p.y / 8f);
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                if (cell.TryGetValue((cx + dx) * 7919 + (cz + dz), out var l))
                    foreach (int j in l)
                    {
                        ref var k = ref c[j]; if (Mathf.Abs(k.lift - lift) > 1.5f || k.speed <= 2f) continue; // 停止・徐行中の車は通り抜けられる
                        if (Overlap(p, k.pos, k.yaw, out push)) { fast = k.speed > 2f; return true; }
                    }
            return false;
        }

        /// <summary>点pが、位置carPos・向きyawDegの車(長さ4.2 x 幅1.8)に半径0.35mの余裕つきで重なるか。重なれば押し出し量。</summary>
        public static bool Overlap(Vector2 p, Vector2 carPos, float yawDeg, out Vector2 push)
        {
            push = Vector2.zero; float y = yawDeg * Mathf.Deg2Rad; var f = new Vector2(Mathf.Sin(y), Mathf.Cos(y)); var r = new Vector2(f.y, -f.x);
            var rel = p - carPos; float a = Vector2.Dot(rel, f), b = Vector2.Dot(rel, r);
            if (Mathf.Abs(a) >= 2.45f || Mathf.Abs(b) >= 1.25f) return false;
            float pa = 2.45f - Mathf.Abs(a), pb = 1.25f - Mathf.Abs(b);
            push = pa < pb ? f * Mathf.Sign(a) * pa : r * Mathf.Sign(b) * pb; return true;
        }

        public void Render(Vector3 camPos, float maxDist = 260f)
        {
            var hf = Heightfield.Current; var cp = new Vector2(camPos.x, camPos.z);
            for (int i = 0; i < c.Length; i++)
            {
                if ((c[i].pos - cp).sqrMagnitude > maxDist * maxDist) continue;
                var pos = new Vector3(c[i].pos.x, hf.Height(c[i].pos.x, c[i].pos.y) + c[i].lift, c[i].pos.y);
                var rot = Quaternion.Euler(0, c[i].yaw, 0);
                bodyL[c[i].color].Add(Matrix4x4.TRS(pos + new Vector3(0, 0.35f, 0), rot, Vector3.one));
                cabinL.Add(Matrix4x4.TRS(pos + rot * new Vector3(0, 1.15f, -0.25f), rot, Vector3.one));
            }
            for (int i = 0; i < bodyL.Length; i++) Draw(body, bodyMats[i], bodyL[i]);
            Draw(cabin, cabinMat, cabinL);
        }

        void Draw(Mesh m, Material mat, List<Matrix4x4> list)
        {
            var rp = new RenderParams(mat) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true, layer = 0 };
            for (int i = 0; i < list.Count; i += buf.Length) { int n = Mathf.Min(buf.Length, list.Count - i); list.CopyTo(i, buf, 0, n); Graphics.RenderMeshInstanced(rp, m, 0, buf, n); }
            list.Clear();
        }
    }
}
