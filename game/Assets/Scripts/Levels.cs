using System.Collections.Generic;
using UnityEngine;

namespace ShibuyaRush
{
    /// <summary>1回の挑戦(レベル)の内容。スタートとゴール、制限時間、星の基準、特別ルール。</summary>
    public class LevelSpec
    {
        public int number; public string key, title, goalName, startName, modText;
        public Vector2 start, goal; public float goalLift, pathLen, limit, par3, par2;
        public List<Vector2> route = new List<Vector2>();
        public int mod;                    // 0=なし 1=コイン2倍 2=時間きびしめ 3=ノーダメージ
        public bool boss, free;            // boss=ライバルとの勝負
        public float rivalTime;
        public int seed;
        public int BaseReward => 25 + number * 3;
    }

    /// <summary>
    /// レベルの自動生成。番号から決まるので、同じレベルは何度やっても同じ(ゴースト・ベスト記録が使える)。
    /// 最初の3つはハチ公前から近い名所、それ以降は街のあちこちの地点を使う。
    /// </summary>
    public class LevelGen
    {
        readonly PedGraph g; readonly CityData data; readonly List<int> ground = new List<int>();
        readonly List<(string name, Vector2 p)> roadNames = new List<(string, Vector2)>();
        public static readonly Vector2 Hachiko = new Vector2(3.6f, -3.2f);
        public static readonly string[] ModNames = { "", "コイン2倍!", "時間きびしめ", "ノーダメージ" };
        static readonly string[] BossNames = { "ケンタ", "ミサキ", "ダイキ", "レイナ", "ソウタ", "ユイ" };

        public LevelGen(PedGraph graph, CityData city)
        {
            g = graph; data = city;
            for (int i = 0; i < g.nodes.Count; i++) if (g.inMain[i] && g.nodeLift[i] < 0.5f) ground.Add(i);
            foreach (var r in data.roads)
            {
                if (string.IsNullOrEmpty(r.name) || r.p == null) continue;
                for (int i = 0; i + 1 < r.p.Length; i += 6) roadNames.Add((r.name, new Vector2(r.p[i], r.p[i + 1])));
            }
        }

        public string NameAt(Vector2 p)
        {
            float bd = 45f * 45f; string best = null;
            foreach (var q in data.pois) { float d = (new Vector2(q.x, q.z) - p).sqrMagnitude; if (d < bd) { bd = d; best = q.name; } }
            if (best != null) return best;
            bd = 40f * 40f;
            foreach (var (n, q) in roadNames) { float d = (q - p).sqrMagnitude; if (d < bd) { bd = d; best = n + "付近"; } }
            if (best != null) return best;
            return "渋谷のまちなか";
        }

        /// <summary>経路の長さ[m]。</summary>
        public static float Length(List<Vector2> r) { float s = 0; for (int i = 1; i < r.Count; i++) s += (r[i] - r[i - 1]).magnitude; return s; }

        public LevelSpec Make(int n)
        {
            var rng = new System.Random(n * 7919 + 13);
            var sp = new LevelSpec { number = n, key = "L" + n, seed = n * 31 + 5, boss = n >= 5 && n % 5 == 0 };
            // 序盤は固定: ハチ公→近い名所
            Vector2? fixedGoal = null;
            if (n == 1) fixedGoal = new Vector2(-30, 85); else if (n == 2) fixedGoal = new Vector2(115, -217);
            Vector2 start = Hachiko;
            if (n > 3 && n % 3 == 0) { var q = data.pois[rng.Next(data.pois.Length)]; start = g.nodes[g.NearestNode(new Vector2(q.x, q.z), 0.5f)]; }
            else if (n > 6 && n % 4 == 1) start = g.nodes[ground[rng.Next(ground.Count)]];
            sp.start = start;
            float dMin = Mathf.Min(110f + n * 9f, 300f), dMax = Mathf.Min(190f + n * 14f, 520f);
            List<Vector2> route = null; Vector2 goal = start;
            if (fixedGoal.HasValue) { goal = fixedGoal.Value; route = g.Path(start, goal); }
            else
            {
                for (int tries = 0; tries < 60; tries++)
                {
                    var c = g.nodes[ground[rng.Next(ground.Count)]]; float d = (c - start).magnitude;
                    if (d < dMin || d > dMax) continue;
                    var r = g.Path(start, c); if (r.Count < 2) continue;
                    float len = Length(r); if (len > d * 1.7f + 40f) continue;
                    route = r; goal = c; break;
                }
                if (route == null) { var q = data.pois[n % data.pois.Length]; goal = new Vector2(q.x, q.z); route = g.Path(start, goal); }
            }
            int gn = g.NearestNode(goal, 0.5f); sp.goal = g.nodes[gn]; sp.goalLift = g.nodeLift[gn];
            if (route == null || route.Count < 2) route = new List<Vector2> { start, sp.goal };
            sp.route = route; sp.pathLen = Mathf.Max(60f, Length(route));
            sp.goalName = NameAt(sp.goal); sp.startName = (start - Hachiko).magnitude < 8f ? "ハチ公前" : NameAt(start);
            // 特別ルール(序盤は無し。ボスは単独)
            sp.mod = n <= 2 || sp.boss ? 0 : (n % 7 == 4 ? 3 : n % 3 == 2 ? 1 : n % 5 == 3 ? 2 : 0);
            sp.modText = ModNames[sp.mod];
            float v = Mathf.Lerp(3.2f, 4.5f, Mathf.Clamp01(n / 40f)) * (sp.mod == 2 ? 1.2f : 1f);
            sp.limit = Mathf.Ceil(sp.pathLen / v / 5f) * 5f + 15f;
            sp.par3 = Mathf.Round(sp.pathLen / 8.5f); sp.par2 = Mathf.Round(sp.pathLen / 5.4f);
            sp.limit = Mathf.Max(sp.limit, sp.par2 + 25f);
            sp.rivalTime = sp.boss ? Mathf.Lerp(sp.par2, sp.par3, Mathf.Clamp01((n - 5) / 50f)) * 1.0f : 0f;
            sp.title = sp.boss ? "ライバル勝負 vs " + BossNames[(n / 5) % BossNames.Length] : "LEVEL " + n;
            return sp;
        }

        /// <summary>名所を目的地にした自由プレイ(制限時間なし)。</summary>
        public LevelSpec MakeFree(int poiIndex)
        {
            var q = data.pois[poiIndex]; var sp = new LevelSpec { number = 0, key = "free" + q.id, free = true, seed = poiIndex + 99, start = Hachiko, title = "フリー: " + q.name };
            int gn = g.NearestNode(new Vector2(q.x, q.z), 0.5f); sp.goal = g.nodes[gn]; sp.goalLift = g.nodeLift[gn];
            sp.route = g.Path(Hachiko, sp.goal); if (sp.route.Count < 2) sp.route = new List<Vector2> { Hachiko, sp.goal };
            sp.pathLen = Mathf.Max(60f, Length(sp.route)); sp.goalName = q.name; sp.startName = "ハチ公前"; sp.limit = 9999f;
            sp.par3 = Mathf.Round(sp.pathLen / 8.5f); sp.par2 = Mathf.Round(sp.pathLen / 5.4f);
            return sp;
        }
    }
}
