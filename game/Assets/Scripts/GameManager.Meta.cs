using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShibuyaRush
{
    public enum UiPanel { None, Shop, Missions, Settings, Free }

    /// <summary>ゲームの進行(レベル開始・クリア・失敗・報酬・アップグレード・ミッション)。画面の描画は GameManager.Screens.cs。</summary>
    public partial class GameManager
    {
        // ---- レベルと進行 ----
        LevelGen levels; LevelSpec spec, homeSpec; Pickups pickups; Rival rival; Ghost ghost; HumanoidRenderer rivalHr, ghostHr; Cosmetics cosm;
        UiPanel panel; int shopTab; bool dailyPopup;
        // ---- 1回の挑戦の記録 ----
        int trickN, ghostPassMark; float trickT; bool ghostPassed; int runCoinCount, runCoins, coinStreak, runMaxCombo, nearMiss, runPlow0, runSkill0; float bonusTime, magnetT, coinPulse, coinStreakT, nearCd, hintT; bool continueUsed, ghostShown; string failReason = "";
        float magnetBase;
        class ResultInfo { public float diff; public bool hadBest; public int stars, collected, baseReward, noHit, allCoins, comboBonus, rivalBonus, modBonus, total, extra; public bool newBest, rivalWon, levelUp, noHitRun, claimed; public float mult; public string note = ""; }
        ResultInfo res = new ResultInfo();
        // ---- ダミー広告 ----
        Action adCallback; float adT; string adLabel = "";
        public const float AdSeconds = 2.2f;

        float Remaining => spec == null ? 0f : spec.free ? 9999f : spec.limit + bonusTime - raceT;

        // ===================== 開始 =====================
        void StartLevel(LevelSpec s)
        {
            spec = s; panel = UiPanel.None; dailyPopup = false;
            goalName = s.goalName; goal = s.goal; goalLift = s.goalLift; startPos = s.start;
            player.pos = s.start; player.yaw = 0; player.ResetState(); raceT = 0; player.lift = graph.nodeLift[graph.NearestNode(s.start)];
            state = GameState.Countdown; stateT = 0;
            beam.SetActive(true); beam.transform.position = new Vector3(goal.x, Heightfield.Current.Height(goal.x, goal.y) + goalLift + 40, goal.y); beam.transform.localScale = new Vector3(2.4f, 40f, 2.4f);
            goalDist = (goal - s.start).magnitude; par3 = s.par3; par2 = s.par2;
            combo = 0; comboT = 0; comboBonus = 0; lastHits = player.plowed + player.shoved; lastCount = -1; resultNote = ""; starsEarned = 0; pops.Clear();
            trickN = 0; trickT = 0; ghostPassed = false; runCoins = 0; runCoinCount = 0; coinStreak = 0; runMaxCombo = 0; nearMiss = 0; hits = 0; bonusTime = 0; magnetT = 0; continueUsed = false; hintT = 0; ghostShown = false;
            runPlow0 = player.plowed + player.shoved; runSkill0 = player.skillUses; res = new ResultInfo(); failReason = "";
            crowd.Recenter(s.start, graph.nodeLift[graph.NearestNode(s.start)]); routeT = 0f; guide.Clear();
            player.allowDrop = autoplay; ApplyUpgrades();
            pickups.Populate(s.route, s.seed, graph, city);
            rival = s.boss ? new Rival(s.route, graph, s.rivalTime) : null;
            ghost.Begin(s.key); ghostHr = ghostHr ?? new HumanoidRenderer(new[] { M("GhostA") }, M("GhostA"), M("GhostA"));
            if (autoplay) { botPath = graph.Path(player.pos, goal, player.lift); botLift = new List<float>(graph.lastPathLift); botIdx = 0; nextReplan = Time.unscaledTime + 1.5f; Debug.Log($"[Bot] route nodes={botPath.Count} dest={goalName} len={s.pathLen:F0} limit={s.limit:F0} coins={pickups.CoinsTotal}"); if (shotDir != null) System.IO.Directory.CreateDirectory(shotDir); }
            Cursor.lockState = CursorLockMode.None; Store.D.plays++;
        }

        /// <summary>レベル生成の確認: 経路ができ、道のり・制限時間・星の基準が筋の通った値か。</summary>
        void RunLevelTests()
        {
            int bad = 0; var sb = new System.Text.StringBuilder();
            for (int n = 1; n <= 40; n++)
            {
                var sp = levels.Make(n); bool ok = sp.route.Count >= 2 && levels.Walkable(sp.route) && sp.pathLen > 50f && sp.limit > sp.par2 && sp.par2 > sp.par3 && sp.par3 > 5f;
                if (!ok) bad++;
                if (n <= 12 || !ok) sb.Append($"\n  L{n}: {sp.startName} -> {sp.goalName} len={sp.pathLen:F0}m limit={sp.limit:F0}s par={sp.par3:F0}/{sp.par2:F0} mod={sp.mod} boss={sp.boss}{(ok ? "" : "  NG")}");
            }
            // 人の密度: どの目的地・出発地でも、周りに人が十分いるか(40m以内の人数)
            int minN = int.MaxValue, sumN = 0, cntN = 0; var lst = new List<int>();
            for (int n = 1; n <= 40; n++)
            {
                var sp = levels.Make(n);
                foreach (var pt in new[] { sp.goal, sp.start, sp.route[sp.route.Count / 2] })
                {
                    crowd.Recenter(pt); crowd.Step(0.0001f, pt, false, 0f); crowd.Query(pt, 40f, lst); int c = lst.Count; minN = Mathf.Min(minN, c); sumN += c; cntN++;
                }
            }
            Debug.Log($"[SelfTest] crowd density within 40m of start/mid/goal: min={minN} avg={sumN / Mathf.Max(1, cntN)} (期待 minが十分大きい)");
            crowd.Recenter(Vector2.zero);
            Debug.Log($"[SelfTest] levels: 40 generated, invalid={bad} (期待 0){sb}");
        }

        /// <summary>
        /// 最短距離の検証: ゲームの経路(グラフのA*)の長さを、グラフとは無関係に「歩ける範囲を1m格子にして最短を探した距離」と比べる。
        /// 格子の距離は斜め移動の近似で、実際より最大8%ほど長く出る。経路がそれより明らかに長ければ、グラフが近道を持っていない。
        /// </summary>
        void RunRouteOptimalityTest()
        {
            int tested = 0, bad = 0, ungrid = 0; float worst = 0f; var sb = new System.Text.StringBuilder();
            for (int n = 1; n <= 40; n++)
            {
                var sp = levels.Make(n); float routeLen = sp.pathLen; if (routeLen < 1f) continue;
                bool flagged = false; float gl = GridShortest(sp.start, sp.goal); if (gl < 0f) { ungrid++; continue; }
                float ratio = routeLen / Mathf.Max(1f, gl); tested++; if (ratio > worst) worst = ratio;
                bool flag = ratio > 1.20f && routeLen - gl > 12f; if (flag) { bad++; Debug.Log($"[SelfTest] NG level {n}: start={sp.start} goal={sp.goal}"); GridShortest(sp.start, sp.goal, true); }
                if (flag || n <= 8) sb.Append($"\n  L{n}: route={routeLen:F0}m grid={gl:F0}m ratio={ratio:F2}{(flag ? "  NG(遠回り)" : "")}");
            }
            Debug.Log($"[SelfTest] shortest-path: tested={tested} detour(>1.2x and >12m)={bad} noGridPath={ungrid} worstRatio={worst:F2} (期待 detour=0){sb}");
        }

        float GridShortest(Vector2 a, Vector2 b, bool diag = false)
        {
            const float cell = 1f; float pad = 70f;
            float x0 = Mathf.Min(a.x, b.x) - pad, z0 = Mathf.Min(a.y, b.y) - pad, x1 = Mathf.Max(a.x, b.x) + pad, z1 = Mathf.Max(a.y, b.y) + pad;
            int W = Mathf.CeilToInt((x1 - x0) / cell), H = Mathf.CeilToInt((z1 - z0) / cell); if ((long)W * H > 1500000L) return -1f;
            var state = new byte[W * H];   // 0=未調査 1=歩ける 2=だめ
            bool Free(int ix, int iz)
            {
                if (ix < 0 || iz < 0 || ix >= W || iz >= H) return false; int k = ix + iz * W;
                if (state[k] == 0) { float x = x0 + (ix + 0.5f) * cell, z = z0 + (iz + 0.5f) * cell; state[k] = (byte)(!city.Blocked(x, z, 0.4f) && graph.InCorridor(x, z, 0f) ? 1 : 2); }
                return state[k] == 1;
            }
            bool Snap(Vector2 p, out int sx, out int sz)
            {
                int cx = Mathf.FloorToInt((p.x - x0) / cell), cz = Mathf.FloorToInt((p.y - z0) / cell);
                for (int r = 0; r <= 6; r++) for (int dx = -r; dx <= r; dx++) for (int dz = -r; dz <= r; dz++) if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) == r && Free(cx + dx, cz + dz)) { sx = cx + dx; sz = cz + dz; return true; }
                sx = sz = 0; return false;
            }
            if (!Snap(a, out int ax, out int az) || !Snap(b, out int bx, out int bz)) return -1f;
            var dist = new float[W * H]; System.Array.Fill(dist, float.MaxValue); var pq = new PriorityQueue<int, float>(); var prev = diag ? new int[W * H] : null;
            float Hh(int ix, int iz) { float dx = Mathf.Abs(ix - bx), dz = Mathf.Abs(iz - bz); return (dx + dz + (1.4142f - 2f) * Mathf.Min(dx, dz)) * cell; }
            dist[ax + az * W] = 0f; pq.Enqueue(ax + az * W, Hh(ax, az));
            int[] ddx = { 1, -1, 0, 0, 1, 1, -1, -1 }, ddz = { 0, 0, 1, -1, 1, -1, 1, -1 };
            while (pq.TryDequeue(out int cur, out _))
            {
                int cx = cur % W, cz = cur / W;
                if (cx == bx && cz == bz && prev != null)
                {   // 経路のうち、グラフの道から2.5m以上離れた区間(=グラフにない近道)を報告する
                    int c2 = cur; var gaps = new System.Text.StringBuilder(); int run = 0; Vector2 first = Vector2.zero; int cnt = 0;
                    while (c2 != ax + az * W) { var pp = new Vector2(x0 + (c2 % W + .5f) * cell, z0 + (c2 / W + .5f) * cell); if (!graph.NearEdge(pp, 2.5f)) { if (run == 0) first = pp; run++; } else { if (run >= 6 && cnt < 4) { gaps.Append($" gap({first.x:F0},{first.y:F0}) len~{run}m;"); cnt++; } run = 0; } c2 = prev[c2]; }
                    if (run >= 6 && cnt < 4) gaps.Append($" gap({first.x:F0},{first.y:F0}) len~{run}m;"); Debug.Log("[SelfTest] offgraph:" + gaps);
                }
                if (cx == bx && cz == bz) return dist[cur] + (a - new Vector2(x0 + (ax + .5f) * cell, z0 + (az + .5f) * cell)).magnitude + (b - new Vector2(x0 + (bx + .5f) * cell, z0 + (bz + .5f) * cell)).magnitude;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + ddx[k], nz = cz + ddz[k]; if (!Free(nx, nz)) continue;
                    if (k >= 4 && (!Free(cx + ddx[k], cz) || !Free(cx, cz + ddz[k]))) continue;   // 角を斜めにすり抜けない
                    float nd = dist[cur] + (k < 4 ? cell : 1.4142f * cell); int ni = nx + nz * W;
                    if (nd < dist[ni]) { dist[ni] = nd; if (prev != null) prev[ni] = cur; pq.Enqueue(ni, nd + Hh(nx, nz)); }
                }
            }
            return -1f;
        }

        void GoHome()
        {
            state = GameState.Menu; panel = UiPanel.None; beam.SetActive(false); Time.timeScale = 1f;
            if (Store.D.level > 0) homeSpec = levels.Make(Store.D.level);
            crowd.Recenter(homeSpec.start, 0f); guide.Clear();
            player.pos = homeSpec.start; player.yaw = 0; player.ResetState(); player.lift = graph.nodeLift[graph.NearestNode(homeSpec.start)]; pickups.Clear(); rival = null;
        }

        void ApplyUpgrades()
        {
            var u = Store.D.up;
            player.speedMul = 1f + Meta.Upgrades[0].step * u[0]; player.dashCdMul = 1f - Meta.Upgrades[1].step * u[1]; player.slipCdMul = 1f - Meta.Upgrades[2].step * u[2];
            player.shoveCdMul = 1f - 0.04f * u[3]; player.shoveRange = 1f + Meta.Upgrades[3].step * u[3] * 2f;
            magnetBase = u[4] > 0 ? 1.8f + Meta.Upgrades[4].step * u[4] : 0f;
        }

        void RebuildPlayerRenderer()
        {
            var sk = Meta.Skins[Mathf.Clamp(Store.D.skin, 0, Meta.Skins.Length - 1)];
            playerHr = new HumanoidRenderer(new[] { Cosmetics.Flat(sk.shirt) }, Cosmetics.Flat(sk.pants), M("Skin"));
        }

        // ===================== アイテム =====================
        void OnPick(Pickups.Kind k, Vector3 w, int value)
        {
            if (state != GameState.Playing) return;
            if (k == Pickups.Kind.Coin || k == Pickups.Kind.Gem)
            {
                int tm = trickN >= 2 ? Mathf.Min(4, trickN) : 1; value *= tm; runCoins += value; if (k == Pickups.Kind.Coin) runCoinCount++; coinStreak++; coinStreakT = 0.9f; coinPulse = Time.unscaledTime;
                fx.PlayCoin(coinStreak); fx.Sparkle(w, k == Pickups.Kind.Gem ? new Color(.8f, .5f, 1f) : new Color(1f, .85f, .2f), k == Pickups.Kind.Gem ? 18 : 5);
                if (k == Pickups.Kind.Gem) Pop("+10", w + Vector3.up, new Color(.85f, .6f, 1f), 1.2f);
                else if (coinStreak % 10 == 0) Pop("コイン x" + coinStreak, w + Vector3.up, new Color(1f, .9f, .3f), 0.9f);
                return;
            }
            int pi = (int)k - 2; fx.PlayPower(); fx.BigBurst(w, Pickups.PuColor[pi]); Pop(Pickups.PuName[pi], w + Vector3.up * 1.2f, Pickups.PuColor[pi], 1.5f);
            switch (k)
            {
                case Pickups.Kind.Magnet: magnetT = 9f; break;
                case Pickups.Kind.Time: bonusTime += 5f; break;
                case Pickups.Kind.Boost: player.dashT = 2.6f; player.evDash = true; break;
                case Pickups.Kind.Shield: player.shieldT = 10f; break;
            }
            Haptic();
        }

        void Haptic()
        {
#if UNITY_IOS || UNITY_ANDROID
            if (Store.D.haptics) Handheld.Vibrate();
#endif
        }

        // ===================== クリア・失敗 =====================
        void OnReachGoal()
        {
            state = GameState.Finished; stateT = 0; Cursor.lockState = CursorLockMode.None;
            var d = Store.D; res = new ResultInfo { mult = 1f };
            res.stars = raceT <= par3 ? 3 : raceT <= par2 ? 2 : 1; starsEarned = res.stars;
            res.collected = runCoins * (spec.mod == 1 ? 2 : 1);
            res.baseReward = spec.free ? 10 : spec.BaseReward + res.stars * 15;
            res.noHitRun = hits == 0; res.noHit = res.noHitRun ? 20 : 0;
            res.allCoins = pickups.CoinsTotal >= 8 && runCoinCount >= pickups.CoinsTotal ? 30 : 0;
            res.comboBonus = Mathf.Min(60, runMaxCombo);
            res.rivalWon = rival != null && !rival.Finished;
            res.rivalBonus = spec.boss ? (res.rivalWon ? 80 + spec.number * 4 : 0) : 0;
            float inc = 1f + Meta.Upgrades[5].step * d.up[5];
            res.total = Mathf.RoundToInt((res.collected + res.baseReward + res.noHit + res.allCoins + res.comboBonus + res.rivalBonus) * inc);
            if (!autoplay)
            {
                float prevBest = Store.BestTime(spec.key); res.hadBest = prevBest < 9000f; res.diff = raceT - prevBest;
                res.newBest = Store.SetBest(spec.key, raceT, res.stars);
                if (res.newBest) { ghost.Save(spec.key); resultNote = "NEW BEST!"; }
                d.coins += res.total; d.totalCoins += res.total; d.totalWins++; d.totalRuns++;
                if (res.stars == 3) d.threeStars++;
                if (res.noHitRun) d.noHitWins++;
                if (res.rivalWon) d.rivalWins++;
                if (!spec.free && spec.number == d.level) { d.level++; d.bestLevel = Mathf.Max(d.bestLevel, d.level); res.levelUp = true; }
                CommitRunStats(true);
            }
            if (spec.boss && !res.rivalWon) res.note = "ライバルに先を越された… 勝つと報酬アップ!";
            fx.Confetti(new Vector3(player.pos.x, Heightfield.Current.Height(player.pos.x, player.pos.y) + player.lift, player.pos.y)); fx.PlayWin(); Haptic();
            Debug.Log($"[Result] dest={goalName} time={raceT:F1}s stars={res.stars} coins={runCoins} reward={res.total}");
            Shot("finish"); WriteResult();
        }

        void OnFail(string reason)
        {
            state = GameState.Failed; stateT = 0; failReason = reason; fx.PlayFail(); Haptic(); Cursor.lockState = CursorLockMode.None;
            res = new ResultInfo { collected = runCoins }; int part = Mathf.RoundToInt(runCoins * 0.5f); res.total = part;
            if (!autoplay) { Store.D.coins += part; Store.D.totalCoins += part; Store.D.totalRuns++; CommitRunStats(false); }
            Debug.Log($"[Fail] {reason} t={raceT:F1} coins={runCoins}");
            Shot("fail"); WriteResult();
        }

        void CommitRunStats(bool win)
        {
            var d = Store.D; int plow = player.plowed + player.shoved - runPlow0, skills = player.skillUses - runSkill0;
            d.totalPlowed += plow; d.skillUses += skills; d.maxCombo = Mathf.Max(d.maxCombo, runMaxCombo);
            Store.Progress(Meta.MPlow, plow); Store.Progress(Meta.MCoin, runCoins); Store.Progress(Meta.MSkill, skills); Store.ProgressMax(Meta.MCombo, runMaxCombo);
            if (win)
            {
                Store.Progress(Meta.MClear, 1); if (res.noHitRun) Store.Progress(Meta.MNoHit, 1); if (res.stars == 3) Store.Progress(Meta.MStar3, 1); if (res.rivalWon) Store.Progress(Meta.MRival, 1);
            }
            Store.CheckAchievements(); Store.Commit();
        }

        void Continue()
        {   // 失敗後、広告を見て15秒もらって続ける
            bonusTime += 15f; continueUsed = true; state = GameState.Playing; stateT = 0; Store.D.coins -= res.total; Store.D.totalCoins -= res.total; Store.D.totalRuns = Mathf.Max(0, Store.D.totalRuns - 1); Store.Commit();
            fx.Banner = "つづき!"; fx.BannerSub = "+15秒"; fx.BannerColor = new Color(.5f, 1f, .6f); fx.BannerT = 1f;
        }

        /// <summary>スキルをつなげるほどコインが増える(3秒以内に次のスキルでチェーン)。</summary>
        void Trick(Vector3 w)
        {
            trickN++; trickT = 3f;
            if (trickN >= 2) Pop("TRICK ×" + Mathf.Min(4, trickN), w + Vector3.up * 3.2f, new Color(1f, .6f, .9f), 1.4f);
        }

        // ---- 地面の道しるべ: 目的地までの最短経路にそって、流れる矢じるしを並べる ----
        readonly List<Vector2> guide = new List<Vector2>(); float routeT; Mesh chevron; readonly List<Matrix4x4> chevL = new List<Matrix4x4>(); Material chevMat;

        /// <summary>矢印が指す点: 経路にそって約14m先。ゴールまでの経路の残りの長さも返す(経路がなければゴールの直線)。</summary>
        Vector2 RouteAim(out float remain)
        {
            remain = (goal - player.pos).magnitude; if (guide.Count < 2) return goal;
            int start = 0; float bd = float.MaxValue;
            for (int i = 0; i + 1 < guide.Count; i++) { var a = guide[i]; var ab = guide[i + 1] - a; float l2 = ab.sqrMagnitude; float tt = l2 > 1e-4f ? Mathf.Clamp01(Vector2.Dot(player.pos - a, ab) / l2) : 0f; float d = (a + ab * tt - player.pos).sqrMagnitude; if (d < bd) { bd = d; start = i; } }
            var cur = player.pos; float acc = 0f; Vector2 aim = goal; bool got = false;
            for (int i = start; i + 1 < guide.Count; i++)
            {
                var b = guide[i + 1]; float l = (b - cur).magnitude;
                if (!got && acc + l >= 14f && l > 1e-3f) { aim = cur + (b - cur) / l * (14f - acc); got = true; }
                acc += l; cur = b;
            }
            remain = acc; return got ? aim : goal;
        }

        void UpdateRouteGuide(float dt)
        {
            if (state != GameState.Playing && state != GameState.Countdown) return;
            routeT -= dt; if (routeT > 0f) return; routeT = touchMode ? 2.5f : 1.0f;
            var r = graph.Path(player.pos, goal, player.lift); guide.Clear(); if (r.Count >= 2) guide.AddRange(r);
        }

        void RenderRouteGuide()
        {
            if ((state != GameState.Playing && state != GameState.Countdown) || guide.Count < 2) return;
            if (chevron == null)
            {
                chevron = new Mesh { name = "Chevron" };
                chevron.SetVertices(new[] { new Vector3(0, .05f, 1.0f), new Vector3(-1.1f, .05f, -0.5f), new Vector3(0, .05f, 0.05f), new Vector3(1.1f, .05f, -0.5f) });
                chevron.SetTriangles(new[] { 0, 2, 1, 0, 3, 2, 1, 2, 0, 2, 3, 0 }, 0); chevron.RecalculateNormals(); chevron.RecalculateBounds();
                chevMat = new Material(M("Goal")); chevMat.SetColor("_BaseColor", new Color(1f, .9f, .2f)); chevMat.SetColor("_EmissionColor", new Color(2.2f, 1.7f, .2f)); chevMat.enableInstancing = true;
            }
            float shift = (Time.unscaledTime * 7f) % 6f, s = -shift, lift = player.lift, total = 0f; chevL.Clear(); var hf = Heightfield.Current;
            // 現在位置にいちばん近い線分から先へ、6mおき・最大110m
            int start = 0; float bd = float.MaxValue;
            for (int i = 0; i + 1 < guide.Count; i++) { var a = guide[i]; var b = guide[i + 1]; var ab = b - a; float l2 = ab.sqrMagnitude; float tt = l2 > 1e-4f ? Mathf.Clamp01(Vector2.Dot(player.pos - a, ab) / l2) : 0f; float d = (a + ab * tt - player.pos).sqrMagnitude; if (d < bd) { bd = d; start = i; } }
            var cur = player.pos; float carry = 3f;
            for (int i = start; i + 1 < guide.Count && total < 110f; i++)
            {
                var b = guide[i + 1]; var seg = b - cur; float l = seg.magnitude; if (l < 1e-3f) { cur = b; continue; } var dir = seg / l; float pos = carry;
                while (pos < l && total + pos < 110f)
                {
                    var p = cur + dir * pos; lift = graph.ReachableLift(p.x, p.y, lift); float fade = 1f - (total + pos) / 130f;
                    chevL.Add(Matrix4x4.TRS(new Vector3(p.x, hf.Height(p.x, p.y) + lift + 0.12f, p.y), Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y)), new Vector3(0.7f, 1f, 0.9f) * (0.55f + 0.45f * fade)));
                    pos += 6f;
                }
                carry = pos - l; total += l; cur = b;
            }
            if (chevL.Count == 0) return;
            var rp = new RenderParams(chevMat) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
            var arr = chevL.ToArray(); for (int i = 0; i < arr.Length; i += 500) Graphics.RenderMeshInstanced(rp, chevron, 0, arr, Mathf.Min(500, arr.Length - i), 0);
        }

        /// <summary>スタックから抜ける(ポーズ画面のボタン、またはUキー)。道の上に戻し、時間を3秒足す。</summary>
        void UnstickPlayer()
        {
            player.Unstick(graph); if (!spec.free) raceT += 3f; Pop("スタック脱出 +3秒", pw0() + Vector3.up * 2.5f, new Color(1f, .8f, .5f), 1.2f); fx.PlayPower(); Resume();
        }

        void Pause() { if (state == GameState.Playing) { state = GameState.Paused; } }
        void Resume() { if (state == GameState.Paused) state = GameState.Playing; }
        void OnApplicationPause(bool paused) { if (paused && state == GameState.Playing && !autoplay) Pause(); }

        // ===================== ダミー広告(本番ではAdMob等のSDKに差し替える) =====================
        void ShowRewardedAd(string label, Action reward) { if (adT > 0f) return; adCallback = reward; adLabel = label; adT = AdSeconds; }

        // ===================== ショップ =====================
        bool Buy(int cost) { if (Store.D.coins < cost) { fx.PlayBump(); toast = "コインが足りません"; toastT = 1.4f; return false; } Store.D.coins -= cost; fx.PlayCash(); return true; }

        void BuyUpgrade(int i)
        {
            var d = Store.D; if (d.up[i] >= Meta.Upgrades[i].max) return; int cost = Meta.UpgradeCost(i, d.up[i]);
            if (!Buy(cost)) return; d.up[i]++; ApplyUpgrades(); Store.CheckAchievements(); Store.Commit();
        }
        void BuyOrEquipSkin(int i) { var d = Store.D; if (!d.ownSkin.Contains(i)) { if (!Buy(Meta.Skins[i].price)) return; d.ownSkin.Add(i); } d.skin = i; RebuildPlayerRenderer(); Store.CheckAchievements(); Store.Commit(); fx.PlayClick(); }
        void BuyOrEquipHat(int i) { var d = Store.D; if (!d.ownHat.Contains(i)) { if (!Buy(Meta.Hats[i].price)) return; d.ownHat.Add(i); } d.hat = i; Store.Commit(); fx.PlayClick(); }
        void BuyOrEquipTrail(int i) { var d = Store.D; if (!d.ownTrail.Contains(i)) { if (!Buy(Meta.Trails[i].price)) return; d.ownTrail.Add(i); } d.trail = i; Store.Commit(); fx.PlayClick(); }

        /// <summary>いま一番ほしいもの: 持っていない中で一番安いスキン・ぼうし・トレイル。</summary>
        public bool NextWish(out string name, out int price)
        {
            var d = Store.D; name = ""; price = int.MaxValue;
            for (int i = 0; i < Meta.Skins.Length; i++) if (!d.ownSkin.Contains(i) && Meta.Skins[i].price < price) { price = Meta.Skins[i].price; name = "スキン『" + Meta.Skins[i].name + "』"; }
            for (int i = 1; i < Meta.Hats.Length; i++) if (!d.ownHat.Contains(i) && Meta.Hats[i].price < price) { price = Meta.Hats[i].price; name = "ぼうし『" + Meta.Hats[i].name + "』"; }
            for (int i = 1; i < Meta.Trails.Length; i++) if (!d.ownTrail.Contains(i) && Meta.Trails[i].price < price) { price = Meta.Trails[i].price; name = "トレイル『" + Meta.Trails[i].name + "』"; }
            return price != int.MaxValue;
        }

        string toast = ""; float toastT;

        string ShareText() => $"SHIBUYA RUSH {spec.title} を {raceT:F1}秒 {new string('★', res.stars)} でクリア! 目的地: {goalName}";
    }
}
