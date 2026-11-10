using System.Collections.Generic;
using UnityEngine;

namespace ShibuyaRush
{
    /// <summary>
    /// HUD・メニュー・演出表示(GameManagerの一部)。丸い面・太い縁取り文字・鮮やかな色のハイパーカジュアル風。
    /// 座標は「仮想UI座標」(短辺が基準サイズ。PCは720、スマホは480)。縦画面・横画面・ノッチ(セーフエリア)に対応する。
    /// </summary>
    public partial class GameManager
    {
        struct UiLayout
        {
            public float S, W, H, sR; public bool portrait, touch;
            public float x0, x1, y0, y1;                      // セーフエリア
            public Rect timer, chip, minimap, pause, coinPill; public Vector2 arrow, combo, sDash, sSlip, sShove;
        }
        UiLayout L;
        GUIStyle big, mid, small, box, round, huge; Texture2D shadeTex, circleTex, gradTex, iconBolt, iconGhost, iconBlast; Material ringMat;
        class FloatText { public string s; public Vector3 w; public Color c; public float age, life, size; }
        readonly List<FloatText> pops = new List<FloatText>();
        float combo, comboT, comboPop; int lastHits, comboBonus, lastCount = -1; float par3, par2, goalDist;
        int starsEarned; float roll, prevYaw, sq, sqV; GameObject playerRing, goalRing;
        static readonly Color[] Candy = { new Color(1f, .42f, .5f), new Color(1f, .68f, .25f), new Color(.38f, .8f, .5f), new Color(.35f, .65f, 1f), new Color(.7f, .5f, 1f), new Color(1f, .5f, .78f) };

        // ---------------- レイアウト ----------------
        void ComputeLayout()
        {
            L.touch = touchMode; float refShort = touchMode ? 480f : 720f;
            L.S = Mathf.Min(Screen.width, Screen.height) / refShort; L.W = Screen.width / L.S; L.H = Screen.height / L.S; L.portrait = L.W < L.H;
            var sa = Screen.safeArea; L.x0 = sa.xMin / L.S; L.x1 = sa.xMax / L.S; L.y0 = (Screen.height - sa.yMax) / L.S; L.y1 = (Screen.height - sa.yMin) / L.S;
            const float m = 12f; bool po = L.portrait;
            L.timer = new Rect(L.x0 + m, L.y0 + m, po ? 200 : 250, po ? 60 : 76);
            L.chip = new Rect(L.x0 + m, L.timer.yMax + 8, po ? Mathf.Min(L.W - 2 * (L.x0 + m) - 130f, 300f) : 380, 44);
            float mm = touchMode ? (po ? 118 : 150) : 220; L.minimap = new Rect(L.x1 - m - mm, L.y0 + m, mm, mm);
            L.pause = new Rect(L.timer.xMax + 8, L.timer.y + 2, 60, 60); L.coinPill = new Rect(L.x0 + m, L.chip.yMax + 8, 150, 36);
            L.arrow = new Vector2(L.W / 2f, po ? L.y0 + m + Mathf.Max(mm, 110f) + 62f : L.y0 + 92f);
            if (touchMode)
            {
                L.sR = 46f; var b = new Vector2(L.x1 - m - 72f, L.y1 - m - 72f);
                L.sSlip = b; L.sDash = b + new Vector2(-112f, 14f); L.sShove = b + new Vector2(-10f, -114f);
                L.combo = new Vector2(L.x0 + m + 110f, L.y0 + (po ? 360f : 215f));
            }
            else
            {
                L.sR = 40f; float cy = L.y1 - 84f; L.sDash = new Vector2(L.W / 2 - 130f, cy); L.sSlip = new Vector2(L.W / 2, cy); L.sShove = new Vector2(L.W / 2 + 130f, cy);
                L.combo = new Vector2(L.x1 - 120f, L.y0 + 330f);
            }
        }

