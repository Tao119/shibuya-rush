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

        public HumanoidRenderer(Material[] tints, Material pants, Material skin)
        {
            this.tints = tints; this.pants = pants; this.skin = skin;
            torso = HumanoidMesh.Box(new Vector3(0.46f, 0.55f, 0.26f), false);
            head = HumanoidMesh.Sphere(0.2f);
            arm = HumanoidMesh.Box(new Vector3(0.12f, 0.6f, 0.12f), true);
            leg = HumanoidMesh.Box(new Vector3(0.17f, 0.85f, 0.17f), true);
            torsoL = new List<Matrix4x4>[tints.Length]; armL = new List<Matrix4x4>[tints.Length];
            for (int i = 0; i < tints.Length; i++) { torsoL[i] = new List<Matrix4x4>(); armL[i] = new List<Matrix4x4>(); }
        }

        /// <summary>1人分を追加。swing=0..1 (歩き/走りの振り幅)。</summary>
        public void Add(Vector3 pos, float yawDeg, float phase, float swing, int tint, float scale, float lean = 0f)
        {
            float s = Mathf.Sin(phase), bob = Mathf.Abs(s) * 0.05f * swing;
            var root = Matrix4x4.TRS(pos + new Vector3(0, bob * scale, 0), Quaternion.Euler(lean, yawDeg, 0), Vector3.one * scale);
            float ang = s * 38f * swing;
            tint %= tints.Length;
            torsoL[tint].Add(root * Matrix4x4.TRS(new Vector3(0, 0.85f, 0), Quaternion.identity, Vector3.one));
            headL.Add(root * Matrix4x4.TRS(new Vector3(0, 1.62f, 0), Quaternion.identity, Vector3.one));
            armL[tint].Add(root * Matrix4x4.TRS(new Vector3(-0.30f, 1.38f, 0), Quaternion.Euler(ang * 1.1f, 0, 4), Vector3.one));
            armL[tint].Add(root * Matrix4x4.TRS(new Vector3(0.30f, 1.38f, 0), Quaternion.Euler(-ang * 1.1f, 0, -4), Vector3.one));
            legL.Add(root * Matrix4x4.TRS(new Vector3(-0.11f, 0.85f, 0), Quaternion.Euler(-ang, 0, 0), Vector3.one));
            legL.Add(root * Matrix4x4.TRS(new Vector3(0.11f, 0.85f, 0), Quaternion.Euler(ang, 0, 0), Vector3.one));
        }

        /// <summary>遠景用: 胴の箱を縦に伸ばした1個だけで人を表す。</summary>
        public void AddFar(Vector3 pos, int tint, float scale)
        {
            torsoL[tint % tints.Length].Add(Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(1.1f, 2.9f, 1.1f) * scale));
        }

        public void Flush()
        {
            for (int i = 0; i < tints.Length; i++) { Draw(torso, tints[i], torsoL[i]); Draw(arm, tints[i], armL[i]); }
            Draw(head, skin, headL); Draw(leg, pants, legL);
        }

        void Draw(Mesh m, Material mat, List<Matrix4x4> list)
        {
            var rp = new RenderParams(mat) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, layer = 0 };
            for (int i = 0; i < list.Count; i += buf.Length)
            {
                int n = Mathf.Min(buf.Length, list.Count - i);
                list.CopyTo(i, buf, 0, n);
                Graphics.RenderMeshInstanced(rp, m, 0, buf, n);
            }
            list.Clear();
        }
    }
}
