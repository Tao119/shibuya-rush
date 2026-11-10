using System.Collections.Generic;
using UnityEngine;

namespace ShibuyaRush
{
    /// <summary>
    /// キーボード・マウス・タッチをまとめる入力。
    /// 画面のどこかを押してドラッグすると、その方向へ走る(仮想スティック。マウスでもタッチでも同じ)。
    /// スキルは画面上の丸ボタンか、Shift/Space/E。視点は自動で進行方向に追従し、右ドラッグ・←→・右側のスワイプで回せる。
    /// 座標はすべて「仮想UI座標」(左上が原点、短辺が基準サイズになる単位)。
    /// </summary>
    public class InputHub
    {
        public struct Circle { public Vector2 c; public float r; public bool Hit(Vector2 p, float pad = 14f) => (p - c).sqrMagnitude <= (r + pad) * (r + pad); }
        public const float StickRadius = 70f;

        // ---- 毎フレームの結果 ----
        public Vector2 stick;                 // x:右 y:上(画面基準)。長さ0〜1
        public bool stickActive; public Vector2 stickOrigin, stickPos;   // 描画用
        public float camDelta;                // 手動の視点回転(度)
        public bool dash, slip, shove;        // このフレームに押された
        public bool kbMove;                   // キーボードで移動中
        public bool usingTouch;               // 直近の操作がタッチ
        public float manualCamT;              // 手動で視点を回してからの残り秒(自動追従を止める)
        // ---- HUDが毎フレーム設定する ----
        public Circle btnDash, btnSlip, btnShove; public float uiScale = 1f; public bool playing; public List<Rect> noDrive = new List<Rect>();

        class Ptr { public int id; public int role; public Vector2 start, last; }   // role: 1=スティック 2=視点 3=ボタン
        readonly List<Ptr> ptrs = new List<Ptr>();
        float W => Screen.width / uiScale;

        Vector2 ToUi(Vector2 screen) => new Vector2(screen.x / uiScale, (Screen.height - screen.y) / uiScale);

        public void Update(float dt)
        {
            dash = slip = shove = false; camDelta = 0f; manualCamT = Mathf.Max(0f, manualCamT - dt);
            // --- キーボード ---
            var kb = new Vector2((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0), (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            // 矢印キーだけでも全操作できる: ↑↓←→で移動、Z/X/Cでスキル、Q/Wではなく ,/. で視点
            if (Input.GetKey(KeyCode.UpArrow)) kb.y += 1f; if (Input.GetKey(KeyCode.DownArrow)) kb.y -= 1f;
            if (Input.GetKey(KeyCode.RightArrow)) kb.x += 1f; if (Input.GetKey(KeyCode.LeftArrow)) kb.x -= 1f;
            kb = Vector2.ClampMagnitude(kb, 1f);
            if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift) || Input.GetKeyDown(KeyCode.Z)) { dash = true; usingTouch = false; }
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.X)) { slip = true; usingTouch = false; }
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.C)) { shove = true; usingTouch = false; }
            float cam = (Input.GetKey(KeyCode.Period) || Input.GetKey(KeyCode.Q) ? 1f : 0f) - (Input.GetKey(KeyCode.Comma) || Input.GetKey(KeyCode.Tab) ? 1f : 0f);
            if (cam != 0f) { camDelta += cam * 120f * dt; manualCamT = 1.2f; }
            kbMove = kb.sqrMagnitude > 0.01f; if (kbMove) usingTouch = false;
            // --- ポインタ(タッチ / マウス) ---
            if (Input.touchCount > 0) { usingTouch = true; foreach (var t in Input.touches) Handle(t.fingerId, ToUi(t.position), t.phase == TouchPhase.Began, t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled, true); }
            else
            {
                Handle(-1, ToUi(Input.mousePosition), Input.GetMouseButtonDown(0), Input.GetMouseButtonUp(0), Input.GetMouseButton(0));
                Handle(-2, ToUi(Input.mousePosition), Input.GetMouseButtonDown(1), Input.GetMouseButtonUp(1), Input.GetMouseButton(1));
            }
            // --- スティック値 ---
            Vector2 ptr = Vector2.zero; stickActive = false;
            foreach (var p in ptrs) if (p.role == 1) { var d = p.last - p.start; var v = new Vector2(d.x, -d.y) / StickRadius; if (v.magnitude < 0.12f) v = Vector2.zero; ptr = Vector2.ClampMagnitude(v, 1f); stickActive = true; stickOrigin = p.start; stickPos = p.start + Vector2.ClampMagnitude(d, StickRadius); }
            stick = kb.sqrMagnitude > 0.01f ? Vector2.ClampMagnitude(kb, 1f) : ptr;
        }

        void Handle(int id, Vector2 pos, bool down, bool up, bool held)
        {
            Ptr p = ptrs.Find(q => q.id == id);
            if (down && p == null)
            {
                p = new Ptr { id = id, start = pos, last = pos }; ptrs.Add(p);
                if (!playing) p.role = 0;
                else if (btnDash.Hit(pos)) { p.role = 3; dash = true; }
                else if (btnSlip.Hit(pos)) { p.role = 3; slip = true; }
                else if (btnShove.Hit(pos)) { p.role = 3; shove = true; }
                else if (noDrive.Exists(r => r.Contains(pos))) p.role = 0;
                else if (id == -2) p.role = 2;                                          // 右クリックのドラッグは視点
                else if (usingTouch && pos.x > W * 0.62f) p.role = 2;                   // 右側のスワイプは視点
                else p.role = 1;                                                        // それ以外はスティック
            }
            if (p == null) return;
            if (held) { if (p.role == 2) { camDelta += (pos.x - p.last.x) * 0.35f; manualCamT = 1.2f; } p.last = pos; }
            if (up || (!held && id < 0)) ptrs.Remove(p);
        }
    }
}
