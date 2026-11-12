using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShibuyaRush
{
    /// <summary>端末に保存する進行データ。JSONにして PlayerPrefs に1つのキーで持つ。</summary>
    [Serializable]
    public class SaveData
    {
        public int coins, level = 1, bestLevel = 1;
        public int totalRuns, totalWins, totalPlowed, totalCoins, noHitWins, threeStars, maxCombo, skillUses, rivalWins;
        public int[] up = new int[Meta.Upgrades.Length];
        public List<int> ownSkin = new List<int> { 0 }, ownHat = new List<int> { 0 }, ownTrail = new List<int> { 0 };
        public int skin, hat, trail;
        public string lastGift = "", lastDay = ""; public int giftStreak;
        public string missionDay = ""; public int[] mKind = new int[3], mTarget = new int[3], mProg = new int[3]; public bool[] mDone = new bool[3];
        public List<string> ach = new List<string>();
        public List<string> bestKeys = new List<string>(); public List<float> bestTimes = new List<float>(); public List<int> bestStars = new List<int>();
        public bool music = true, sfx = true, haptics = true, tutorialDone, noAds;
        public int rewardedViews, interstitialViews;
        public int plays;
    }

    /// <summary>セーブの読み書きと、日替わりミッション・ログインボーナス・実績の判定。</summary>
    public static class Store
    {
        const string Key = "shibuya_rush_save_v1";
        static SaveData d;
        public static bool NoPersist;                      // 自動テスト用: 保存しない

        public static SaveData D
        {
            get
            {
                if (d != null) return d;
                string raw = PlayerPrefs.GetString(Key, "");
                if (!string.IsNullOrEmpty(raw)) { try { d = JsonUtility.FromJson<SaveData>(raw); } catch { d = null; } }   // 空文字はWebGLで例外になるため先に除く
                if (d == null) d = new SaveData();
                if (d.up == null || d.up.Length < Meta.Upgrades.Length) { var n = new int[Meta.Upgrades.Length]; if (d.up != null) Array.Copy(d.up, n, d.up.Length); d.up = n; }
                if (d.mKind == null || d.mKind.Length != 3) { d.mKind = new int[3]; d.mTarget = new int[3]; d.mProg = new int[3]; d.mDone = new bool[3]; d.missionDay = ""; }
                return d;
            }
        }

        public static void Reset() { d = new SaveData(); Commit(); }
        public static void Commit() { if (NoPersist) return; PlayerPrefs.SetString(Key, JsonUtility.ToJson(D)); PlayerPrefs.Save(); }
        public static string Today => DateTime.Now.ToString("yyyyMMdd");

        // ---------------- ベスト ----------------
        public static float BestTime(string key) { int i = D.bestKeys.IndexOf(key); return i >= 0 ? D.bestTimes[i] : 9999f; }
        public static int BestStars(string key) { int i = D.bestKeys.IndexOf(key); return i >= 0 ? D.bestStars[i] : 0; }
        public static bool SetBest(string key, float t, int stars)
        {
            int i = D.bestKeys.IndexOf(key);
            if (i < 0) { D.bestKeys.Add(key); D.bestTimes.Add(t); D.bestStars.Add(stars); return true; }
            D.bestStars[i] = Mathf.Max(D.bestStars[i], stars);
            if (t < D.bestTimes[i]) { D.bestTimes[i] = t; return true; }
            return false;
        }

        // ---------------- ログインボーナス(7日周期) ----------------
        public static readonly int[] GiftCoins = { 100, 150, 200, 300, 400, 600, 1000 };
        public static bool GiftReady => D.lastGift != Today;
        public static int GiftDay
        {   // 今日受け取る日(0〜6)。昨日受け取っていれば続き、途切れたら最初から
            get
            {
                if (D.lastGift == "") return 0;
                if (D.lastGift == DateTime.Now.AddDays(-1).ToString("yyyyMMdd")) return D.giftStreak % 7;
                return D.lastGift == Today ? (D.giftStreak + 6) % 7 : 0;
            }
        }
        public static int ClaimGift(int mul)
        {
            if (!GiftReady) return 0;
            int day = GiftDay; int amount = GiftCoins[day] * mul;
            D.giftStreak = (D.lastGift == DateTime.Now.AddDays(-1).ToString("yyyyMMdd") ? D.giftStreak : 0) + 1;
            D.lastGift = Today; D.coins += amount; D.totalCoins += amount; Commit(); return amount;
        }

        // ---------------- 日替わりミッション ----------------
        public static void EnsureMissions()
        {
            if (D.missionDay == Today) return;
            D.missionDay = Today; var rng = new System.Random(int.Parse(Today));
            var pool = new List<int>(); for (int i = 0; i < Meta.Missions.Length; i++) pool.Add(i);
            for (int s = 0; s < 3; s++)
            {
                int k = rng.Next(pool.Count); int kind = pool[k]; pool.RemoveAt(k);
                var m = Meta.Missions[kind]; D.mKind[s] = kind; D.mTarget[s] = m.targets[rng.Next(m.targets.Length)]; D.mProg[s] = 0; D.mDone[s] = false;
            }
            Commit();
        }

        public static void Progress(int kind, int amount)
        {
            EnsureMissions();
            for (int s = 0; s < 3; s++) if (D.mKind[s] == kind && !D.mDone[s]) D.mProg[s] = Mathf.Min(D.mTarget[s], D.mProg[s] + amount);
        }
        public static void ProgressMax(int kind, int value)
        {
            EnsureMissions();
            for (int s = 0; s < 3; s++) if (D.mKind[s] == kind && !D.mDone[s]) D.mProg[s] = Mathf.Min(D.mTarget[s], Mathf.Max(D.mProg[s], value));
        }
        public static bool MissionClaimable(int s) => !D.mDone[s] && D.mProg[s] >= D.mTarget[s];
        public static int MissionReward(int s) => Meta.Missions[D.mKind[s]].reward + (D.mTarget[s] > Meta.Missions[D.mKind[s]].targets[0] ? 40 : 0);
        public static int ClaimMission(int s)
        {
            if (!MissionClaimable(s)) return 0;
            D.mDone[s] = true; int r = MissionReward(s); D.coins += r; D.totalCoins += r; Commit(); return r;
        }
        public static int ClaimableCount { get { EnsureMissions(); int n = 0; for (int s = 0; s < 3; s++) if (MissionClaimable(s)) n++; return n; } }

        // ---------------- 実績 ----------------
        public static readonly Queue<string> Toasts = new Queue<string>();
        public static void CheckAchievements()
        {
            foreach (var a in Meta.Achievements)
            {
                if (D.ach.Contains(a.id) || !a.test(D)) continue;
                D.ach.Add(a.id); D.coins += a.reward; D.totalCoins += a.reward; Toasts.Enqueue($"実績『{a.name}』  +{a.reward}");
            }
            Commit();
        }
    }
}
