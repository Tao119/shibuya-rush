using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShibuyaRush
{
    /// <summary>ホーム・ショップ・ミッション・設定・デイリー・リザルト・ポーズなどの画面(IMGUI、仮想UI座標)。</summary>
    public partial class GameManager
    {
        // ---------------- 入力の補助(タップとドラッグの区別、スクロール) ----------------
        Vector2 downPos, upPos, lastDrag; bool dragMoved, upNow, pressing, haveDown; float dragDY, wheelDY;
        readonly float[] shopScroll = new float[4]; float missionScroll, freeScroll, resetArmUntil; string ach = ""; float achT;

        void PreUI()
        {
            var e = Event.current; upNow = false; dragDY = 0f; wheelDY = 0f;
            if (e.type == EventType.MouseDown) { haveDown = true; downPos = e.mousePosition; lastDrag = downPos; dragMoved = false; pressing = true; }
            else if (e.type == EventType.MouseDrag) { dragDY = e.mousePosition.y - lastDrag.y; lastDrag = e.mousePosition; if ((e.mousePosition - downPos).sqrMagnitude > 100f) dragMoved = true; }
            else if (e.type == EventType.MouseUp) { upNow = true; upPos = e.mousePosition; pressing = false; }
            else if (e.type == EventType.ScrollWheel) wheelDY = e.delta.y;
        }
        void PostUI() { }

        bool Tap(Rect r, Rect? clip = null)
        {
            if (autoplay || !upNow || !haveDown || dragMoved || adT > 0f) return false;   // 自動プレイ中は画面のタップを受け付けない(外からの操作で状態が変わるのを防ぐ)
            if (clip.HasValue && !clip.Value.Contains(downPos)) return false;
            if (r.Contains(downPos) && r.Contains(upPos)) { upNow = false; fx.PlayClick(); return true; }
            return false;
        }

        bool Btn(Rect r, string text, Color col, int fs = 22, bool enabled = true, bool label = true, Rect? clip = null)
        {
            bool down = enabled && pressing && !dragMoved && r.Contains(downPos) && adT <= 0f;
            var rr = down ? new Rect(r.x, r.y + 3, r.width, r.height) : r;
            Panel(rr, enabled ? col : new Color(.5f, .5f, .58f, .92f), down ? 2f : 5f);
            if (label && text != "") Label(rr, text, St(fs), Color.white);
            return enabled && Tap(r, clip);
        }

        readonly Dictionary<int, GUIStyle> stCache = new Dictionary<int, GUIStyle>();
        GUIStyle St(int size, TextAnchor a = TextAnchor.MiddleCenter, bool wrap = false)
        {   // 毎フレーム作ると不要なゴミが増えて処理落ちするので、使い回す
            int key = size * 64 + (int)a * 2 + (wrap ? 1 : 0); if (!stCache.TryGetValue(key, out var st)) stCache[key] = st = new GUIStyle(mid) { fontSize = size, alignment = a, wordWrap = wrap }; return st;
        }
        void Chip(Rect r, string text, Color c) { Panel(r, new Color(c.r, c.g, c.b, .92f), 3f); Label(r, text, St(14), Color.white); }
        static string Fmt(int n) => n.ToString("N0");

        void CoinIcon(Rect r)
        {
            Disc(r, new Color(.8f, .5f, .05f)); float k = r.width * 0.07f; Disc(new Rect(r.x + k, r.y + k, r.width - 2 * k, r.height - 2 * k), new Color(1f, .8f, .12f));
            float k2 = r.width * 0.26f; Disc(new Rect(r.x + k2, r.y + k2, r.width - 2 * k2, r.height - 2 * k2), new Color(1f, .93f, .5f));
        }

        void CoinPill(Rect r, int amount)
        {
            Panel(r, new Color(.12f, .1f, .3f, .9f), 3f); CoinIcon(new Rect(r.x + 6, r.y + r.height / 2 - 14, 28, 28));
            Label(new Rect(r.x + 40, r.y, r.width - 46, r.height), Fmt(amount), St(Mathf.RoundToInt(r.height * 0.5f), TextAnchor.MiddleRight), new Color(1f, .92f, .4f));
        }

        void ScrollHandle(Rect view, ref float scroll, float contentH)
        {
            if (dragDY != 0f && dragMoved && view.Contains(downPos)) scroll -= dragDY;
            if (wheelDY != 0f && view.Contains(Event.current.mousePosition)) scroll += wheelDY * 24f;
            scroll = Mathf.Clamp(scroll, 0f, Mathf.Max(0f, contentH - view.height));
        }

        void Bar(Rect r, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old; }

        void Dim(float a = 0.55f) { var old = GUI.color; GUI.color = new Color(0.05f, 0.03f, 0.15f, a); GUI.DrawTexture(new Rect(0, 0, L.W, L.H), Texture2D.whiteTexture); GUI.color = old; }

        // ---------------- プレイ中の補助表示 ----------------
        void DrawPauseGlyph(Rect r)
        {
            var old = GUI.color; GUI.color = Color.white; float w = r.width * 0.12f, h = r.height * 0.36f;
            GUI.DrawTexture(new Rect(r.center.x - w * 1.8f, r.center.y - h / 2, w, h), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(r.center.x + w * 0.8f, r.center.y - h / 2, w, h), Texture2D.whiteTexture); GUI.color = old;
        }

        void DrawProgress(float W, float H)
        {
            if (state != GameState.Playing && state != GameState.Countdown) return;
            float w = Mathf.Min(300f, W * 0.52f), y = L.arrow.y + (touchMode ? 94f : 102f); var tr = new Rect(W / 2 - w / 2, y, w, 10f);
            Bar(new Rect(tr.x - 3, tr.y - 3, tr.width + 6, tr.height + 6), new Color(0, 0, 0, .5f));
            float Prog(Vector2 p) => Mathf.Clamp01(1f - (p - goal).magnitude / Mathf.Max(goalDist, 1f));
            void Dot(float pr, Color c, float s) => Disc(new Rect(tr.x + pr * tr.width - s, tr.center.y - s, s * 2, s * 2), c);
            Dot(1f, new Color(1f, .85f, .2f), 9f);
            if (ghostShown) Dot(Prog(ghost.pos), new Color(1, 1, 1, .6f), 7f);
            if (rival != null) Dot(rival.Progress, new Color(.62f, .38f, 1f), 9f);
            Dot(Prog(player.pos), new Color(1f, .5f, .1f), 10f);
        }

        void DrawRivalTag()
        {
            if (rival == null || state != GameState.Playing) return;
            var wp = new Vector3(rival.pos.x, Heightfield.Current.Height(rival.pos.x, rival.pos.y) + rival.lift + 3.4f, rival.pos.y); var sp = cam.WorldToScreenPoint(wp);
            if (sp.z < 0) return; var pv = new Vector2(sp.x / L.S, (Screen.height - sp.y) / L.S);
            Chip(new Rect(pv.x - 38, pv.y - 14, 76, 28), "RIVAL", new Color(.55f, .3f, 1f));
        }

        void DrawHint()
        {
            if (state != GameState.Playing || spec.free || spec.number != 1) return;
            string msg = null;
            if (hintT < 7f) msg = touchMode ? "ドラッグして走ろう!  黄色い矢印がゴール" : "ドラッグ / WASD で走ろう!  黄色い矢印がゴール";
            else if (player.skillUses == 0 && hintT > 10f) msg = touchMode ? "ぶつかると弾かれる! ボタンでスキル" : "ぶつかると弾かれる! Shift でダッシュ";
            else if (hintT > 2f && hintT < 12f && pickups.CoinsTotal > 0 && runCoins == 0) msg = "コインを取ると強くなれる!";
            if (msg == null) return;
            float w = Mathf.Min(L.W - 24f, 520f); var r = new Rect(L.W / 2 - w / 2, L.H * (L.portrait ? 0.55f : 0.5f), w, 44f);
            Panel(r, new Color(.15f, .13f, .35f, .88f), 3f); Label(r, msg, St(touchMode ? 18 : 21), Color.white);
            if (player.skillUses == 0 && hintT > 10f) { float p = 1f + 0.15f * Mathf.Sin(Time.unscaledTime * 8f); Disc(new Rect(L.sDash.x - L.sR * p - 8, L.sDash.y - L.sR * p - 8, (L.sR * p + 8) * 2, (L.sR * p + 8) * 2), new Color(1f, .9f, .3f, .55f)); }
        }

        void DrawToasts(float W)
        {
            if (toastT > 0f && toast != "") { var r = new Rect(W / 2 - 150, L.H * 0.4f, 300, 44); Panel(r, new Color(.2f, .12f, .35f, .94f), 3f); Label(r, toast, St(20), Color.white); }
            if (achT > 0f && ach != "")
            {
                float k = Mathf.Clamp01(Mathf.Min(achT, 3f - achT) * 4f); float w = Mathf.Min(W - 20f, 440f); var r = new Rect(W / 2 - w / 2, L.y0 + 8f - (1f - k) * 70f, w, 46);
                Panel(r, new Color(1f, .72f, .15f, .97f), 4f); Label(r, ach, St(touchMode ? 17 : 20), new Color(.3f, .15f, .02f));
            }
        }

        void UpdateToasts()
        {
            float d = Time.unscaledDeltaTime; if (toastT > 0f) toastT -= d;
            if (achT > 0f) achT -= d; else if (Store.Toasts.Count > 0) { ach = Store.Toasts.Dequeue(); achT = 3f; fx.PlayPower(); }
        }

        void DrawAd(float W, float H)
        {
            if (adT <= 0f) return;
            Dim(0.85f); float w = Mathf.Min(W - 40f, 420f); var r = new Rect(W / 2 - w / 2, H / 2 - 110, w, 220);
            Panel(r, new Color(.16f, .14f, .34f, 1f), 6f);
            Label(new Rect(r.x, r.y + 14, r.width, 40), "広告 (開発用ダミー)", St(26), Color.white);
            Label(new Rect(r.x + 12, r.y + 62, r.width - 24, 60), "実際のアプリでは、ここに動画広告が流れます。\n見終わると報酬: " + adLabel, St(16, TextAnchor.MiddleCenter, true), new Color(1, 1, 1, .9f));
            var bar = new Rect(r.x + 30, r.yMax - 52, r.width - 60, 16); Bar(bar, new Color(0, 0, 0, .45f));
            Bar(new Rect(bar.x, bar.y, Mathf.Max(8f, bar.width * (1f - adT / AdSeconds)), bar.height), new Color(.5f, 1f, .6f));
        }

        // ================== 読み込み画面 ==================
        void DrawLoading()
        {
            InitStyles(); ComputeLayout(); GUI.matrix = Matrix4x4.Scale(new Vector3(L.S, L.S, 1)); float W = L.W, H = L.H;
            GUI.DrawTexture(new Rect(0, 0, W, H), gradTex, ScaleMode.StretchToFill, true);
            string title = "SHIBUYA RUSH"; int ts = L.portrait ? 50 : 68; Label(new Rect(0, H * 0.30f, W, ts * 1.4f), title, new GUIStyle(huge) { fontSize = ts }, Color.white);
            float bw = Mathf.Min(W - 80f, 420f); var bar = new Rect(W / 2 - bw / 2, H * 0.30f + ts * 1.7f, bw, 14f);
            var old = GUI.color; GUI.color = new Color(0, 0, 0, .35f); GUI.DrawTexture(bar, Texture2D.whiteTexture); GUI.color = new Color(1f, .9f, .3f); GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(bootP), bar.height), Texture2D.whiteTexture); GUI.color = old;
            Label(new Rect(0, bar.yMax + 12f, W, 30f), bootMsg + "…", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 18 }, Color.white);
        }

        // ================== ホーム ==================
        void DrawHome()
        {
            float W = L.W, H = L.H; bool po = L.portrait; bool modal = panel != UiPanel.None || dailyPopup;
            if (panel == UiPanel.Shop) { DrawShop(); DrawDaily(W, H); return; }
            GUI.DrawTexture(new Rect(0, 0, W, H * 0.34f), shadeTex);
            var flip = GUI.matrix; GUI.matrix = flip * Matrix4x4.TRS(new Vector3(0, H, 0), Quaternion.identity, new Vector3(1, -1, 1)); GUI.DrawTexture(new Rect(0, 0, W, H * 0.4f), shadeTex); GUI.matrix = flip;
            var d = Store.D; float m = 12f;
            // 上の帯: レベルとコイン
            var lp = new Rect(L.x0 + m, L.y0 + m, 150, 44); Panel(lp, new Color(.12f, .1f, .3f, .9f), 3f); Label(lp, "LEVEL " + d.level, St(24), new Color(.8f, .9f, 1f));
            CoinPill(new Rect(L.x1 - m - 170, L.y0 + m, 170, 44), d.coins);
            // タイトル
            float t = Time.unscaledTime; string title = "SHIBUYA RUSH"; int ts = po ? 50 : (touchMode ? 52 : 72);
            var st = new GUIStyle(huge) { fontSize = ts }; float total = 0f; var wd = new float[title.Length];
            for (int i = 0; i < title.Length; i++) { wd[i] = title[i] == ' ' ? 20f : st.CalcSize(new GUIContent(title[i].ToString())).x + 2f; total += wd[i]; }
            float xc = W / 2 - total / 2, ty = L.y0 + (po ? 64f : 18f);
            for (int i = 0; i < title.Length; i++)
            {
                if (title[i] != ' ') { float dy = Mathf.Sin(t * 3f + i * 0.55f) * 5f; Label(new Rect(xc, ty + dy, wd[i], ts * 1.25f), title[i].ToString(), st, Color.Lerp(Color.white, Candy[i % Candy.Length], 0.5f)); }
                xc += wd[i];
            }
            // 下の帯: 目的地カード・ナビ・PLAY
            float bw = 352f; float bx = po ? W / 2 - bw / 2 : L.x1 - m - bw;
            var play = new Rect(bx + 16, L.y1 - m - 92f, bw - 32, 88f); float pk = 1f + 0.035f * Mathf.Sin(t * 5f); play = new Rect(play.center.x - play.width * pk / 2, play.center.y - play.height * pk / 2, play.width * pk, play.height * pk);
            if (Btn(play, "", new Color(.25f, .78f, .38f), 22, !modal, false)) StartLevel(homeSpec);
            Label(new Rect(play.x, play.y + 4, play.width, play.height * 0.62f), "PLAY", new GUIStyle(huge) { fontSize = 54 }, Color.white);
            Label(new Rect(play.x, play.yMax - 32, play.width, 26), homeSpec.title, St(17), new Color(1, 1, 1, .92f));
            float ny = play.y - 14f - 84f; float nw = (bw - 3 * 8f) / 4f;
            string[] names = { "ショップ", "ミッション", "ギフト", "フリー" }; Color[] cols = { Candy[0], Candy[3], Candy[1], Candy[2] };
            for (int i = 0; i < 4; i++)
            {
                var nr = new Rect(bx + i * (nw + 8f), ny, nw, 84f);
                if (Btn(nr, names[i], cols[i], 17, !modal))
                {
                    if (i == 0) { panel = UiPanel.Shop; shopTab = 0; } else if (i == 1) panel = UiPanel.Missions; else if (i == 2) { if (Store.GiftReady) dailyPopup = true; else { toast = "ギフトは明日また受け取れます"; toastT = 1.6f; } } else panel = UiPanel.Free;
                }
                bool badge = (i == 1 && Store.ClaimableCount > 0) || (i == 2 && Store.GiftReady); if (badge) Disc(new Rect(nr.xMax - 18, nr.y - 8, 26, 26), new Color(1f, .2f, .25f));
                if (badge) Label(new Rect(nr.xMax - 18, nr.y - 8, 26, 26), "!", St(18), Color.white);
            }
            var sg = new Rect(L.x0 + m, lp.yMax + 8, 128, 52); if (Btn(sg, "せってい", new Color(.4f, .38f, .62f, .95f), 18, !modal)) panel = UiPanel.Settings;
            // 目的地カード
            float cw = po ? Mathf.Min(W - 28f, 460f) : 420f; float chh = 148f;
            var card = po ? new Rect(W / 2 - cw / 2, ny - 14f - chh, cw, chh) : new Rect(L.x0 + m + 6f, (L.y0 + L.y1) / 2f - chh / 2f + 30f, cw, chh);
            Panel(card, new Color(.14f, .12f, .34f, .9f), 5f);
            Label(new Rect(card.x + 14, card.y + 6, card.width - 28, 34), homeSpec.title, St(26, TextAnchor.MiddleLeft), homeSpec.boss ? new Color(.8f, .65f, 1f) : Color.white);
            Label(new Rect(card.x + 14, card.y + 40, card.width - 28, 28), homeSpec.startName + "  ▶  " + homeSpec.goalName, St(19, TextAnchor.MiddleLeft), new Color(1f, .95f, .6f));
            Label(new Rect(card.x + 14, card.y + 68, card.width - 28, 26), $"約{homeSpec.pathLen:F0}m   制限 {homeSpec.limit:F0}秒   ★3は {homeSpec.par3:F0}秒以内", St(15, TextAnchor.MiddleLeft), new Color(1, 1, 1, .9f));
            float best = Store.BestTime(homeSpec.key); int bs = Store.BestStars(homeSpec.key);
            string line = best < 9000f ? $"BEST {best:F1}秒  " + new string('★', bs) + new string('☆', 3 - bs) : "まだクリアしていません";
            Label(new Rect(card.x + 14, card.y + 94, card.width - 28, 26), line, St(15, TextAnchor.MiddleLeft), new Color(.8f, 1f, .85f));
            if (homeSpec.mod > 0) Chip(new Rect(card.xMax - 150, card.y + 8, 138, 28), homeSpec.modText, new Color(1f, .5f, .35f));
            if (homeSpec.boss) Chip(new Rect(card.xMax - 150, card.y + 8, 138, 28), "ライバルより先に!", new Color(.55f, .3f, 1f));
            Label(new Rect(card.x + 14, card.y + 118, card.width - 28, 24), touchMode ? "画面をドラッグして走る / 右下でスキル" : "矢印/WASD/ドラッグで走る  Z/Shift ダッシュ  X/Space すり抜け  C/E かき分け", St(13, TextAnchor.MiddleLeft), new Color(1, 1, 1, .7f));
            // 次のもくひょう(貯めて買うものの進み具合)
            if (NextWish(out var wn, out int wp))
            {
                float ww = Mathf.Min(W - 28f, 360f); var wr = new Rect(po ? W / 2 - ww / 2 : L.x0 + m + 6f, po ? card.y - 62f : card.y - 62f, po ? ww : cw, 54f);
                Panel(wr, new Color(.12f, .1f, .3f, .9f), 3f); Label(new Rect(wr.x + 10, wr.y + 2, wr.width - 90, 26), "もくひょう: " + wn, St(15, TextAnchor.MiddleLeft), Color.white);
                var wb = new Rect(wr.x + 10, wr.y + 32, wr.width - 20, 14); Bar(wb, new Color(0, 0, 0, .45f));
                float wf = Mathf.Clamp01(d.coins / (float)wp); if (wf > .03f) Bar(new Rect(wb.x, wb.y, wb.width * wf, wb.height), wf >= 1f ? new Color(.4f, 1f, .5f) : new Color(1f, .8f, .2f));
                Label(new Rect(wr.xMax - 86, wr.y + 2, 76, 26), Fmt(d.coins) + "/" + Fmt(wp), St(13, TextAnchor.MiddleRight), new Color(1f, .92f, .4f));
                if (wf >= 1f && !modal && Tap(wr)) { panel = UiPanel.Shop; shopTab = 1; }
            }
            // モーダル
            if (panel == UiPanel.Missions) DrawMissions(W, H); else if (panel == UiPanel.Settings) DrawSettings(W, H); else if (panel == UiPanel.Credits) DrawCredits(W, H); else if (panel == UiPanel.Free) DrawFree(W, H);
            DrawDaily(W, H);
        }

        Rect ModalRect(float W, float H, float maxW, float maxH)
        {
            float w = Mathf.Min(W - 20f, maxW), h = Mathf.Min(H - (L.y0 + (H - L.y1)) - 20f, maxH); return new Rect(W / 2 - w / 2, (L.y0 + L.y1) / 2f - h / 2f, w, h);
        }

        /// <summary>全画面共通の戻るボタン(左上・大きく・矢印つき)。</summary>
        bool BackBtn(Rect r, string text = "‹ もどる") => Btn(r, text, new Color(.9f, .4f, .5f), 20);

        void ModalHeader(Rect r, string title, out Rect body) { body = new Rect(r.x + 10, r.y + 74, r.width - 20, r.height - 84); }

        /// <summary>リストのはみ出しを隠したうえで、戻るボタンとタイトルを描く。</summary>
        void ModalHeaderTop(Rect r, string title)
        {
            Panel(new Rect(r.x, r.y - 30, r.width, 104), new Color(.17f, .15f, .38f, 1f), 0f);
            if (BackBtn(new Rect(r.x + 12, r.y + 10, 128, 56))) panel = panel == UiPanel.Credits ? UiPanel.Settings : UiPanel.None;
            Label(new Rect(r.x + 150, r.y + 10, r.width - 160, 56), title, St(30, TextAnchor.MiddleLeft), Color.white);
        }

        // ================== ミッション ==================
        void DrawMissions(float W, float H)
        {
            Dim(); var r = ModalRect(W, H, 560f, 720f); Panel(r, new Color(.17f, .15f, .38f, 1f), 6f); ModalHeader(r, "ミッション", out var body);
            Store.EnsureMissions(); var d = Store.D;
            float rowH = 82f, achH = 56f; float content = 34f + 3 * (rowH + 8f) + 40f + Meta.Achievements.Length * (achH + 6f);
            ScrollHandle(body, ref missionScroll, content); float y = body.y - missionScroll;
            Label(new Rect(body.x + 6, y, body.width, 30), "きょうのミッション", St(20, TextAnchor.MiddleLeft), new Color(1f, .9f, .4f)); y += 34f;
            for (int s = 0; s < 3; s++)
            {
                var rr = new Rect(body.x, y, body.width, rowH); y += rowH + 8f;
                if (rr.yMax > body.y - 4 && rr.y < body.yMax + 4)
                {
                    bool done = d.mDone[s], can = Store.MissionClaimable(s); var mis = Meta.Missions[d.mKind[s]];
                    Panel(rr, done ? new Color(.25f, .3f, .35f, .95f) : new Color(.3f, .28f, .56f, .98f), 3f);
                    Label(new Rect(rr.x + 12, rr.y + 6, rr.width - 130, 30), string.Format(mis.text, d.mTarget[s]), St(touchMode ? 17 : 19, TextAnchor.MiddleLeft), Color.white);
                    var bar = new Rect(rr.x + 12, rr.y + 48, rr.width - 140, 16); Bar(bar, new Color(0, 0, 0, .4f));
                    float f = Mathf.Clamp01(d.mProg[s] / (float)d.mTarget[s]); if (f > 0.02f) Bar(new Rect(bar.x, bar.y, bar.width * f, bar.height), can || done ? new Color(.4f, .9f, .5f) : new Color(.35f, .65f, 1f));
                    Label(bar, d.mProg[s] + " / " + d.mTarget[s], St(12), Color.white);
                    var br = new Rect(rr.xMax - 112, rr.y + 14, 100, 54);
                    if (done) Btn(br, "完了", new Color(.45f, .5f, .55f), 18, false);
                    else if (can) { if (Btn(br, "+" + Store.MissionReward(s), new Color(.25f, .78f, .38f), 20, true, true, body)) { int g = Store.ClaimMission(s); toast = $"+{g} コイン!"; toastT = 1.5f; fx.PlayCash(); } }
                    else { CoinIcon(new Rect(br.x + 8, br.center.y - 12, 24, 24)); Label(new Rect(br.x + 34, br.y, br.width - 36, br.height), "" + Store.MissionReward(s), St(20), new Color(1f, .9f, .4f)); }
                }
            }
            y += 6f; Label(new Rect(body.x + 6, y, body.width, 30), "実績", St(20, TextAnchor.MiddleLeft), new Color(1f, .9f, .4f)); y += 34f;
            foreach (var a in Meta.Achievements)
            {
                var rr = new Rect(body.x, y, body.width, achH); y += achH + 6f; bool got = d.ach.Contains(a.id);
                if (rr.yMax < body.y - 4 || rr.y > body.yMax + 4) continue;
                Panel(rr, got ? new Color(.3f, .5f, .4f, .96f) : new Color(.26f, .24f, .48f, .96f), 3f);
                Label(new Rect(rr.x + 12, rr.y + 2, rr.width - 120, 28), a.name, St(18, TextAnchor.MiddleLeft), Color.white);
                Label(new Rect(rr.x + 12, rr.y + 28, rr.width - 120, 24), a.desc, St(13, TextAnchor.MiddleLeft), new Color(1, 1, 1, .8f));
                Label(new Rect(rr.xMax - 106, rr.y, 96, rr.height), got ? "達成!" : "+" + a.reward, St(19, TextAnchor.MiddleRight), got ? new Color(.7f, 1f, .8f) : new Color(1f, .9f, .4f));
            }
            // はみ出し分を隠す
            ModalHeaderTop(r, "ミッション");
            Panel(new Rect(r.x, body.yMax, r.width, 40), new Color(.17f, .15f, .38f, 1f), 0f);
        }

        // ================== せってい ==================
        void DrawSettings(float W, float H)
        {
            Dim(); var r = ModalRect(W, H, 520f, 720f); Panel(r, new Color(.17f, .15f, .38f, 1f), 6f); ModalHeader(r, "せってい", out var body); ModalHeaderTop(r, "せってい"); var d = Store.D;
            float y = body.y + 4f;
            void Toggle(string name, bool on, Action<bool> set)
            {
                var rr = new Rect(body.x, y, body.width, 56); y += 64f; Panel(rr, new Color(.3f, .28f, .56f, .98f), 3f);
                Label(new Rect(rr.x + 14, rr.y, rr.width - 130, rr.height), name, St(21, TextAnchor.MiddleLeft), Color.white);
                if (Btn(new Rect(rr.xMax - 108, rr.y + 8, 96, 40), on ? "ON" : "OFF", on ? new Color(.25f, .78f, .38f) : new Color(.55f, .5f, .65f), 20)) { set(!on); Store.Commit(); }
            }
            Toggle("おんがく", d.music, v => { d.music = v; fx.SetAudio(d.music, d.sfx); });
            Toggle("こうかおん", d.sfx, v => { d.sfx = v; fx.SetAudio(d.music, d.sfx); });
            Toggle("しんどう", d.haptics, v => d.haptics = v);
            if (!d.noAds) { if (Btn(new Rect(body.x, y, body.width, 52), "広告を消す", new Color(1f, .6f, .2f), 20)) Purchases.BuyNoAds(ok => { toast = ok ? "広告を消しました(開発版の擬似購入)" : "購入できませんでした"; toastT = 2f; }); y += 60f; }
            else { Label(new Rect(body.x, y, body.width, 40), "広告は表示されません", St(17), new Color(.7f, 1f, .8f)); y += 44f; }
            { float hw = (body.width - 8f) / 2f; if (Btn(new Rect(body.x, y, hw, 48), "購入を復元", new Color(.4f, .38f, .62f), 16)) Purchases.Restore(ok => { toast = ok ? "購入を復元しました" : "復元できる購入はありません"; toastT = 2f; });
              if (Btn(new Rect(body.x + hw + 8f, y, hw, 48), "クレジット・規約", new Color(.4f, .38f, .62f), 16)) panel = UiPanel.Credits; y += 56f; }
            bool armed = Time.unscaledTime < resetArmUntil;
            if (Btn(new Rect(body.x, y, body.width, 48), armed ? "もう一度おすと すべて消えます" : "データを初期化", armed ? new Color(.9f, .25f, .3f) : new Color(.5f, .45f, .6f), 18))
            {
                if (armed) { Store.Reset(); RebuildPlayerRenderer(); ApplyUpgrades(); Store.EnsureMissions(); GoHome(); toast = "初期化しました"; toastT = 1.5f; panel = UiPanel.None; } else resetArmUntil = Time.unscaledTime + 3f;
            }
            y += 56f;
            Label(new Rect(body.x, y, body.width, body.yMax - y), "地図データ: OpenStreetMap contributors (ODbL)\n建物の高さ: PLATEAU (国土交通省) ・ 標高: 国土地理院\n実在の施設名は地図データに基づく表示です。", St(13, TextAnchor.UpperLeft, true), new Color(1, 1, 1, .7f));
        }

        // ================== クレジット・プライバシー ==================
        float creditScroll;
        void DrawCredits(float W, float H)
        {
            Dim(); var r = ModalRect(W, H, 560f, 760f); Panel(r, new Color(.17f, .15f, .38f, 1f), 6f); ModalHeader(r, "クレジット", out var body);
            string text = "SHIBUYA RUSH\n\n【地図・地形のデータ】\n・OpenStreetMap contributors (ODbL)\n  © OpenStreetMap contributors\n・建物の高さ: 国土交通省 Project PLATEAU (CC BY 4.0)\n・標高: 国土地理院 基盤地図情報 数値標高モデル\n・通りや公共の場所の名前は地図データにもとづく表示です。企業・商業施設の名前は、実在の名称とは異なるパロディ名で、各企業・施設とは関係ありません。\n\n【フォント】\nM PLUS Rounded 1c (SIL Open Font License 1.1)\n\n【広告について】\nこのアプリは広告を表示します。広告の配信には端末の広告識別子が使われることがあります。\n\n【プライバシー】\n個人を特定する情報は、アプリ内に保存しません。進行データは端末内にのみ保存されます。\n\n【お問い合わせ】\nhttps://github.com/Tao119/shibuya-rush";
            var st = St(15, TextAnchor.UpperLeft, true); float h = st.CalcHeight(new GUIContent(text), body.width - 12f) + 20f;
            ScrollHandle(body, ref creditScroll, h + 60f); Label(new Rect(body.x + 6, body.y - creditScroll, body.width - 12f, h), text, st, new Color(1, 1, 1, .92f));
            ModalHeaderTop(r, "クレジット"); Panel(new Rect(r.x, body.yMax, r.width, 40), new Color(.17f, .15f, .38f, 1f), 0f);
        }

        // ================== フリー(目的地えらび) ==================
        void DrawFree(float W, float H)
        {
            Dim(); var r = ModalRect(W, H, 560f, 640f); Panel(r, new Color(.17f, .15f, .38f, 1f), 6f); ModalHeader(r, "フリー (制限なし)", out var body);
            float rowH = 74f; int n = data.pois.Length; ScrollHandle(body, ref freeScroll, n * (rowH + 8f)); float y = body.y - freeScroll;
            for (int i = 0; i < n; i++)
            {
                var rr = new Rect(body.x, y, body.width, rowH); y += rowH + 8f; if (rr.yMax < body.y - 4 || rr.y > body.yMax + 4) continue;
                string key = "free" + data.pois[i].id; int stars = Store.BestStars(key); float best = Store.BestTime(key);
                bool hit = Btn(rr, "", Candy[i % Candy.Length], 20, true, false, body);
                Disc(new Rect(rr.x + 10, rr.y + 10, 54, 54), Color.white); Label(new Rect(rr.x + 10, rr.y + 10, 54, 54), "" + (i + 1), St(28), Candy[i % Candy.Length]);
                Label(new Rect(rr.x + 76, rr.y + 6, rr.width - 190, 36), data.pois[i].name, St(touchMode ? 19 : 22, TextAnchor.MiddleLeft), Color.white);
                Label(new Rect(rr.x + 76, rr.y + 40, rr.width - 190, 28), $"約{new Vector2(data.pois[i].x, data.pois[i].z).magnitude:F0}m", St(15, TextAnchor.MiddleLeft), new Color(1, 1, 1, .9f));
                Label(new Rect(rr.xMax - 118, rr.y + 6, 108, 34), new string('★', stars) + new string('☆', 3 - stars), St(22, TextAnchor.MiddleRight), stars > 0 ? new Color(1f, .93f, .35f) : new Color(1, 1, 1, .6f));
                if (best < 9000f) Label(new Rect(rr.xMax - 118, rr.y + 40, 108, 28), $"BEST {best:F1}s", St(14, TextAnchor.MiddleRight), Color.white);
                if (hit) StartLevel(levels.MakeFree(i));
            }
            ModalHeaderTop(r, "フリー (制限なし)");
        }

        // ================== デイリーギフト ==================
        void DrawDaily(float W, float H)
        {
            if (!dailyPopup) return;
            Dim(0.7f); bool wide = W > 700f; float cw = Mathf.Min(W - 20f, wide ? 720f : 440f), chh = wide ? 390f : 530f; var r = new Rect(W / 2 - cw / 2, (L.y0 + L.y1) / 2f - chh / 2f, cw, chh);
            Panel(r, new Color(.17f, .15f, .38f, 1f), 6f);
            Label(new Rect(r.x, r.y + 8, r.width, 44), "ログインボーナス", St(30), new Color(1f, .9f, .4f));
            int today = Store.GiftDay; int perRow = wide ? 7 : 4; float gap = 8f; float cell = Mathf.Min(86f, (r.width - 24f - gap * (perRow - 1)) / perRow);
            for (int i = 0; i < 7; i++)
            {
                int row = i / perRow, col = i % perRow; int inRow = Mathf.Min(perRow, 7 - row * perRow); float rowW = inRow * cell + (inRow - 1) * gap;
                var c = new Rect(r.center.x - rowW / 2f + col * (cell + gap), r.y + 62f + row * (cell * 1.2f + gap), cell, cell * 1.2f);
                bool past = i < today, cur = i == today; float pk = cur ? 1f + 0.05f * Mathf.Sin(Time.unscaledTime * 6f) : 1f;
                var cc = new Rect(c.center.x - c.width * pk / 2, c.center.y - c.height * pk / 2, c.width * pk, c.height * pk);
                Panel(cc, cur ? new Color(1f, .7f, .15f) : past ? new Color(.3f, .5f, .4f) : new Color(.3f, .28f, .56f), 3f);
                Label(new Rect(cc.x, cc.y + 4, cc.width, 24), (i + 1) + "日目", St(13), new Color(1, 1, 1, .9f));
                CoinIcon(new Rect(cc.center.x - 15, cc.y + cc.height * 0.3f, 30, 30));
                Label(new Rect(cc.x, cc.yMax - 32, cc.width, 28), past ? "済" : "" + Store.GiftCoins[i], St(i == 6 ? 19 : 17), i == 6 ? new Color(1f, .95f, .4f) : Color.white);
            }
            float by = r.yMax - 130f; float bw = (r.width - 16f * 3) / 2f;
            if (Btn(new Rect(r.center.x - 80f, r.yMax - 66f, 160f, 52f), "あとで", new Color(.45f, .42f, .66f), 18)) dailyPopup = false;
            if (Btn(new Rect(r.x + 16, by, bw, 52), "うけとる", new Color(.25f, .78f, .38f), 22)) { int g = Store.ClaimGift(1); toast = $"+{g} コイン!"; toastT = 1.8f; dailyPopup = false; fx.PlayCash(); }
            if (Btn(new Rect(r.x + 32 + bw, by, bw, 52), "広告で ×2", new Color(1f, .6f, .2f), 22)) ShowRewardedAd("ログインボーナス ×2", () => { int g = Store.ClaimGift(2); toast = $"+{g} コイン!"; toastT = 1.8f; dailyPopup = false; fx.PlayCash(); });
        }

        // ================== ショップ ==================
        void DrawShop()
        {
            float W = L.W, H = L.H; bool po = L.portrait; var d = Store.D;
            float px = po ? -30f : W * 0.42f, py = po ? H * 0.46f : -30f; var P = new Rect(px, py, po ? W + 60f : W - px + 30f, po ? H - py + 40f : H + 60f);
            Panel(P, new Color(.17f, .15f, .38f, 1f), 0f);
            float ix = po ? L.x0 + 12f : W * 0.42f + 14f, iy = po ? H * 0.46f + 10f : L.y0 + 10f, iw = po ? (L.x1 - L.x0) - 24f : (L.x1 - ix - 12f), ih = (po ? L.y1 : L.y1) - iy - 10f;
            var tabsR = new Rect(ix, iy + 64f, iw, 50f); var foot = new Rect(ix, iy + ih - 58f, iw, 56f); var list = new Rect(ix, tabsR.yMax + 8f, iw, foot.y - 8f - (tabsR.yMax + 8f));
            // リスト
            string[] tabs = { "アップグレード", "スキン", "ぼうし", "トレイル" }; float rowH = 84f; int count = shopTab == 0 ? Meta.Upgrades.Length : shopTab == 1 ? Meta.Skins.Length : shopTab == 2 ? Meta.Hats.Length : Meta.Trails.Length;
            ScrollHandle(list, ref shopScroll[shopTab], count * (rowH + 6f)); float y = list.y - shopScroll[shopTab];
            for (int i = 0; i < count; i++)
            {
                var rr = new Rect(list.x, y, list.width, rowH); y += rowH + 6f; if (rr.yMax < list.y - 4 || rr.y > list.yMax + 4) continue;
                ShopRow(rr, list, i);
            }
            // ヘッダ・タブ・フッタ(リストのはみ出しを隠す)
            Panel(new Rect(P.x, P.y - 30f, P.width, tabsR.yMax + 8f - P.y + 30f), new Color(.17f, .15f, .38f, 1f), 0f);
            if (BackBtn(new Rect(ix, iy, 128f, 56f))) { panel = UiPanel.None; Store.Commit(); }
            Label(new Rect(ix + 138f, iy, 150, 56), "ショップ", St(28, TextAnchor.MiddleLeft), Color.white);
            CoinPill(new Rect(ix + iw - 186f, iy + 6f, 186f, 44f), d.coins);
            float tw = (iw - 3 * 6f) / 4f;
            for (int i = 0; i < 4; i++) if (Btn(new Rect(ix + i * (tw + 6f), tabsR.y, tw, 50f), tabs[i], i == shopTab ? Candy[3] : new Color(.4f, .38f, .62f), touchMode ? 15 : 17)) shopTab = i;
            Panel(new Rect(P.x, foot.y - 8f, P.width, P.yMax - foot.y + 8f), new Color(.17f, .15f, .38f, 1f), 0f);
            if (Btn(foot, "広告を見て +150 コイン", new Color(1f, .6f, .2f), 19)) ShowRewardedAd("150 コイン", () => { Store.D.coins += 150; Store.D.totalCoins += 150; Store.Commit(); toast = "+150 コイン!"; toastT = 1.5f; fx.PlayCash(); });
        }

        void ShopRow(Rect r, Rect clip, int i)
        {
            var d = Store.D; string name = "", sub = "", btn = ""; Color bc = new Color(.25f, .78f, .38f); bool enabled = true, equipped = false; Action tap = null; Color pa = Color.white, pb = Color.white;
            switch (shopTab)
            {
                case 0:
                    {
                        var u = Meta.Upgrades[i]; int lv = d.up[i]; name = $"{u.name}  Lv {lv}"; sub = u.desc;
                        if (lv >= u.max) { btn = "MAX"; enabled = false; } else { int c = Meta.UpgradeCost(i, lv); btn = Fmt(c); bc = d.coins >= c ? new Color(.25f, .78f, .38f) : new Color(.55f, .5f, .65f); tap = () => BuyUpgrade(i); }
                        break;
                    }
                case 1:
                    {
                        var s = Meta.Skins[i]; name = s.name; pa = s.shirt; pb = s.pants; bool own = d.ownSkin.Contains(i); equipped = d.skin == i;
                        sub = own ? (equipped ? "そうび中" : "もっている") : "かうと すぐ着替える"; btn = equipped ? "そうび中" : own ? "つかう" : Fmt(s.price); enabled = !equipped; bc = own ? Candy[3] : (d.coins >= s.price ? new Color(.25f, .78f, .38f) : new Color(.55f, .5f, .65f)); tap = () => BuyOrEquipSkin(i); break;
                    }
                case 2:
                    {
                        var h = Meta.Hats[i]; name = h.name; pa = i == 0 ? new Color(.5f, .5f, .6f) : h.color; pb = i == 0 ? pa : (h.color2.a > 0 ? h.color2 : h.color); bool own = d.ownHat.Contains(i); equipped = d.hat == i;
                        sub = own ? (equipped ? "そうび中" : "もっている") : "あたまにのせる"; btn = equipped ? "そうび中" : own ? "つかう" : Fmt(h.price); enabled = !equipped; bc = own ? Candy[3] : (d.coins >= h.price ? new Color(.25f, .78f, .38f) : new Color(.55f, .5f, .65f)); tap = () => BuyOrEquipHat(i); break;
                    }
                default:
                    {
                        var t = Meta.Trails[i]; name = t.name; pa = i == 0 ? new Color(.5f, .5f, .6f) : new Color(t.a.r, t.a.g, t.a.b, 1f); pb = i == 0 ? pa : new Color(t.b.r, t.b.g, t.b.b, 1f); bool own = d.ownTrail.Contains(i); equipped = d.trail == i;
                        sub = own ? (equipped ? "そうび中" : "もっている") : "走った跡が光る"; btn = equipped ? "そうび中" : own ? "つかう" : Fmt(t.price); enabled = !equipped; bc = own ? Candy[3] : (d.coins >= t.price ? new Color(.25f, .78f, .38f) : new Color(.55f, .5f, .65f)); tap = () => BuyOrEquipTrail(i); break;
                    }
            }
            Panel(r, equipped ? new Color(.28f, .5f, .42f, .98f) : new Color(.3f, .28f, .56f, .98f), 3f);
            var pv = new Rect(r.x + 10, r.center.y - 24, 48, 48);
            if (shopTab == 0)
            {
                var u = Meta.Upgrades[i]; Disc(pv, Candy[i % Candy.Length]); Label(pv, "" + d.up[i], St(24), Color.white);
            }
            else { Disc(pv, new Color(1, 1, 1, .95f)); Disc(new Rect(pv.x + 4, pv.y + 4, 40, 40), pa); Disc(new Rect(pv.x + 20, pv.y + 20, 24, 24), pb); }
            float bw = 112f;
            Label(new Rect(r.x + 68, r.y + 6, r.width - 68 - bw - 20, 30), name, St(touchMode ? 18 : 20, TextAnchor.MiddleLeft), Color.white);
            if (shopTab == 0)
            {   // レベルの目盛り
                var u = Meta.Upgrades[i]; float px = r.x + 68, pw = Mathf.Min(150f, r.width - 68 - bw - 20); for (int k = 0; k < u.max; k++) Bar(new Rect(px + k * (pw / u.max), r.y + 40, pw / u.max - 3f, 8), k < d.up[i] ? new Color(1f, .85f, .25f) : new Color(0, 0, 0, .35f));
                Label(new Rect(r.x + 68, r.y + 48, r.width - 68 - bw - 20, 24), sub, St(12, TextAnchor.MiddleLeft), new Color(1, 1, 1, .8f));
            }
            else Label(new Rect(r.x + 68, r.y + 38, r.width - 68 - bw - 20, 28), sub, St(14, TextAnchor.MiddleLeft), new Color(1, 1, 1, .8f));
            var br = new Rect(r.xMax - bw - 8, r.y + 10, bw, r.height - 20);
            if (Btn(br, "", bc, 16, enabled, false, clip)) tap?.Invoke();
            bool price = enabled && (shopTab == 0 || btn.Length > 0 && char.IsDigit(btn[0]));
            if (price) { CoinIcon(new Rect(br.x + 8, br.center.y - 11, 22, 22)); Label(new Rect(br.x + 30, br.y, br.width - 34, br.height), btn, St(btn.Length > 4 ? 15 : 18, TextAnchor.MiddleRight), Color.white); }
            else Label(br, btn, St(16), Color.white);
        }

        // ================== リザルト ==================
        void DrawResult(float W, float H)
        {
            float t = stateT; bool two = !L.portrait; float m = 10f;
            float cw = Mathf.Min(W - 2 * m, two ? 800f : 480f), chh = Mathf.Min(H - (L.y0 + (H - L.y1)) - 2 * m, two ? 440f : 700f);
            var p = new Rect(W / 2 - cw / 2, (L.y0 + L.y1) / 2f - chh / 2f, cw, chh); Panel(p, new Color(.2f, .18f, .42f, .97f), 8f);
            var left = two ? new Rect(p.x + 12, p.y + 8, p.width * 0.44f, p.height - 16) : new Rect(p.x + 12, p.y + 8, p.width - 24, 250f);
            var right = two ? new Rect(left.xMax + 6, p.y + 12, p.xMax - left.xMax - 18, p.height - 24) : new Rect(p.x + 14, left.yMax + 4, p.width - 28, p.height - left.height - 24);
            // 左(上): タイトル・時間・星
            float k = 1f + 0.5f * Mathf.Exp(-t * 8f); var mat = GUI.matrix; ScaleAround(new Vector2(k, k), new Vector2(left.center.x, left.y + 40));
            Label(new Rect(left.x, left.y, left.width, 76), spec.boss ? (res.rivalWon ? "WIN!" : "GOAL!") : "CLEAR!", new GUIStyle(huge) { fontSize = two ? 58 : 66 }, new Color(1, .9f, .3f)); GUI.matrix = mat;
            Label(new Rect(left.x, left.y + 74, left.width, 30), goalName, St(touchMode ? 18 : 20), Color.white);
            float shown = Mathf.Min(raceT, raceT * Mathf.Clamp01(t / 0.8f));
            Label(new Rect(left.x, left.y + 102, left.width, 70), $"{shown:F2} 秒", new GUIStyle(huge) { fontSize = two ? 52 : 58 }, Color.white);
            for (int i = 0; i < 3; i++)
            {
                float st = 0.9f + i * 0.4f; bool on = i < res.stars && t > st; float ks = on ? 1f + 1.0f * Mathf.Exp(-(t - st) * 9f) : 1f;
                var mat2 = GUI.matrix; var pv = new Vector2(left.center.x + (i - 1) * (two ? 78 : 90), left.y + 196); ScaleAround(new Vector2(ks, ks), pv);
                Label(new Rect(pv.x - 46, pv.y - 42, 92, 84), on ? "★" : "☆", new GUIStyle(huge) { fontSize = two ? 70 : 78 }, on ? new Color(1f, .88f, .2f) : new Color(1, 1, 1, 0.4f)); GUI.matrix = mat2;
            }
            string badge = res.newBest ? "NEW BEST!" : (res.levelUp ? "LEVEL UP!  次は LEVEL " + Store.D.level : ""); if (badge != "" && t > 1.8f) Label(new Rect(left.x, left.y + 238, left.width, 30), badge, St(21), new Color(.5f, 1f, .6f));
            if (res.hadBest && t > 1.8f) Label(new Rect(left.x, left.y + 214 + (two ? 30 : 56), left.width, 24), res.diff <= 0 ? $"前回ベストより {-res.diff:F1}秒 はやい!" : $"前回ベストまで あと {res.diff:F1}秒", St(14), res.diff <= 0 ? new Color(.5f, 1f, .6f) : new Color(1f, .85f, .6f));
            if (res.note != "" && t > 1.8f) Label(new Rect(left.x, left.y + 262, left.width, 28), res.note, St(14), new Color(1f, .8f, .6f));
            // 右(下): 報酬の内訳・ルーレット・ボタン
            float y = right.y; float a = Mathf.Clamp01((t - 1.2f) / 0.5f); var old = GUI.color; GUI.color = new Color(1, 1, 1, a);
            void Row(string n, int v, bool show = true) { if (!show || v <= 0) return; Label(new Rect(right.x + 6, y, right.width - 12, 26), n, St(17, TextAnchor.MiddleLeft), Color.white); Label(new Rect(right.x + 6, y, right.width - 12, 26), "+" + Fmt(v), St(18, TextAnchor.MiddleRight), new Color(1f, .92f, .4f)); y += 26f; }
            Row("コイン" + (spec.mod == 1 ? " (2倍)" : ""), res.collected); Row("クリア報酬", res.baseReward); Row("ノーダメージ", res.noHit); Row("コイン全部!", res.allCoins); Row("コンボ", res.comboBonus); Row("ライバルに勝利", res.rivalBonus);
            float inc = 1f + Meta.Upgrades[5].step * Store.D.up[5]; if (inc > 1.001f) { Label(new Rect(right.x + 6, y, right.width - 12, 24), $"コインボーナス ×{inc:F2}", St(14, TextAnchor.MiddleLeft), new Color(.7f, .9f, 1f)); y += 24f; }
            y += 4f; CoinIcon(new Rect(right.x + 6, y + 2, 30, 30)); Label(new Rect(right.x + 42, y, right.width - 48, 34), Fmt(res.total + res.extra), St(30, TextAnchor.MiddleLeft), new Color(1f, .92f, .4f));
            if (res.extra > 0) Label(new Rect(right.x, y, right.width - 8, 34), "(広告 +" + Fmt(res.extra) + ")", St(15, TextAnchor.MiddleRight), new Color(.7f, 1f, .8f));
            y += 40f; GUI.color = old;
            // ルーレット
            if (!res.claimed && t > 1.6f && !autoplay)
            {
                var bar = new Rect(right.x + 6, y, right.width - 12, 30); float[] segs = { 2, 3, 5, 3, 2 }; float tot = 15f, sx = bar.x; Color[] sc = { Candy[3], Candy[2], Candy[0], Candy[2], Candy[3] };
                for (int i = 0; i < segs.Length; i++) { float sw = bar.width * segs[i] / tot; Bar(new Rect(sx, bar.y, sw - 2f, bar.height), sc[i]); Label(new Rect(sx, bar.y, sw - 2f, bar.height), "×" + segs[i], St(16), Color.white); sx += sw; }
                float pp = rouletteFrozen >= 0f ? rouletteFrozen : Mathf.PingPong(Time.unscaledTime * 0.75f, 1f); float cx = bar.x + pp * bar.width;
                Disc(new Rect(cx - 9, bar.y - 8, 18, 18), Color.white); Bar(new Rect(cx - 2, bar.y - 4, 4, bar.height + 8), Color.white);
                int mult = RouletteValue(pp); y += 38f;
                if (Btn(new Rect(right.x + 6, y, right.width - 12, 48), "広告を見て ×" + mult, new Color(1f, .6f, .2f), 21))
                { rouletteFrozen = pp; int mm = mult; ShowRewardedAd($"ごほうび ×{mm}", () => { res.extra = res.total * (mm - 1); Store.D.coins += res.extra; Store.D.totalCoins += res.extra; Store.Commit(); res.claimed = true; res.mult = mm; fx.PlayCash(); rouletteFrozen = -1f; }); }
                y += 54f;
            }
            else if (res.claimed) { y += 4f; }
            // ボタン
            float bh = touchMode ? 58f : 52f; float by = Mathf.Max(y + 2f, right.yMax - (spec.free ? bh : bh * 2 + 8f) - 2f); if (!two) by = Mathf.Max(y + 2f, right.yMax - bh * 2 - 8f);
            bool canNext = !spec.free; float half = (right.width - 12 - 8f) / 2f;
            if (canNext) { if (Btn(new Rect(right.x + 6, by, right.width - 12, bh), touchMode ? "つぎのレベル" : "つぎのレベル [N]", new Color(.25f, .78f, .38f), 22) && t > 0.9f) { rouletteFrozen = -1f; AfterRun(() => StartLevel(levels.Make(Store.D.level))); } by += bh + 8f; }
            float third = (right.width - 12f - 16f) / 3f;
            if (Btn(new Rect(right.x + 6, by, third, bh), touchMode ? "もう一度" : "もう一度[R]", Candy[3], 15) && t > 0.9f) { rouletteFrozen = -1f; AfterRun(() => StartLevel(spec)); }
            if (Btn(new Rect(right.x + 14 + third, by, third, bh), touchMode ? "ホーム" : "ホーム[M]", Candy[4], 15) && t > 0.9f) { rouletteFrozen = -1f; AfterRun(GoHome); }
            if (Btn(new Rect(right.x + 22 + 2 * third, by, third, bh), "シェア", Candy[1], 15) && t > 0.9f) { GUIUtility.systemCopyBuffer = ShareText(); toast = "結果をコピーしました"; toastT = 1.6f; }
        }

        float rouletteFrozen = -1f;
        static int RouletteValue(float p) { float[] segs = { 2, 3, 5, 3, 2 }; float acc = 0f; foreach (var s in segs) { acc += s / 15f; if (p <= acc) return (int)s; } return 2; }

        // ================== しっぱい ==================
        void DrawFail(float W, float H)
        {
            float cw = Mathf.Min(W - 20f, 460f), chh = 400f; var p = new Rect(W / 2 - cw / 2, (L.y0 + L.y1) / 2f - chh / 2f, cw, chh); Panel(p, new Color(.26f, .16f, .34f, .97f), 8f);
            float k = 1f + 0.4f * Mathf.Exp(-stateT * 8f); var mat = GUI.matrix; ScaleAround(new Vector2(k, k), new Vector2(p.center.x, p.y + 50));
            Label(new Rect(p.x, p.y + 12, p.width, 80), failReason == "TIME UP" ? "TIME UP" : "ざんねん!", new GUIStyle(huge) { fontSize = 60 }, new Color(1f, .45f, .5f)); GUI.matrix = mat;
            Label(new Rect(p.x, p.y + 92, p.width, 30), failReason == "NO DAMAGE" ? "ノーダメージ条件がくずれました" : $"ゴールまで あと {(player.pos - goal).magnitude:F0} m", St(20), Color.white);
            { float prog = Mathf.Clamp01(1f - (player.pos - goal).magnitude / Mathf.Max(goalDist, 1f)); var tb = new Rect(p.x + 30, p.y + 126, p.width - 60, 12); Bar(tb, new Color(0, 0, 0, .45f)); Bar(new Rect(tb.x, tb.y, tb.width * prog, tb.height), prog > .85f ? new Color(1f, .85f, .2f) : new Color(.5f, .8f, 1f)); if ((player.pos - goal).magnitude < 40f) Label(new Rect(p.x, p.y + 140, p.width, 24), "あと少し!", St(16), new Color(1f, .9f, .3f)); }
            CoinIcon(new Rect(p.center.x - 70, p.y + 160, 30, 30)); Label(new Rect(p.center.x - 34, p.y + 158, 130, 34), "+" + Fmt(res.total), St(26, TextAnchor.MiddleLeft), new Color(1f, .92f, .4f));
            float y = p.y + 202f; float bw = p.width - 40f;
            if (failReason == "TIME UP" && !continueUsed && !spec.free)
            { if (Btn(new Rect(p.x + 20, y, bw, 56), "広告を見て +15秒 つづける", new Color(1f, .6f, .2f), 21) && stateT > 0.6f) ShowRewardedAd("+15秒でつづき", Continue); y += 60f; }
            else y += 4f;
            if (Btn(new Rect(p.x + 20, y, bw, 52), touchMode ? "もう一度" : "もう一度 [R]", new Color(.25f, .78f, .38f), 22) && stateT > 0.6f) AfterRun(() => StartLevel(spec)); y += 60f;
            if (Btn(new Rect(p.x + 20, y, bw, 46), touchMode ? "ホーム" : "ホーム [M]", Candy[4], 19) && stateT > 0.6f) AfterRun(GoHome);
        }

        // ================== ポーズ ==================
        void DrawPause(float W, float H)
        {
            Dim(0.6f); float cw = Mathf.Min(W - 20f, 420f), chh = 460f; var p = new Rect(W / 2 - cw / 2, (L.y0 + L.y1) / 2f - chh / 2f, cw, chh); Panel(p, new Color(.17f, .15f, .38f, .98f), 6f);
            Label(new Rect(p.x, p.y + 10, p.width, 54), "ポーズ", St(36), Color.white); float y = p.y + 74f; float bw = p.width - 40f;
            if (Btn(new Rect(p.x + 20, y, bw, 52), "つづける", new Color(.25f, .78f, .38f), 22)) Resume(); y += 60f;
            if (Btn(new Rect(p.x + 20, y, bw, 48), "やりなおす", Candy[3], 20)) StartLevel(spec); y += 56f;
            if (Btn(new Rect(p.x + 20, y, bw, 48), spec.free ? "スタックから抜ける" : "スタックから抜ける (+3秒)", Candy[1], 18)) UnstickPlayer(); y += 56f;
            if (Btn(new Rect(p.x + 20, y, bw, 48), "ホームにもどる", Candy[4], 20)) GoHome(); y += 56f;
            var d = Store.D; float hw = (bw - 8f) / 2f;
            if (Btn(new Rect(p.x + 20, y, hw, 44), "おんがく " + (d.music ? "ON" : "OFF"), d.music ? Candy[2] : new Color(.5f, .45f, .6f), 16)) { d.music = !d.music; fx.SetAudio(d.music, d.sfx); Store.Commit(); }
            if (Btn(new Rect(p.x + 28 + hw, y, hw, 44), "こうかおん " + (d.sfx ? "ON" : "OFF"), d.sfx ? Candy[2] : new Color(.5f, .45f, .6f), 16)) { d.sfx = !d.sfx; fx.SetAudio(d.music, d.sfx); Store.Commit(); }
        }
    }
}
