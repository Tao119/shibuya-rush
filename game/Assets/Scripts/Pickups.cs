using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShibuyaRush
{
    /// <summary>
    /// 経路にそって置くコインと、取るとうれしいアイテム。
    /// コインは最短経路の道しるべにもなる。ジェム(10枚分)は少し脇道にそれた所、パワーアップは経路の上にある。
    /// </summary>
    public class Pickups
    {
        public enum Kind { Coin, Gem, Magnet, Time, Boost, Shield }
        struct Item { public Vector2 pos; public float lift; public Kind kind; public bool taken; public float phase; }
        readonly List<Item> items = new List<Item>();
        readonly Mesh coinMesh, gemMesh, boxMesh; readonly Material coinM, gemM; readonly Material[] puM = new Material[4];
        readonly List<Matrix4x4> coinL = new List<Matrix4x4>(), gemL = new List<Matrix4x4>(); readonly List<Matrix4x4>[] puL = { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
        readonly Matrix4x4[] buf = new Matrix4x4[512];
        public int CoinsTotal { get; private set; }
        /// <summary>種類・ワールド位置・コイン換算の枚数。</summary>
        public Action<Kind, Vector3, int> OnPick;
        public static readonly Color[] PuColor = { new Color(.3f, .62f, 1f), new Color(.4f, 1f, .5f), new Color(1f, .5f, .1f), new Color(.85f, 1f, 1f) };
        public static readonly string[] PuName = { "マグネット!", "+5秒!", "ブースト!", "シールド!" };

        static Material Glow(Color c, float k)
        {
            var m = new Material(Resources.Load<Material>("Mats/Goal")); m.SetColor("_BaseColor", c); m.SetColor("_EmissionColor", c * k); m.enableInstancing = true; return m;
        }

        public Pickups()
        {
            coinMesh = HumanoidMesh.Sphere(0.36f); gemMesh = HumanoidMesh.Box(new Vector3(0.42f, 0.42f, 0.42f), false); boxMesh = HumanoidMesh.Box(new Vector3(0.62f, 0.62f, 0.62f), false);
            Center(gemMesh); Center(boxMesh);
            coinM = Glow(new Color(1f, .8f, .12f), 1.6f); gemM = Glow(new Color(.75f, .35f, 1f), 2.2f);
            for (int i = 0; i < 4; i++) puM[i] = Glow(PuColor[i], 2.0f);
        }

        static void Center(Mesh m) { var v = m.vertices; var c = m.bounds.center; for (int i = 0; i < v.Length; i++) v[i] -= c; m.vertices = v; m.RecalculateBounds(); }

        public void Clear() { items.Clear(); CoinsTotal = 0; }

        public void Populate(List<Vector2> route, int seed, PedGraph graph, CityBuilder city, float coinStep = 3.6f)
        {
            Clear(); var rng = new System.Random(seed); float total = LevelGen.Length(route); float s = 14f, nextPu = 55f, nextGem = 40f; float curLift = 0f;
            int runLeft = 0; float weave = 0f, wPhase = 0f; int puCycle = rng.Next(4);
            while (s < total - 9f)
            {
                Sample(route, s, out var p, out var dir); var nrm = new Vector2(-dir.y, dir.x);
                if (runLeft <= 0) { runLeft = rng.Next(7, 14); weave = rng.Next(0, 3) * 0.6f; wPhase = (float)rng.NextDouble() * 6f; s += rng.Next(12, 26); continue; }
                runLeft--;
                var q = p + nrm * Mathf.Sin(wPhase + s * 0.35f) * weave;
                if (!city.Blocked(q.x, q.y, 0.35f)) { curLift = graph.ReachableLift(q.x, q.y, curLift); items.Add(new Item { pos = q, lift = curLift, kind = Kind.Coin, phase = (float)rng.NextDouble() * 6f }); CoinsTotal++; }
                s += coinStep;
                if (s > nextPu) { Add(route, s + 3f, 0f, (Kind)(2 + puCycle++ % 4), rng, graph, city); nextPu += 95f + rng.Next(0, 40); }
                if (s > nextGem) { float side = rng.Next(2) == 0 ? 1f : -1f; Add(route, s + 6f, side * (2.8f + (float)rng.NextDouble() * 1.5f), Kind.Gem, rng, graph, city); nextGem += 60f + rng.Next(0, 40); }
            }
        }

        void Add(List<Vector2> route, float s, float lateral, Kind k, System.Random rng, PedGraph graph, CityBuilder city)
        {
            Sample(route, s, out var p, out var dir); var q = p + new Vector2(-dir.y, dir.x) * lateral;
            if (city.Blocked(q.x, q.y, 0.4f)) q = p; if (city.Blocked(q.x, q.y, 0.4f)) return;
            items.Add(new Item { pos = q, lift = graph.ReachableLift(q.x, q.y, 0f), kind = k, phase = (float)rng.NextDouble() * 6f });
        }

        static void Sample(List<Vector2> r, float s, out Vector2 p, out Vector2 dir)
        {
            p = r[r.Count - 1]; dir = Vector2.up;
            for (int i = 1; i < r.Count; i++)
            {
                float l = (r[i] - r[i - 1]).magnitude;
                if (l < 1e-4f) continue;
                dir = (r[i] - r[i - 1]) / l;
                if (s <= l) { p = r[i - 1] + dir * s; return; }
                s -= l;
            }
        }

        /// <summary>プレイヤー周辺のコインを集める。magnet: 吸い寄せる半径。</summary>
        public void Tick(float dt, Vector2 pp, float plift, float magnet)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i]; if (it.taken) continue;
                if (Mathf.Abs(it.lift - plift) > 2f) continue;
                var d = pp - it.pos; float dist = d.magnitude;
                bool coinLike = it.kind == Kind.Coin || it.kind == Kind.Gem;
                if (coinLike && magnet > 1.5f && dist < magnet) { it.pos += d / Mathf.Max(dist, 0.01f) * Mathf.Min(dist, (14f + (magnet - dist) * 6f) * dt); items[i] = it; dist = (pp - it.pos).magnitude; }
                if (dist < (coinLike ? 1.25f : 1.7f))
                {
                    it.taken = true; items[i] = it;
                    var w = new Vector3(it.pos.x, Heightfield.Current.Height(it.pos.x, it.pos.y) + it.lift + 1f, it.pos.y);
                    OnPick?.Invoke(it.kind, w, it.kind == Kind.Coin ? 1 : it.kind == Kind.Gem ? 10 : 0);
                }
            }
        }

        public void Render(Vector3 camPos)
        {
            float t = Time.unscaledTime; var hf = Heightfield.Current; var cp = new Vector2(camPos.x, camPos.z);
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i]; if (it.taken || (it.pos - cp).sqrMagnitude > 95f * 95f) continue;
                float y = hf.Height(it.pos.x, it.pos.y) + it.lift + 1.0f + Mathf.Sin(t * 3f + it.phase) * 0.12f; var pos = new Vector3(it.pos.x, y, it.pos.y);
                switch (it.kind)
                {
                    case Kind.Coin: coinL.Add(Matrix4x4.TRS(pos, Quaternion.Euler(0, t * 200f + it.phase * 50f, 0), new Vector3(1f, 1f, 0.22f))); break;
                    case Kind.Gem: gemL.Add(Matrix4x4.TRS(pos + Vector3.up * 0.2f, Quaternion.Euler(35f, t * 150f, 35f), Vector3.one * 1.3f)); break;
                    default:
                        float k = 1f + 0.12f * Mathf.Sin(t * 6f);
                        puL[(int)it.kind - 2].Add(Matrix4x4.TRS(pos + Vector3.up * 0.3f, Quaternion.Euler(20f, t * 120f, 15f), Vector3.one * (1.3f * k))); break;
                }
            }
            Draw(coinMesh, coinM, coinL); Draw(gemMesh, gemM, gemL); for (int i = 0; i < 4; i++) Draw(boxMesh, puM[i], puL[i]);
        }

        void Draw(Mesh m, Material mat, List<Matrix4x4> l)
        {
            var rp = new RenderParams(mat) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            for (int i = 0; i < l.Count; i += buf.Length) { int n = Mathf.Min(buf.Length, l.Count - i); l.CopyTo(i, buf, 0, n); Graphics.RenderMeshInstanced(rp, m, 0, buf, n); }
            l.Clear();
        }
    }
}