        // ---------------- スタイル・部品 ----------------
        void InitStyles()
        {
            if (big != null) return;
            var font = Resources.Load<Font>("Fonts/MPLUSRounded1c-ExtraBold");
            GUIStyle S(int sz, TextAnchor a) { var st = new GUIStyle(GUI.skin.label) { font = font, fontSize = sz, fontStyle = FontStyle.Normal, alignment = a, richText = true, wordWrap = false }; st.normal.textColor = Color.white; return st; }
            big = S(54, TextAnchor.MiddleCenter); mid = S(26, TextAnchor.MiddleLeft); small = S(18, TextAnchor.MiddleLeft); huge = S(88, TextAnchor.MiddleCenter);
            box = new GUIStyle(GUI.skin.box); var t = new Texture2D(1, 1); t.SetPixel(0, 0, new Color(0, 0, 0, 0.55f)); t.Apply(); box.normal.background = t;
            const int N = 64, R = 26; var rt = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float cx = Mathf.Clamp(x + .5f, R, N - R), cy = Mathf.Clamp(y + .5f, R, N - R);
                float d = Mathf.Sqrt((x + .5f - cx) * (x + .5f - cx) + (y + .5f - cy) * (y + .5f - cy));
                rt.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(R - d + 0.5f)));
            }
            rt.Apply(); round = new GUIStyle { border = new RectOffset(R, R, R, R) }; round.normal.background = rt;
            var cs = new Texture2D(128, 128, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) { float r = Mathf.Sqrt(((x + .5f) / 128 - .5f) * ((x + .5f) / 128 - .5f) + ((y + .5f) / 128 - .5f) * ((y + .5f) / 128 - .5f)); cs.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((0.5f - r) * 128f * 0.5f))); }
            cs.Apply(); circleTex = cs;
            shadeTex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 64; y++) shadeTex.SetPixel(0, y, new Color(0.08f, 0.05f, 0.2f, Mathf.Pow(y / 63f, 1.6f) * 0.7f)); shadeTex.Apply();
            gradTex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 64; y++) gradTex.SetPixel(0, y, Color.Lerp(new Color(.42f, .5f, 1f, .93f), new Color(1f, .62f, .45f, .93f), y / 63f));
            gradTex.Apply();
            // スキルのアイコン(白いシルエットを描く)
            iconBolt = Icon(new[] { new Vector2(.60f, .97f), new Vector2(.22f, .50f), new Vector2(.45f, .50f), new Vector2(.34f, .03f), new Vector2(.80f, .58f), new Vector2(.55f, .58f) }, null);
            iconGhost = Icon(null, (x, y) =>
            {
                bool head = (x - .5f) * (x - .5f) + (y - .56f) * (y - .56f) < .32f * .32f; bool body = x > .18f && x < .82f && y > .10f + 0.045f * Mathf.Sin(x * 22f) && y <= .56f;
                bool eyeL = (x - .38f) * (x - .38f) + (y - .60f) * (y - .60f) < .065f * .065f, eyeR = (x - .62f) * (x - .62f) + (y - .60f) * (y - .60f) < .065f * .065f;
                return (head || body) && !eyeL && !eyeR;
            });
            var star = new List<Vector2>(); for (int i = 0; i < 16; i++) { float a = i * Mathf.PI / 8f, r = i % 2 == 0 ? .48f : .20f; star.Add(new Vector2(.5f + Mathf.Cos(a) * r, .5f + Mathf.Sin(a) * r)); }
            iconBlast = Icon(star.ToArray(), null);
        }

        static Texture2D Icon(Vector2[] poly, System.Func<float, float, bool> f)
        {
            const int N = 96; var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                int hit = 0;
                for (int sy = 0; sy < 3; sy++) for (int sx = 0; sx < 3; sx++)
                {
                    float u = (x + (sx + .5f) / 3f) / N, v = (y + (sy + .5f) / 3f) / N;
                    if (poly != null ? PointInPoly(poly, u, v) : f(u, v)) hit++;
                }
                t.SetPixel(x, y, new Color(1, 1, 1, hit / 9f));
            }
            t.Apply(); return t;
        }

        static bool PointInPoly(Vector2[] p, float x, float y)
        {
            bool c = false;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                if ((p[i].y > y) != (p[j].y > y) && x < (p[j].x - p[i].x) * (y - p[i].y) / (p[j].y - p[i].y) + p[i].x) c = !c;
            return c;
        }

        /// <summary>縁取りつき文字(大きい文字ほど縁取りを太く)。</summary>
        void Label(Rect r, string s, GUIStyle st, Color c)
        {
            float k = Mathf.Clamp(st.fontSize / 26f, 1f, 3.2f); st.normal.textColor = new Color(0.1f, 0.08f, 0.2f, 0.92f);
            foreach (var o in new[] { new Vector2(-2, 0), new Vector2(2, 0), new Vector2(0, -2), new Vector2(0, 2), new Vector2(0, 4), new Vector2(-1.5f, 1.5f), new Vector2(1.5f, 1.5f), new Vector2(-1.5f, -1.5f), new Vector2(1.5f, -1.5f) })
                GUI.Label(new Rect(r.x + o.x * k, r.y + o.y * k, r.width, r.height), s, st);
            st.normal.textColor = c; GUI.Label(r, s, st);
        }

        void Panel(Rect r, Color c, float depth = 5f)
        {
            var old = GUI.color; GUI.color = new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.55f, c.a); GUI.Box(new Rect(r.x, r.y + depth, r.width, r.height), GUIContent.none, round);
            GUI.color = c; GUI.Box(r, GUIContent.none, round); GUI.color = old;
        }

        void Disc(Rect r, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, circleTex); GUI.color = old; }
        void Pop(string text, Vector3 world, Color c, float size = 1f) { pops.Add(new FloatText { s = text, w = world, c = c, life = 1.0f, size = size }); if (pops.Count > 24) pops.RemoveAt(0); }

        // ---------------- OnGUI ----------------
        void OnGUI()
        {
            if (data == null) return; InitStyles(); ComputeLayout();
            GUI.matrix = Matrix4x4.Scale(new Vector3(L.S, L.S, 1)); float W = L.W, H = L.H;
            PreUI();
            if (state == GameState.Menu) DrawHome();
            else
            {
                DrawOverlays(W, H);
                DrawTopLeft();
                DrawGoalArrow(W, H, (player.pos - goal).magnitude);
                DrawMinimap();
                DrawProgress(W, H);
                DrawCombo();
                DrawSkills();
                DrawJoystick();
                DrawPops(L.S);
                DrawRivalTag();
                DrawHint();
                if (hitFlash > 0) Label(new Rect(0, H / 2 + 50, W, 60), "車に接触!  +3秒", new GUIStyle(big) { fontSize = L.portrait ? 40 : 54 }, new Color(1, 0.35f, 0.3f));
                DrawBanner(W, H);
                if (state == GameState.Countdown) DrawCountdown(W, H);
                if (state == GameState.Playing && Btn(L.pause, "", new Color(.25f, .22f, .5f, .85f), 20, true, false)) Pause();
                if (state == GameState.Playing) DrawPauseGlyph(L.pause);
                if (state == GameState.Finished) DrawResult(W, H);
                if (state == GameState.Failed) DrawFail(W, H);
                if (state == GameState.Paused) DrawPause(W, H);
            }
            DrawToasts(W); DrawAd(W, H);
            PostUI();
        }

        void DrawTopLeft()
        {
            bool frozen = fx.ClockFrozen; var r = L.timer;
            Panel(r, frozen ? new Color(.2f, .55f, .95f, .92f) : new Color(.18f, .17f, .36f, .82f), 4f);
            bool lim = !spec.free; float shownT = lim ? Mathf.Max(0f, Remaining) : raceT; bool urgent = lim && shownT < 10f && state == GameState.Playing;
            var tc = urgent ? Color.Lerp(Color.white, new Color(1f, .3f, .3f), 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 12f)) : Color.white;
            Label(new Rect(r.x + 10, r.y, r.width - 18, r.height), (frozen ? "❚❚ " : "") + (lim ? $"{shownT:F1}" : $"{shownT:F2}"), new GUIStyle(huge) { fontSize = (int)(r.height * 0.66f), alignment = TextAnchor.MiddleLeft }, tc);
            Label(new Rect(r.xMax - 40, r.y + r.height * 0.42f, 36, r.height * 0.5f), lim ? "のこり" : "秒", new GUIStyle(small) { fontSize = lim ? 12 : 18 }, new Color(1, 1, 1, 0.85f));
            Panel(L.chip, new Color(.14f, .12f, .32f, .93f), 3f);
            { int fs = 22; var gs = new GUIStyle(mid) { fontSize = fs, alignment = TextAnchor.MiddleLeft }; while (fs > 13 && gs.CalcSize(new GUIContent(goalName)).x > L.chip.width - 50f) { fs--; gs.fontSize = fs; }
              Label(new Rect(L.chip.x + 10, L.chip.y, 30, L.chip.height), "▶", new GUIStyle(mid) { fontSize = 20, alignment = TextAnchor.MiddleLeft }, new Color(1f, .85f, .2f));
              Label(new Rect(L.chip.x + 38, L.chip.y, L.chip.width - 44, L.chip.height), goalName, gs, Color.white); }
            // コインの数(取るたびにぽんと弾む)
            float cp = 1f + 0.35f * Mathf.Exp(-(Time.unscaledTime - coinPulse) * 10f); var pr = L.coinPill; var mt = GUI.matrix; GUIUtility.ScaleAroundPivot(new Vector2(cp, cp), pr.center);
            Panel(pr, new Color(.18f, .17f, .36f, .82f), 3f); CoinIcon(new Rect(pr.x + 6, pr.y + 5, 26, 26));
            Label(new Rect(pr.x + 38, pr.y, pr.width - 44, pr.height), Fmt(runCoins * (spec.mod == 1 ? 2 : 1)), new GUIStyle(mid) { fontSize = 22 }, new Color(1f, .9f, .35f)); GUI.matrix = mt;
            float py = pr.yMax + 6;
            if (trickN >= 2) { Chip(new Rect(pr.x, py, 150, 28), "TRICK ×" + Mathf.Min(4, trickN), new Color(.9f, .4f, .75f)); py += 32; }
            if (magnetT > 0f) { Chip(new Rect(pr.x, py, 150, 28), $"マグネット {magnetT:F0}", Pickups.PuColor[0]); py += 32; }
            if (player.shieldT > 0f) { Chip(new Rect(pr.x, py, 150, 28), $"シールド {player.shieldT:F0}", new Color(.5f, .8f, .9f)); py += 32; }
            if (spec.mod > 0) Chip(new Rect(pr.x, py, 150, 28), spec.modText, new Color(1f, .5f, .35f));
        }

        void DrawMinimap()
        {
            var mr = L.minimap; float full = 220f, k = mr.width / full;
            Panel(new Rect(mr.x - 5, mr.y - 5, mr.width + 10, mr.height + 10), new Color(1, 1, 1, 0.9f), 4f);
            GUI.DrawTexture(mr, miniRt, ScaleMode.StretchToFill, false);
            Disc(new Rect(mr.center.x - 6 * k - 1, mr.center.y - 6 * k - 1, 12 * k + 2, 12 * k + 2), new Color(1f, .45f, .1f));
            var rel = (goal - player.pos) / 110f; var gp = mr.center + new Vector2(rel.x, -rel.y) * (mr.width / 2f);
            gp = new Vector2(Mathf.Clamp(gp.x, mr.xMin + 8, mr.xMax - 8), Mathf.Clamp(gp.y, mr.yMin + 8, mr.yMax - 8));
            float pu = 1f + 0.2f * Mathf.Sin(Time.unscaledTime * 6f); float gs = 8f * pu * Mathf.Max(0.8f, k); Disc(new Rect(gp.x - gs, gp.y - gs, gs * 2, gs * 2), new Color(1, .82f, .1f));
        }

        void DrawCombo()
        {
            if (combo < 2) return;
            float k = 1f + 0.55f * Mathf.Exp(-(Time.unscaledTime - comboPop) * 9f);
            float lv = Mathf.Clamp01(combo / 60f); var col = Color.Lerp(Color.Lerp(Color.white, new Color(1f, .9f, .2f), Mathf.Clamp01(lv * 2)), new Color(1f, .35f, .55f), Mathf.Clamp01(lv * 2 - 1));
            var mat = GUI.matrix; var piv = L.combo; GUIUtility.ScaleAroundPivot(new Vector2(k, k), piv);
            Label(new Rect(piv.x - 130, piv.y - 70, 260, 34), "COMBO", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 22 }, new Color(1, 1, 1, 0.95f));
            Label(new Rect(piv.x - 130, piv.y - 40, 260, 90), "x" + (int)combo, new GUIStyle(huge) { fontSize = touchMode ? 64 : 84 }, col);
            var old = GUI.color; GUI.color = new Color(1, 1, 1, 0.9f); GUI.DrawTexture(new Rect(piv.x - 90, piv.y + 56, 180 * Mathf.Clamp01(comboT / 1.6f), 8), Texture2D.whiteTexture); GUI.color = old;
            GUI.matrix = mat;
        }

        void DrawSkills()
        {
            Skill(L.sDash, "Z", "ダッシュ", iconBolt, player.dashCd, player.DashCd, player.dashT > 0, new Color(1f, .72f, .2f));
            Skill(L.sSlip, "X", "すり抜け", iconGhost, player.slipCd, player.SlipCd, player.Slipping, new Color(.4f, .85f, 1f));
            Skill(L.sShove, "C", "かき分け", iconBlast, player.shoveCd, player.ShoveCd, player.shoveFx > 0, new Color(1f, .45f, .5f));
        }

        void Skill(Vector2 c, string key, string name, Texture2D icon, float cd, float total, bool active, Color col)
        {
            float R = L.sR; bool ready = cd <= 0f; float f = ready || active ? 1f : 1f - cd / total;
            float pulse = ready ? 1f + 0.05f * Mathf.Sin(Time.unscaledTime * 7f) : 1f; if (active) pulse = 1.2f;
            var rr = new Rect(c.x - R * pulse - 5, c.y - R * pulse - 5, (R * pulse + 5) * 2, (R * pulse + 5) * 2);
            Disc(rr, ready || active ? new Color(1, 1, 1, 0.95f) : new Color(1, 1, 1, 0.55f));
            var inner = new Rect(c.x - R * pulse, c.y - R * pulse, R * 2 * pulse, R * 2 * pulse);
            Disc(inner, new Color(.16f, .14f, .3f, 0.95f));
            GUI.BeginGroup(new Rect(inner.x, inner.y + inner.height * (1f - f), inner.width, inner.height * f));
            Disc(new Rect(0, -inner.height * (1f - f), inner.width, inner.height), active ? Color.white : col);
            GUI.EndGroup();
            var ir = new Rect(c.x - R * 0.62f, c.y - R * 0.62f - (ready ? 0f : 6f), R * 1.24f, R * 1.24f); var old = GUI.color;
            GUI.color = active ? new Color(.16f, .14f, .3f) : (ready ? new Color(.16f, .14f, .3f, 0.95f) : new Color(1, 1, 1, 0.5f)); GUI.DrawTexture(ir, icon); GUI.color = old;
            if (!ready && !active) Label(new Rect(c.x - R, c.y + R * 0.15f, R * 2, R * 0.8f), $"{cd:F1}", new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter, fontSize = 20 }, Color.white);
            if (!touchMode) Label(new Rect(c.x - 70, c.y + R + 2, 140, 24), name + "  [" + key + "]", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 14 }, new Color(1, 1, 1, 0.95f));
        }

        void DrawJoystick()
        {
            if (state != GameState.Playing) return;
            var old = GUI.color;
            if (input.stickActive)
            {
                Disc(new Rect(input.stickOrigin.x - 70, input.stickOrigin.y - 70, 140, 140), new Color(1, 1, 1, 0.22f));
                Disc(new Rect(input.stickPos.x - 30, input.stickPos.y - 30, 60, 60), new Color(1, 1, 1, 0.75f));
            }
            else if (touchMode && raceT < 8f)
            {   // 初めの数秒だけ、操作の見本を出す
                float a = 0.5f + 0.3f * Mathf.Sin(Time.unscaledTime * 4f); var c = new Vector2(L.x0 + 130f, L.y1 - 110f);
                Disc(new Rect(c.x - 60, c.y - 60, 120, 120), new Color(1, 1, 1, 0.16f)); Disc(new Rect(c.x - 26, c.y - 26, 52, 52), new Color(1, 1, 1, a));
                Label(new Rect(c.x - 100, c.y - 100, 200, 30), "ドラッグで走る", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 16 }, Color.white);
            }
            GUI.color = old;
        }

        void DrawPops(float sc)
        {
            foreach (var p in pops)
            {
                var sp = cam.WorldToScreenPoint(p.w + Vector3.up * (p.age * 2.2f)); if (sp.z < 0) continue;
                float k = (1f + 0.8f * Mathf.Exp(-p.age * 12f)) * p.size; float a = Mathf.Clamp01((p.life - p.age) / 0.35f);
                var mat = GUI.matrix; var pv = new Vector2(sp.x / sc, (Screen.height - sp.y) / sc); GUIUtility.ScaleAroundPivot(new Vector2(k, k), pv);
                var c = p.c; c.a = a; Label(new Rect(pv.x - 100, pv.y - 20, 200, 40), p.s, new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter, fontSize = 28 }, c);
                GUI.matrix = mat;
            }
        }

        void DrawCountdown(float W, float H)
        {
            int n = Mathf.Max(1, 3 - (int)stateT); float frac = stateT - Mathf.Floor(stateT);
            float k = 1f + 1.2f * Mathf.Exp(-frac * 9f); var mat = GUI.matrix; var piv = new Vector2(W / 2, H / 2 - 40); GUIUtility.ScaleAroundPivot(new Vector2(k, k), piv);
            Label(new Rect(0, piv.y - 70, W, 140), n.ToString(), new GUIStyle(huge) { fontSize = 130 }, Candy[(3 - n) % Candy.Length]);
            GUI.matrix = mat;
        }

        // ---------------- 演出・矢印 ----------------
        void DrawOverlays(float W, float H)
        {
            var old = GUI.color; var full = new Rect(0, 0, W, H);
            var vc = fx.VignetteColor; if (vc.a > 0.01f) { GUI.color = vc; GUI.DrawTexture(full, vigTex, ScaleMode.StretchToFill, true); }
            if (fx.SpeedLines > 0.02f)
            {
                var mat = GUI.matrix; var ctr = new Vector2(W / 2, H / 2); float t = Time.unscaledTime, sh = Mathf.Min(W, H);
                for (int i = 0; i < 56; i++)
                {
                    float ang = i * (360f / 56f) + Mathf.Sin(i * 12.9898f + Mathf.Floor(t * 24f)) * 3f;
                    float r0 = sh * (0.36f + 0.1f * Mathf.Abs(Mathf.Sin(i * 3.1f + Mathf.Floor(t * 30f)))), len = sh * (0.18f + 0.3f * Mathf.Abs(Mathf.Sin(i * 7.7f)));
                    GUI.matrix = mat; GUIUtility.RotateAroundPivot(ang, ctr);
                    GUI.color = new Color(1, 1, 1, 0.55f * fx.SpeedLines * (0.4f + 0.6f * Mathf.Abs(Mathf.Sin(i * 5.3f))));
                    GUI.DrawTexture(new Rect(ctr.x - 1.5f, ctr.y - r0 - len, 3f, len), Texture2D.whiteTexture);
                }
                GUI.matrix = mat;
            }
            var fl = fx.Flash; if (fl.a > 0.01f) { GUI.color = fl; GUI.DrawTexture(full, Texture2D.whiteTexture); }
            GUI.color = old;
        }

        void DrawBanner(float W, float H)
        {
            if (fx.BannerT <= 0f) return;
            float a = Mathf.Clamp01(fx.BannerT / 0.35f), age = 1f - Mathf.Clamp01(fx.BannerT / 1.4f);
            float pop = 1f + 0.9f * Mathf.Exp(-age * 14f);
            var mat = GUI.matrix; var piv = new Vector2(W / 2, H * (L.portrait ? 0.30f : 0.36f));
            GUIUtility.ScaleAroundPivot(new Vector2(pop, pop), piv);
            var c = fx.BannerColor; c.a = a; int fs = L.portrait || touchMode ? 44 : 54;
            Label(new Rect(0, piv.y - 45, W, 90), fx.Banner, new GUIStyle(big) { fontSize = fs }, c);
            Label(new Rect(0, piv.y + 30, W, 40), fx.BannerSub, new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter, fontSize = touchMode ? 20 : 26 }, new Color(1, 1, 1, a));
            GUI.matrix = mat;
        }

        /// <summary>画面上部の大きな矢印: カメラの向きに対する目的地の方角。</summary>
        void DrawGoalArrow(float W, float H, float dist)
        {
            var to = goal - player.pos; float bearing = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
            float rel = Mathf.DeltaAngle(camYaw, bearing);
            var ctr = L.arrow; var mat = GUI.matrix; var old = GUI.color;
            float pulse = 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 6f); float sz = (touchMode ? 80f : 96f) * pulse;
            GUIUtility.RotateAroundPivot(rel, ctr);
            GUI.color = new Color(0, 0, 0, 0.6f); GUI.DrawTexture(new Rect(ctr.x - sz / 2 - 6, ctr.y - sz / 2 - 6, sz + 12, sz + 12), arrowTex);
            GUI.color = new Color(1f, 0.85f, 0.15f, 1f); GUI.DrawTexture(new Rect(ctr.x - sz / 2, ctr.y - sz / 2, sz, sz), arrowTex);
            GUI.matrix = mat; GUI.color = old;
            Label(new Rect(W / 2 - 160, ctr.y + sz / 2 + 4, 320, 34), $"{dist:F0} m", new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter }, new Color(1, 0.92f, 0.35f));
        }

        void UpdateRings()
        {
            bool on = state == GameState.Countdown || state == GameState.Playing;
            playerRing.SetActive(on); goalRing.SetActive(on);
            if (!on) return;
            float t = Time.unscaledTime; var hf = Heightfield.Current;
            playerRing.transform.position = new Vector3(player.pos.x, hf.Height(player.pos.x, player.pos.y) + player.lift + 0.12f, player.pos.y);
            playerRing.transform.rotation = Quaternion.Euler(90, 0, 0); float ps = 2.4f * (1f + 0.07f * Mathf.Sin(t * 8f)); playerRing.transform.localScale = new Vector3(ps, ps, 1);
            playerRing.GetComponent<MeshRenderer>().material.SetColor("_BaseColor", new Color(1f, .75f, .15f, 0.95f));
            goalRing.transform.position = new Vector3(goal.x, hf.Height(goal.x, goal.y) + goalLift + 0.15f, goal.y);
            goalRing.transform.rotation = Quaternion.Euler(90, 0, 0); float gs = GoalRadius * 2f * (1f + 0.06f * Mathf.Sin(t * 4f)); goalRing.transform.localScale = new Vector3(gs, gs, 1);
            goalRing.GetComponent<MeshRenderer>().material.SetColor("_BaseColor", new Color(1f, .85f, .2f, 0.9f));
        }

        /// <summary>プレイヤーの前方に浮かぶ3Dの矢印。目的地の方向を指す。</summary>
        void UpdateArrow()
        {
            bool on = state == GameState.Countdown || state == GameState.Playing;
            arrow3d.SetActive(on); if (!on) return;
            var to = goal - player.pos; var dir = to.sqrMagnitude > 1e-4f ? to.normalized : Vector2.up;
            var p = new Vector3(player.pos.x, Heightfield.Current.Height(player.pos.x, player.pos.y) + player.lift, player.pos.y);
            arrow3d.transform.position = p + new Vector3(dir.x, 0, dir.y) * 1.9f + Vector3.up * (2.5f + 0.12f * Mathf.Sin(Time.unscaledTime * 5f));
            arrow3d.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y));
            float k = 1.1f + 0.1f * Mathf.Sin(Time.unscaledTime * 5f); arrow3d.transform.localScale = new Vector3(k, k, k);
        }

        static Mesh ArrowMesh()
        {
            var v = new List<Vector3>(); var t = new List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c) { int s = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(s); t.Add(s + 1); t.Add(s + 2); }
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(a, b, c); Tri(a, c, d); }
            float w = 0.13f, h = 0.09f, L0 = 0.9f;
            Quad(new Vector3(-w, h, 0), new Vector3(-w, h, L0), new Vector3(w, h, L0), new Vector3(w, h, 0));
            Quad(new Vector3(-w, -h, 0), new Vector3(w, -h, 0), new Vector3(w, -h, L0), new Vector3(-w, -h, L0));
            Quad(new Vector3(-w, -h, 0), new Vector3(-w, -h, L0), new Vector3(-w, h, L0), new Vector3(-w, h, 0));
            Quad(new Vector3(w, -h, 0), new Vector3(w, h, 0), new Vector3(w, h, L0), new Vector3(w, -h, L0));
            Quad(new Vector3(-w, -h, 0), new Vector3(-w, h, 0), new Vector3(w, h, 0), new Vector3(w, -h, 0));
            float hw = 0.42f, hh = 0.12f, tip = 1.7f;
            var a0 = new Vector3(-hw, -hh, L0); var b0 = new Vector3(hw, -hh, L0); var c0 = new Vector3(hw, hh, L0); var d0 = new Vector3(-hw, hh, L0); var ap = new Vector3(0, 0, tip);
            Quad(a0, d0, c0, b0); Tri(a0, ap, d0); Tri(d0, ap, c0); Tri(c0, ap, b0); Tri(b0, ap, a0);
            var m = new Mesh { name = "GoalArrow" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }

        static Texture2D MakeArrowTex()
        {
            const int N = 128; var tx = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float u = (x + .5f) / N - .5f, vv = (y + .5f) / N;
                bool head = vv > 0.5f && Mathf.Abs(u) < (1f - vv) * 0.9f && vv < 0.97f; bool shaft = vv <= 0.55f && vv > 0.08f && Mathf.Abs(u) < 0.12f;
                tx.SetPixel(x, y, head || shaft ? Color.white : Color.clear);
            }
            tx.Apply(); return tx;
        }

        static Texture2D MakeVignetteTex()
        {
            const int N = 128; var tx = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float u = (x + .5f) / N * 2 - 1, vv = (y + .5f) / N * 2 - 1; float r = Mathf.Sqrt(u * u + vv * vv) / 1.2f;
                tx.SetPixel(x, y, new Color(1, 1, 1, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 0.45f) / 0.55f))));
            }
            tx.Apply(); return tx;
        }
    }
}
