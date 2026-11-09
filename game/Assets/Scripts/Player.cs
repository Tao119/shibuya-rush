using System.Collections.Generic;
using UnityEngine;

namespace ShibuyaRush
{
    /// <summary>プレイヤー。移動・建物との衝突・人混みによる減速と、3つのスキルを持つ。</summary>
    public class Player
    {
        public const float WalkSpeed = 6.0f, DashSpeed = 10.5f;
        public Vector2 pos; public float yaw; public float phase;
        public float speedNow, slowFactor = 1f, lift;
        readonly PedGraph graph;
        public float dashT, slipT, shoveFx;                       // 発動中の残り秒
        public float dashCd, slipCd, shoveCd;                     // クールダウン残り秒
        public const float DashDur = 1.4f, DashCool = 4.5f, SlipDur = 0.9f, SlipCool = 7f, ShoveCool = 5f;
        public bool Slipping => slipT > 0f;
        readonly CityBuilder city; readonly Crowd crowd; readonly List<int> hits = new List<int>();
        Vector2 vel;

        public Player(CityBuilder c, Crowd cr, PedGraph g, Vector2 start) { city = c; crowd = cr; graph = g; pos = start; }

        public void Tick(float dt, Vector2 wish, bool dash, bool slip, bool shove)
        {
            dashCd = Mathf.Max(0, dashCd - dt); slipCd = Mathf.Max(0, slipCd - dt); shoveCd = Mathf.Max(0, shoveCd - dt);
            dashT = Mathf.Max(0, dashT - dt); slipT = Mathf.Max(0, slipT - dt); shoveFx = Mathf.Max(0, shoveFx - dt);
            if (dash && dashCd <= 0) { dashT = DashDur; dashCd = DashCool; }
            if (slip && slipCd <= 0) { slipT = SlipDur; slipCd = SlipCool; }
            if (shove && shoveCd <= 0) { shoveCd = ShoveCool; shoveFx = 0.35f; DoShove(wish); }

            wish = Vector2.ClampMagnitude(wish, 1f);
            float target = (dashT > 0 ? DashSpeed : WalkSpeed) * (Slipping ? 1.25f : 1f);

            // 人混みによる減速: 進行方向の前方にいる人の数で決まる
            float slow = 1f; int front = 0, around = 0;
            crowd.Query(pos, 1.3f, hits);
            var fwd = wish.sqrMagnitude > 0.01f ? wish.normalized : new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));
            foreach (int i in hits)
            {
                if (Mathf.Abs(crowd.LiftOf(i) - lift) > 1.5f) continue;
                var d = crowd.PosOf(i) - pos; float dist = d.magnitude;
                if (dist < 1e-3f) continue;
                float dot = Vector2.Dot(d / dist, fwd);
                if (dist < 1.0f && dot > 0.35f) { front++; if (!Slipping) crowd.Shift(i, (d / dist) * 0.02f); }
                if (dist < 0.9f) around++;
            }
            if (!Slipping)
            {
                slow = Mathf.Clamp(1f - 0.2f * front - 0.04f * around, 0.2f, 1f);
                if (dashT > 0) slow = Mathf.Max(slow, 0.65f);          // ダッシュ中は人混みを押し分ける
            }
            slowFactor = Mathf.Lerp(slowFactor, slow, 10f * dt);

            var want = wish * target * slowFactor;
            vel = Vector2.Lerp(vel, want, 1f - Mathf.Exp(-10f * dt));
            Move(vel * dt);
            float tgt = graph.ReachableLift(pos.x, pos.y, lift);
            lift = Mathf.MoveTowards(lift, tgt, (tgt > lift ? 8f : 14f) * dt);
            speedNow = vel.magnitude;
            if (speedNow > 0.2f) yaw = Mathf.LerpAngle(yaw, Mathf.Atan2(vel.x, vel.y) * Mathf.Rad2Deg, 14f * dt);
            phase += speedNow * dt * 2.6f;
        }

        public void Knock(Vector2 v) { vel = v; }
        public void Shove(Vector2 d) { pos += d; }

        void Move(Vector2 d)
        {
            var n = pos + d;
            if (!city.Blocked(n.x, n.y)) { pos = n; return; }
            if (!city.Blocked(pos.x + d.x, pos.y)) pos.x += d.x;
            else if (!city.Blocked(pos.x, pos.y + d.y)) pos.y += d.y;
            else vel = Vector2.zero;
        }

        void DoShove(Vector2 wish)
        {
            var fwd = wish.sqrMagnitude > 0.01f ? wish.normalized : new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));
            crowd.Query(pos, 4.5f, hits);
            foreach (int i in hits)
            {
                if (Mathf.Abs(crowd.LiftOf(i) - lift) > 1.5f) continue;
                var d = crowd.PosOf(i) - pos; float dist = d.magnitude; if (dist < 1e-3f) continue;
                if (Vector2.Dot(d / dist, fwd) < 0.45f) continue;
                var side = new Vector2(-fwd.y, fwd.x) * Mathf.Sign(Vector2.Dot(d, new Vector2(-fwd.y, fwd.x)) + 0.01f);
                crowd.Push(i, (d / dist * 3f + side * 7f) * (1.2f - dist / 6f));
            }
        }
    }
}
