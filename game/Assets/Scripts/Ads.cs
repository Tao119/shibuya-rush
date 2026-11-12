using System;
using UnityEngine;

namespace ShibuyaRush
{
    /// <summary>広告の窓口。本物のSDK(AdMob)は、シンボル SHIBUYA_ADMOB を定義したときだけ使われる。無いときは開発用のダミー広告を出す。</summary>
    public interface IAdProvider
    {
        string Name { get; }
        void Init();
        bool RewardedReady { get; }
        void ShowRewarded(Action onReward, Action onFail);
        bool InterstitialReady { get; }
        void ShowInterstitial(Action onClosed);
    }

    public static class Ads
    {
        public static IAdProvider Real;                    // 本物のSDK。なければnull(ダミー広告)
        public static float LastInterstitial = -999f, LastRewarded = -999f; public static int RunsSinceInterstitial;
        public const float InterstitialGap = 150f, AfterRewardedGap = 90f;   // 動画広告の直後や、続けざまには出さない

        public static void Init()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { Real = WebAdProvider.Create(); if (Real != null) Real.Init(); } catch (Exception e) { Debug.LogWarning("[Ads] web init failed: " + e.Message); Real = null; }
#elif SHIBUYA_ADMOB
            try { Real = new AdMobProvider(); Real.Init(); } catch (Exception e) { Debug.LogWarning("[Ads] init failed: " + e.Message); Real = null; }
#endif
        }

        /// <summary>1回の挑戦のあと、全画面広告を出してよいか(序盤は出さない・間隔をあける・失敗直後は出さない)。</summary>
        public static bool InterstitialDue()
        {
            var d = Store.D; if (d.noAds || d.totalWins < 3 || RunsSinceInterstitial < 2) return false;
            float t = Time.realtimeSinceStartup; return t - LastInterstitial > InterstitialGap && t - LastRewarded > AfterRewardedGap;
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    /// <summary>
    /// Web版の広告。ページ側(WebGLTemplates/Shibuya/index.html)の Google AdSense「ゲーム向け広告(adBreak)」を呼ぶ。
    /// ads-config.js に自分の AdSense のID(ca-pub-…)を入れると有効になる。空のあいだは、開発用のダミー広告になる。
    /// </summary>
    public class WebAdProvider : MonoBehaviour, IAdProvider
    {
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int WebAds_Enabled();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void WebAds_Show(int kind);   // 0=ごほうび動画 1=全画面
        Action onReward, onFail, onClosed;
        public static IAdProvider Create()
        {
            if (WebAds_Enabled() == 0) return null;
            var go = new GameObject("AdBridge"); DontDestroyOnLoad(go); return go.AddComponent<WebAdProvider>();
        }
        public string Name => "AdSense(Web)";
        public void Init() { }
        public bool RewardedReady => true;
        public bool InterstitialReady => true;
        public void ShowRewarded(Action reward, Action fail) { onReward = reward; onFail = fail; WebAds_Show(0); }
        public void ShowInterstitial(Action closed) { onClosed = closed; WebAds_Show(1); }
        // ページ側から SendMessage で呼ばれる
        public void OnAdResult(string r)
        {
            var rw = onReward; var f = onFail; var c = onClosed; onReward = onFail = onClosed = null;
            if (r == "reward") rw?.Invoke(); else if (r == "fail") f?.Invoke(); else if (r == "closed") { c?.Invoke(); f?.Invoke(); }
        }
    }
#endif

#if SHIBUYA_ADMOB
    /// <summary>
    /// Google AdMob。広告ユニットIDは、公開されているテスト用のもの。公開前に、自分のAdMobアカウントのIDへ差し替えること。
    /// </summary>
    public class AdMobProvider : IAdProvider
    {
#if UNITY_IOS
        const string RewardedId = "ca-app-pub-3940256099942544/1712485313", InterstitialId = "ca-app-pub-3940256099942544/4411468910";
#else
        const string RewardedId = "ca-app-pub-3940256099942544/5224354917", InterstitialId = "ca-app-pub-3940256099942544/1033173712";
#endif
        GoogleMobileAds.Api.RewardedAd rewarded; GoogleMobileAds.Api.InterstitialAd inter;
        public string Name => "AdMob";
        public void Init() { GoogleMobileAds.Api.MobileAds.Initialize(_ => { LoadRewarded(); LoadInterstitial(); }); }
        public bool RewardedReady => rewarded != null && rewarded.CanShowAd();
        public bool InterstitialReady => inter != null && inter.CanShowAd();

        void LoadRewarded()
        {
            GoogleMobileAds.Api.RewardedAd.Load(RewardedId, new GoogleMobileAds.Api.AdRequest(), (ad, err) => { rewarded = err == null ? ad : null; });
        }
        void LoadInterstitial()
        {
            GoogleMobileAds.Api.InterstitialAd.Load(InterstitialId, new GoogleMobileAds.Api.AdRequest(), (ad, err) => { inter = err == null ? ad : null; });
        }

        public void ShowRewarded(Action onReward, Action onFail)
        {
            if (!RewardedReady) { onFail?.Invoke(); LoadRewarded(); return; }
            bool earned = false; var ad = rewarded; rewarded = null;
            ad.OnAdFullScreenContentClosed += () => { LoadRewarded(); if (earned) onReward?.Invoke(); else onFail?.Invoke(); };
            ad.OnAdFullScreenContentFailed += _ => { LoadRewarded(); onFail?.Invoke(); };
            ad.Show(_ => { earned = true; });
        }

        public void ShowInterstitial(Action onClosed)
        {
            if (!InterstitialReady) { onClosed?.Invoke(); LoadInterstitial(); return; }
            var ad = inter; inter = null;
            ad.OnAdFullScreenContentClosed += () => { LoadInterstitial(); onClosed?.Invoke(); };
            ad.OnAdFullScreenContentFailed += _ => { LoadInterstitial(); onClosed?.Invoke(); };
            ad.Show();
        }
    }
#endif

    /// <summary>「広告を消す」の購入。本番は Unity IAP(App Store Connect の商品 jp.tao.shibuyarush.noads)につなぐ。いまは開発用の擬似購入。</summary>
    public static class Purchases
    {
        public const string NoAdsProduct = "jp.tao.shibuyarush.noads";
        public static void BuyNoAds(Action<bool> done)
        {
            // TODO(公開前): Unity IAP の購入処理に置き換える。成功したら Store.D.noAds = true; Store.Commit();
            Store.D.noAds = true; Store.Commit(); done?.Invoke(true);
        }
        public static void Restore(Action<bool> done) { done?.Invoke(Store.D.noAds); }
    }
}
