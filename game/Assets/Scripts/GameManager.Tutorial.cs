using UnityEngine;

namespace ShibuyaRush
{
    /// <summary>
    /// はじめての操作練習(チュートリアル)。PCとスマホで同じ道を走りながら、
    /// 1.走る 2.視点を回す 3.ダッシュ 4.すり抜け 5.かき分け 6.ゴール の順に、できたら次へ進む。
    /// 時間制限はなく、各ステップは「とばす」で飛ばせる。クリアすると tutorialDone が保存され、次回から自動では出ない。
    /// </summary>
    public partial class GameManager
    {
        const int TutSteps = 6;
        int tutStep, tutDash0, tutSlip0, tutShove0; float tutOk, tutCam; Vector2 tutStart;
        static readonly string[] TutTitle = { "うごく", "視点をまわす", "ダッシュ", "すり抜け", "かき分け", "ゴールへ" };

        string TutText(int step)
        {
            if (touchMode)
                switch (step)
                {
                    case 0: return "画面をドラッグして走ろう。指を動かした方向へ進むよ";
                    case 1: return "画面の右側を指でスワイプして、視点を回してみよう";
                    case 2: return "人にぶつかると弾かれる! 黄色いボタンでダッシュ。人を押しのけて進める";
                    case 3: return "水色のボタンですり抜け。人の間を通り抜けられる";
                    case 4: return "ピンクのボタンでかき分け。目の前の人を吹き飛ばす";
                    default: return "黄色い矢印と光の柱がゴール。そこまで走ろう!";
                }
            switch (step)
            {
                case 0: return "WASD か 矢印キーで走ろう。マウスの左ドラッグでもOK";
                case 1: return "Q キーか、右クリックのドラッグで、視点を回してみよう";
                case 2: return "人にぶつかると弾かれる! Shift キーでダッシュ。人を押しのけて進める";
                case 3: return "Space キーですり抜け。人の間を通り抜けられる";
                case 4: return "E キーでかき分け。目の前の人を吹き飛ばす";
                default: return "黄色い矢印と光の柱がゴール。そこまで走ろう!";
            }
        }

        void TutBegin()
        {
            tutStep = 0; tutOk = 0f; tutCam = 0f; tutStart = player.pos; tutDash0 = player.dashN; tutSlip0 = player.slipN; tutShove0 = player.shoveN;
        }

        void TutNext() { tutStep = Mathf.Min(tutStep + 1, TutSteps - 1); tutOk = 0f; }

        void TutUpdate(float rdt)
        {
            if (tutOk > 0f) { tutOk -= rdt; if (tutOk <= 0f) TutNext(); return; }
            bool done = false;
            switch (tutStep)
            {
                case 0: done = (player.pos - tutStart).magnitude > 10f; break;
                case 1: tutCam += Mathf.Abs(input.camDelta); done = tutCam > 45f; break;
                case 2: done = player.dashN > tutDash0; break;
                case 3: done = player.slipN > tutSlip0; break;
                case 4: done = player.shoveN > tutShove0; break;
            }
            if (done) { tutOk = 0.9f; fx.PlayCoin(10); Pop("OK!", pw0() + Vector3.up * 3f, new Color(.5f, 1f, .6f), 1.5f); }
        }

        /// <summary>説明パネルの位置(操作ボタンや指と重ならない場所)。</summary>
        Rect TutPanel()
        {
            float h = 118f;
            if (L.portrait) { float w = Mathf.Min(L.W - 24f, 470f); return new Rect(L.W / 2 - w / 2, L.H * 0.50f, w, h); }
            if (touchMode) { float w = Mathf.Min(520f, L.W - 2f * 270f); return new Rect(L.W / 2 - w / 2, L.y1 - 12f - h, w, h); }
            float pw = Mathf.Min(560f, L.W - 40f); return new Rect(L.W / 2 - pw / 2, L.y0 + 200f, pw, h);   // PC は上寄り(下は主人公の足元と重なる)
        }

        Vector2? TutSkillPos() => tutStep == 2 ? L.sDash : tutStep == 3 ? L.sSlip : tutStep == 4 ? L.sShove : (Vector2?)null;

        void DrawTutorial()
        {
            if (spec == null || !spec.tutorial || (state != GameState.Playing && state != GameState.Countdown)) return;
            var r = TutPanel(); bool ok = tutOk > 0f;
            // 押してほしいボタンを光らせる
            var sk = TutSkillPos();
            if (sk.HasValue && !ok) { float p = 1f + 0.16f * Mathf.Sin(Time.unscaledTime * 8f); float R = L.sR * p + 9f; Disc(new Rect(sk.Value.x - R, sk.Value.y - R, R * 2, R * 2), new Color(1f, .9f, .3f, .6f)); }
            Panel(r, ok ? new Color(.16f, .5f, .3f, .95f) : new Color(.15f, .13f, .35f, .93f), 4f);
            Label(new Rect(r.x + 14, r.y + 6, r.width - 130, 26), $"STEP {tutStep + 1}/{TutSteps}   {TutTitle[tutStep]}", St(16, TextAnchor.MiddleLeft), new Color(1f, .9f, .4f));
            Label(new Rect(r.x + 14, r.y + 34, r.width - 28, r.height - 56), ok ? "OK!  いいね!" : TutText(tutStep), St(touchMode ? 18 : 20, TextAnchor.UpperLeft, true), Color.white);
            for (int i = 0; i < TutSteps; i++) Disc(new Rect(r.x + 16 + i * 16, r.yMax - 16, 10, 10), i < tutStep || (ok && i == tutStep) ? new Color(1f, .9f, .3f) : new Color(1, 1, 1, .3f));
            if (tutStep < TutSteps - 1 && state == GameState.Playing && Btn(new Rect(r.xMax - 108, r.y + 6, 96, 30), "とばす ▶", new Color(.4f, .38f, .62f, .95f), 14)) TutNext();
        }

        /// <summary>チュートリアルをここで終えて、本編(LEVEL 1)へ。</summary>
        void TutQuit()
        {
            Store.D.tutorialDone = true; Store.Commit(); Resume(); StartLevel(levels.Make(Store.D.level));
        }
    }
}
