using System.Collections.Generic;
using UnityEngine;

namespace ShibuyaRush
{
    /// <summary>
    /// プレイヤー。人は硬い障害物で、何もしなければぶつかって弾かれる。
    /// ダッシュは押しのけて突き進み、すり抜けは貫通し、かき分けは前方を吹き飛ばす。
    /// Tick には実時間のdtを渡す(スローモーション中もプレイヤーは同じ速さで動ける)。
    /// </summary>
    public class Player
    {
        public const float WalkSpeed = 6.0f, DashSpeed = 10.5f, BodyR = 0.72f;
        public const float DashDur = 1.2f, DashCool = 8f, SlipDur = 0.9f, SlipCool = 6f, ShoveCool = 5f;   // ダッシュは強いので待ち時間を長く、持続を短くした
        public Vector2 pos; public float yaw; public float phase;
        public float speedNow, slowFactor = 1f, lift;
        public float dashT, slipT, shoveFx, stagger;                 // 発動中・よろけの残り秒
        public float dashCd, slipCd, shoveCd;                        // クールダウン残り秒
        public bool evDash, evSlip, evShove, evBump, evBump2;                 // 発動・衝突の通知(GameManagerが読んで消す)
        public int bumps, plowed, shoved; public Vector2 bumpDir;
        // アップグレードと一時効果
        public float speedMul = 1f, dashCdMul = 1f, slipCdMul = 1f, shoveCdMul = 1f, shoveRange = 1f, shieldT;
        public int skillUses; public bool allowDrop;   // 自動テスト用: 2階の端から飛び降りてもよい(人が操作するときは落ちない)
        public float DashCd => DashCool * dashCdMul; public float SlipCd => SlipCool * slipCdMul; public float ShoveCd => ShoveCool * shoveCdMul;
        public bool Slipping => slipT > 0f;
        readonly CityBuilder city; readonly Crowd crowd; readonly PedGraph graph; readonly List<int> hits = new List<int>();
        Vector2 vel; float bumpCd;

        public Player(CityBuilder c, Crowd cr, PedGraph g, Vector2 start) { city = c; crowd = cr; graph = g; pos = start; }

        public void Tick(float dt, Vector2 wish, bool dash, bool slip, bool shove)
        {
            dashCd = Mathf.Max(0, dashCd - dt); slipCd = Mathf.Max(0, slipCd - dt); shoveCd = Mathf.Max(0, shoveCd - dt);
            dashT = Mathf.Max(0, dashT - dt); slipT = Mathf.Max(0, slipT - dt); shoveFx = Mathf.Max(0, shoveFx - dt);
            stagger = Mathf.Max(0, stagger - dt); bumpCd = Mathf.Max(0, bumpCd - dt);
            shieldT = Mathf.Max(0, shieldT - dt);
            if (dash && dashCd <= 0) { dashT = DashDur; dashCd = DashCd; evDash = true; skillUses++; }
            if (slip && slipCd <= 0) { slipT = SlipDur; slipCd = SlipCd; evSlip = true; skillUses++; }
            if (shove && shoveCd <= 0) { shoveCd = ShoveCd; shoveFx = 0.35f; DoShove(wish); evShove = true; skillUses++; }

            wish = Vector2.ClampMagnitude(wish, 1f);
            if (stagger > 0f) wish *= 0.3f;
            float target = (dashT > 0 ? DashSpeed : WalkSpeed) * (Slipping ? 1.25f : 1f) * speedMul;
            var want = wish * target;
            vel = Vector2.Lerp(vel, want, 1f - Mathf.Exp(-(stagger > 0f ? 4f : 10f) * dt));
            Move(vel * dt);
            Collide(dt);

            float tgt = graph.ReachableLift(pos.x, pos.y, lift);
            lift = Mathf.MoveTowards(lift, tgt, (tgt > lift ? 8f : 14f) * dt);
            speedNow = vel.magnitude; slowFactor = Mathf.Clamp01(speedNow / Mathf.Max(0.01f, target));
            if (speedNow > 0.2f) yaw = Mathf.LerpAngle(yaw, Mathf.Atan2(vel.x, vel.y) * Mathf.Rad2Deg, 14f * dt);
            phase += speedNow * dt * 2.6f;
        }

