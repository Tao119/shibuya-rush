using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShibuyaRush
{
    /// <summary>人形(胴・頭・両腕・両脚)をGPUインスタンシングで大量描画する。関節は歩行位相から計算。</summary>
    public class HumanoidRenderer
    {
        readonly Mesh torso, head, arm, leg;
        readonly Material[] tints; readonly Material pants, skin;
        readonly List<Matrix4x4>[] torsoL, armL; readonly List<Matrix4x4> legL = new List<Matrix4x4>(), headL = new List<Matrix4x4>();
        readonly Matrix4x4[] buf = new Matrix4x4[1000];
        public int TintCount => tints.Length;
        public Material XRay;   // 設定すると、手前の物に隠れた部分をシルエットで描く
        public Matrix4x4 LastHead;                                   // 直前に追加した人の頭の行列(ぼうし用)

        public HumanoidRenderer(Material[] tints, Material pants, Material skin)
        {
            this.tints = tints; this.pants = pants; this.skin = skin;
            torso = HumanoidMesh.Capsule(0.27f, 0.78f, false);       // 丸いデフォルメ体型(胴・頭・手足がカプセルと球)
            head = HumanoidMesh.Sphere(0.27f);
            arm = HumanoidMesh.Capsule(0.085f, 0.6f, true, 8, 3);
            leg = HumanoidMesh.Capsule(0.115f, 0.78f, true, 8, 3);
            torsoL = new List<Matrix4x4>[tints.Length]; armL = new List<Matrix4x4>[tints.Length];
            for (int i = 0; i < tints.Length; i++) { torsoL[i] = new List<Matrix4x4>(); armL[i] = new List<Matrix4x4>(); }
        }

        /// <summary>1人分を追加。swing=0..1 (歩き/走りの振り幅)。</summary>
        public void Add(Vector3 pos, float yawDeg, float phase, float swing, int tint, float scale, float lean = 0f, float sy = 1f)
        {
            float s = Mathf.Sin(phase), bob = Mathf.Abs(s) * 0.05f * swing;
            var root = Matrix4x4.TRS(pos + new Vector3(0, bob * scale, 0), Quaternion.Euler(lean, yawDeg, 0), new Vector3(1f / Mathf.Sqrt(sy), sy, 1f / Mathf.Sqrt(sy)) * scale);
            float ang = s * 38f * swing;
            tint %= tints.Length;
            torsoL[tint].Add(root * Matrix4x4.TRS(new Vector3(0, 0.72f, 0), Quaternion.identity, Vector3.one));
            LastHead = root * Matrix4x4.TRS(new Vector3(0, 1.70f, 0), Quaternion.identity, Vector3.one); headL.Add(LastHead);
            armL[tint].Add(root * Matrix4x4.TRS(new Vector3(-0.35f, 1.34f, 0), Quaternion.Euler(ang * 1.1f, 0, 4), Vector3.one));
            armL[tint].Add(root * Matrix4x4.TRS(new Vector3(0.35f, 1.34f, 0), Quaternion.Euler(-ang * 1.1f, 0, -4), Vector3.one));
            legL.Add(root * Matrix4x4.TRS(new Vector3(-0.14f, 0.78f, 0), Quaternion.Euler(-ang, 0, 0), Vector3.one));
            legL.Add(root * Matrix4x4.TRS(new Vector3(0.14f, 0.78f, 0), Quaternion.Euler(ang, 0, 0), Vector3.one));
        }

        /// <summary>遠景用: 胴の箱を縦に伸ばした1個だけで人を表す。</summary>
        public void AddFar(Vector3 pos, int tint, float scale)
        {
            torsoL[tint % tints.Length].Add(Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(1.15f, 2.15f, 1.15f) * scale));
        }

        public void Flush()
        {
            if (XRay != null)
            {   // 隠れた部分のシルエット(普通の描画の前に、同じ位置へもう一度描く)
                for (int i = 0; i < tints.Length; i++) { Draw(torso, XRay, torsoL[i], false); Draw(arm, XRay, armL[i], false); }
                Draw(head, XRay, headL, false); Draw(leg, XRay, legL, false);
            }
            for (int i = 0; i < tints.Length; i++) { Draw(torso, tints[i], torsoL[i]); Draw(arm, tints[i], armL[i]); }
            Draw(head, skin, headL); Draw(leg, pants, legL);
        }

        void Draw(Mesh m, Material mat, List<Matrix4x4> list, bool clear = true)
        {
            var rp = new RenderParams(mat) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, layer = 0 };
            for (int i = 0; i < list.Count; i += buf.Length)
            {
                int n = Mathf.Min(buf.Length, list.Count - i);
                list.CopyTo(i, buf, 0, n);
                Graphics.RenderMeshInstanced(rp, m, 0, buf, n);
            }
            if (clear) list.Clear();
        }
    }
}
