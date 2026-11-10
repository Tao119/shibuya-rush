using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ShibuyaRush
{
    /// <summary>ライバル: 経路をほぼ一定の速さで走る。人や車は気にしない(ボスレベル用)。</summary>
    public class Rival
    {
        readonly List<Vector2> route; readonly PedGraph graph; readonly float speed; readonly float total;
        public float dist, lift, yaw, phase; public Vector2 pos; public bool Finished => dist >= total - 3f;

        public Rival(List<Vector2> route, PedGraph graph, float timeToGoal)
        {
            this.route = route; this.graph = graph; total = LevelGen.Length(route); speed = total / Mathf.Max(5f, timeToGoal); pos = route[0];
        }

        public void Tick(float dt)
        {
            dist = Mathf.Min(total, dist + speed * dt); float s = dist; phase += speed * dt * 2.6f;
            for (int i = 1; i < route.Count; i++)
            {
                float l = (route[i] - route[i - 1]).magnitude; if (l < 1e-4f) continue;
                if (s <= l || i == route.Count - 1)
                {
                    var dir = (route[i] - route[i - 1]) / l; pos = route[i - 1] + dir * Mathf.Min(s, l);
                    yaw = Mathf.LerpAngle(yaw, Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg, 10f * dt); break;
                }
                s -= l;
            }
            lift = Mathf.MoveTowards(lift, graph.ReachableLift(pos.x, pos.y, lift), 10f * dt);
        }
        public float Progress => Mathf.Clamp01(dist / total);
    }

    /// <summary>ゴースト: 自己ベストの走りを記録・保存し、次の挑戦で半透明の分身として再生する。</summary>
    public class Ghost
    {
        public const float Step = 0.1f;
        struct S { public float x, z, yaw, lift; }
        List<S> rec = new List<S>(), play; float acc, last;
        public bool HasPlayback => play != null && play.Count > 1;
        public Vector2 pos; public float yaw, lift, phase;

        static string PathFor(string key) => Path.Combine(Application.persistentDataPath, "ghost_" + key + ".bin");

        public void Begin(string key)
        {
            rec = new List<S>(); acc = 0; play = null;
            try
            {
                var f = PathFor(key); if (!File.Exists(f)) return;
                using (var r = new BinaryReader(File.OpenRead(f)))
                {
                    int n = r.ReadInt32(); if (n < 2 || n > 20000) return; play = new List<S>(n);
                    for (int i = 0; i < n; i++) play.Add(new S { x = r.ReadSingle(), z = r.ReadSingle(), yaw = r.ReadSingle(), lift = r.ReadSingle() });
                }
            }
            catch { play = null; }
        }

        public void Record(float dt, Vector2 p, float yw, float lf)
        {
            acc += dt; while (acc >= Step) { acc -= Step; rec.Add(new S { x = p.x, z = p.y, yaw = yw, lift = lf }); }
        }

        public void Save(string key)
        {
            if (Store.NoPersist || rec.Count < 5) return;
            try { using (var w = new BinaryWriter(File.Create(PathFor(key)))) { w.Write(rec.Count); foreach (var s in rec) { w.Write(s.x); w.Write(s.z); w.Write(s.yaw); w.Write(s.lift); } } } catch { }
        }

        /// <summary>経過秒tの位置。終わっていればfalse。</summary>
        public bool Sample(float t)
        {
            if (!HasPlayback) return false;
            float f = t / Step; int i = (int)f; if (i >= play.Count - 1) return false;
            float k = f - i; var a = play[i]; var b = play[i + 1];
            var np = new Vector2(Mathf.Lerp(a.x, b.x, k), Mathf.Lerp(a.z, b.z, k));
            if ((np - pos).sqrMagnitude > 1e-6f) phase += (np - pos).magnitude * 2.6f;
            pos = np; yaw = Mathf.LerpAngle(a.yaw, b.yaw, k); lift = Mathf.Lerp(a.lift, b.lift, k); return true;
        }
    }
}