        /// <summary>人との衝突。すり抜け中は貫通、ダッシュ中は押しのけ、それ以外は押し戻されて弾かれる。</summary>
        void Collide(float dt)
        {
            if (Slipping) return;
            crowd.Query(pos, BodyR + 0.1f, hits);
            foreach (int i in hits)
            {
                if (Mathf.Abs(crowd.LiftOf(i) - lift) > 2.4f) continue;
                var d = pos - crowd.PosOf(i); float dist = d.magnitude;
                if (dist >= BodyR) continue;
                var n = dist > 1e-3f ? d / dist : new Vector2(-Mathf.Sin(yaw * Mathf.Deg2Rad), -Mathf.Cos(yaw * Mathf.Deg2Rad));
                float overlap = BodyR - dist;
                if (dashT > 0f)
                {   // 突進: 当たった人を弾き飛ばす(自分はほとんど止まらない)
                    var side = new Vector2(-n.y, n.x) * ((i & 1) == 0 ? 1f : -1f);
                    crowd.Launch(i, -n * (7f + speedNow * 0.5f) + side * 2.5f, 3.5f);
                    vel *= 1f - 1.5f * dt; plowed++;
                    continue;
                }
                Move(n * overlap * 0.9f);                                        // 押し戻される
                float closing = Vector2.Dot(vel, -n);
                if (closing > 0f) vel += n * closing * 1.35f;                    // 進行方向の速度を打ち消し、少し跳ね返る
                crowd.Shift(i, -n * overlap * 0.15f);
                if (closing > 2.4f && bumpCd <= 0f && shieldT <= 0f)
                {   // 勢いよくぶつかった: よろける
                    stagger = 0.32f; bumpCd = 0.3f; vel += n * 2.5f; crowd.Push(i, -n * 1.6f);
                    bumps++; evBump = true; bumpDir = -n;
                }
            }
        }

        public void ResetState() { dashCd = slipCd = shoveCd = 0; dashT = slipT = shoveFx = stagger = shieldT = 0; vel = Vector2.zero; speedNow = 0; bumpCd = 0; }
        public void Knock(Vector2 v) { vel = v; stagger = Mathf.Max(stagger, 0.4f); }
        public void Shove(Vector2 d) { pos += d; }

        void Move(Vector2 d) { graph.walkLift = lift; city.QueryLift = lift; try { MoveInner(d); } finally { graph.walkLift = 0f; city.QueryLift = 0f; } }

        void MoveInner(Vector2 d)
        {
            // すでに建物の中(駅構内など、通路から入った場合)にいるなら、建物の中どうしの移動は許す。外から壁を抜けて入ることはできない
            if (city.Blocked(pos.x, pos.y, 0f) || (lift < 1.5f && !graph.InCorridor(pos.x, pos.y, lift))) Rescue();   // 建物の中に入り込んでいたら、いちばん近い外に出す(中を歩き回らせない)
            // 柱は地上(1階)の人だけにぶつかる。2階にいる間は、2階の床の外へは出られない(落ちない)
            bool Free(float x, float z) => !city.Blocked(x, z) && (lift >= 1.5f || graph.InCorridor(x, z, lift)) && !(lift < 2.2f && city.HitPillar(x, z, 0.35f)) && !(lift < 1.0f && graph.Clearance(x, z, lift) < 2.2f) && (lift < 1.5f || allowDrop || graph.ReachableLift(x, z, lift) > lift - 1.0f);
            var n = pos + d;
            if (Free(n.x, n.y)) { pos = n; return; }
            if (Free(pos.x + d.x, pos.y)) pos.x += d.x;
            else if (Free(pos.x, pos.y + d.y)) pos.y += d.y;
            else if (city.Blocked(pos.x, pos.y)) pos = n;   // 壁の余白に入ってしまっている場合は、抜け出すために動けるようにする
            else vel = Vector2.zero;
        }

        /// <summary>建物の壁の内側にいるとき、最も近い空いた場所へ移す。</summary>
        void Rescue()
        {
            for (float r = 0.5f; r <= 8f; r += 0.5f)
                for (int k = 0; k < 16; k++)
                {
                    float an = k * Mathf.PI / 8f; var q = pos + new Vector2(Mathf.Cos(an), Mathf.Sin(an)) * r;
                    if (!city.Blocked(q.x, q.y) && (lift >= 1.5f || graph.InCorridor(q.x, q.y, lift))) { pos = q; vel = Vector2.zero; return; }
                }
        }

        void DoShove(Vector2 wish)
        {
            var fwd = wish.sqrMagnitude > 0.01f ? wish.normalized : new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));
            crowd.Query(pos, 6f * shoveRange, hits);
            foreach (int i in hits)
            {
                if (Mathf.Abs(crowd.LiftOf(i) - lift) > 1.5f) continue;
                var d = crowd.PosOf(i) - pos; float dist = d.magnitude; if (dist < 1e-3f) continue;
                if (Vector2.Dot(d / dist, fwd) < 0.2f) continue;
                shoved++;
                var side = new Vector2(-fwd.y, fwd.x) * Mathf.Sign(Vector2.Dot(d, new Vector2(-fwd.y, fwd.x)) + 0.01f);
                crowd.Launch(i, (d / dist * 4f + side * 9f) * (1.3f - dist / 8f), 6f);
            }
        }
    }
}
