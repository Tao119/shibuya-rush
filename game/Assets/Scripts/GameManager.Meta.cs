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
        int runCoinCount, runCoins, coinStreak, runMaxCombo, nearMiss, runPlow0, runSkill0; float bonusTime, magnetT, coinPulse, coinStreakT, nearCd, hintT; bool continueUsed, ghostShown; string failReason = "";
        float magnetBase;
        class ResultInfo { public int stars, collected, baseReward, noHit, allCoins, comboBonus, rivalBonus, modBonus, total, extra; public bool newBest, rivalWon, levelUp, noHitRun, claimed; public float mult; public string note = ""; }
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
            runCoins = 0; runCoinCount = 0; coinStreak = 0; runMaxCombo = 0; nearMiss = 0; hits = 0; bonusTime = 0; magnetT = 0; continueUsed = false; hintT = 0; ghostShown = false;
            runPlow0 = player.plowed + player.shoved; runSkill0 = player.skillUses; res = new ResultInfo(); failReason = "";
            ApplyUpgrades();
            pickups.Populate(s.route, s.seed, graph, city);
            rival = s.boss ? new Rival(s.route, graph, s.rivalTime) : null;
            ghost.Begin(s.key); ghostHr = ghostHr ?? new HumanoidRenderer(new[] { M("GhostA") }, M("GhostA"), M("GhostA"));
            if (autoplay) { botPath = new List<Vector2>(s.route); botIdx = 0; Debug.Log($"[Bot] route nodes={botPath.Count} dest={goalName} len={s.pathLen:F0} limit={s.limit:F0} coins={pickups.CoinsTotal}"); if (shotDir != null) System.IO.Directory.CreateDirectory(shotDir); }
            Cursor.lockState = CursorLockMode.None; Store.D.plays++;
        }

        /// <summary>レベル生成の確認: 経路ができ、道のり・制限時間・星の基準が筋の通った値か。</summary>
        void RunLevelTests()
        {
            int bad = 0; var sb = new System.Text.StringBuilder();
            for (int n = 1; n <= 40; n++)
            {
                var sp = levels.Make(n); bool ok = sp.route.Count >= 2 && sp.pathLen > 50f && sp.limit > sp.par2 && sp.par2 > sp.par3 && sp.par3 > 5f;
                if (!ok) bad++;
                if (n <= 12 || !ok) sb.Append($"\n  L{n}: {sp.startName} -> {sp.goalName} len={sp.pathLen:F0}m limit={sp.limit:F0}s par={sp.par3:F0}/{sp.par2:F0} mod={sp.mod} boss={sp.boss}{(ok ? "" : "  NG")}");
            }
            Debug.Log($"[SelfTest] levels: 40 generated, invalid={bad} (期待 0){sb}");
        }

        void GoHome()
        {
            state = GameState.Menu; panel = UiPanel.None; beam.SetActive(false); Time.timeScale = 1f;
            if (Store.D.level > 0) homeSpec = levels.Make(Store.D.level);
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
                runCoins += value; if (k == Pickups.Kind.Coin) runCoinCount++; coinStreak++; coinStreakT = 0.9f; coinPulse = Time.unscaledTime;
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
