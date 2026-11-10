using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShibuyaRush
{
    /// <summary>着せ替え: スキン(服の色)、ぼうし(頭に付ける小物)、トレイル(走った跡)。</summary>
    public class Cosmetics
    {
        readonly Mesh[] hatMeshes; readonly Material[] hatMats = new Material[Meta.Hats.Length]; readonly Material[] hatMats2 = new Material[Meta.Hats.Length];
        readonly Mesh[] hatMeshes2;
        readonly TrailRenderer trail; readonly Material trailMat; int trailKind = -1;
        public Cosmetics(Transform parent, Material fxAdd)
        {
            hatMeshes = new Mesh[Meta.Hats.Length]; hatMeshes2 = new Mesh[Meta.Hats.Length];
            for (int i = 1; i < Meta.Hats.Length; i++)
            {
                BuildHat(i, out hatMeshes[i], out hatMeshes2[i]);
                hatMats[i] = Flat(Meta.Hats[i].color); if (hatMeshes2[i] != null) hatMats2[i] = Flat(Meta.Hats[i].color2);
            }
            var go = new GameObject("PlayerTrail"); go.transform.SetParent(parent);
            trail = go.AddComponent<TrailRenderer>(); trail.time = 0.7f; trail.minVertexDistance = 0.12f; trail.numCapVertices = 4; trail.shadowCastingMode = ShadowCastingMode.Off; trail.emitting = false;
            trailMat = new Material(fxAdd); trail.sharedMaterial = trailMat;
        }

        public static Material Flat(Color c)
        {
            var m = new Material(Resources.Load<Material>("Mats/PlayerTint")); m.SetColor("_BaseColor", c); m.enableInstancing = true; return m;
        }

        // 頭の中心を原点とし、頭の半径は 0.27。パーツを1つのメッシュに束ねる
        struct Part { public Mesh m; public Vector3 p; public Vector3 euler; public Vector3 s; }
        static Mesh Combine(List<Part> parts)
        {
            var ci = new CombineInstance[parts.Count];
            for (int i = 0; i < parts.Count; i++) ci[i] = new CombineInstance { mesh = parts[i].m, transform = Matrix4x4.TRS(parts[i].p, Quaternion.Euler(parts[i].euler), parts[i].s) };
            var r = new Mesh { name = "Hat" }; r.CombineMeshes(ci, true, true); r.RecalculateBounds(); return r;
        }
        static Mesh Ctr(Mesh m) { var v = m.vertices; var c = m.bounds.center; for (int i = 0; i < v.Length; i++) v[i] -= c; m.vertices = v; m.RecalculateBounds(); return m; }

        static void BuildHat(int id, out Mesh a, out Mesh b)
        {
            var parts = new List<Part>(); var parts2 = new List<Part>(); b = null;
            var sph = HumanoidMesh.Sphere(0.5f); var cap = HumanoidMesh.Capsule(0.07f, 0.26f, false, 8, 2);
            Mesh box(float x, float y, float z) => Ctr(HumanoidMesh.Box(new Vector3(x, y, z), false));
            switch (id)
            {
                case 1: // キャップ
                    parts.Add(new Part { m = sph, p = new Vector3(0, 0.13f, 0), euler = Vector3.zero, s = new Vector3(0.62f, 0.34f, 0.62f) });
                    parts.Add(new Part { m = box(0.36f, 0.04f, 0.3f), p = new Vector3(0, 0.14f, 0.3f), euler = new Vector3(-8, 0, 0), s = Vector3.one }); break;
                case 2: // ネコ耳
                    parts.Add(new Part { m = cap, p = new Vector3(-0.17f, 0.2f, 0), euler = new Vector3(0, 0, 22), s = new Vector3(1.1f, 1f, 0.7f) });
                    parts.Add(new Part { m = cap, p = new Vector3(0.17f, 0.2f, 0), euler = new Vector3(0, 0, -22), s = new Vector3(1.1f, 1f, 0.7f) }); break;
                case 3: // わかば
                    parts.Add(new Part { m = HumanoidMesh.Capsule(0.025f, 0.26f, false, 6, 2), p = new Vector3(0, 0.22f, 0), euler = Vector3.zero, s = Vector3.one });
                    parts.Add(new Part { m = sph, p = new Vector3(-0.1f, 0.5f, 0), euler = new Vector3(0, 0, 25), s = new Vector3(0.3f, 0.12f, 0.2f) });
                    parts.Add(new Part { m = sph, p = new Vector3(0.1f, 0.5f, 0), euler = new Vector3(0, 0, -25), s = new Vector3(0.3f, 0.12f, 0.2f) }); break;
                case 4: // シルクハット(帯だけ別色)
                    parts.Add(new Part { m = box(0.3f, 0.34f, 0.3f), p = new Vector3(0, 0.38f, 0), euler = Vector3.zero, s = Vector3.one });
                    parts.Add(new Part { m = box(0.52f, 0.04f, 0.52f), p = new Vector3(0, 0.2f, 0), euler = Vector3.zero, s = Vector3.one });
                    parts2.Add(new Part { m = box(0.32f, 0.07f, 0.32f), p = new Vector3(0, 0.27f, 0), euler = Vector3.zero, s = Vector3.one }); break;
                case 5: // いぬ耳(垂れ耳)
                    parts.Add(new Part { m = cap, p = new Vector3(-0.27f, 0.1f, 0), euler = new Vector3(0, 0, 160), s = new Vector3(1.2f, 1.1f, 0.8f) });
                    parts.Add(new Part { m = cap, p = new Vector3(0.27f, 0.1f, 0), euler = new Vector3(0, 0, -160), s = new Vector3(1.2f, 1.1f, 0.8f) }); break;
                case 6: // ヘイロー
                    for (int i = 0; i < 12; i++) { float an = i * Mathf.PI / 6f; parts.Add(new Part { m = sph, p = new Vector3(Mathf.Cos(an) * 0.3f, 0.6f, Mathf.Sin(an) * 0.3f), euler = Vector3.zero, s = Vector3.one * 0.2f }); } break;
                case 7: // おうかん
                    parts.Add(new Part { m = box(0.5f, 0.1f, 0.5f), p = new Vector3(0, 0.3f, 0), euler = Vector3.zero, s = Vector3.one });
                    for (int i = 0; i < 4; i++) { float an = i * Mathf.PI / 2f + Mathf.PI / 4f; parts.Add(new Part { m = box(0.1f, 0.2f, 0.1f), p = new Vector3(Mathf.Cos(an) * 0.2f, 0.45f, Mathf.Sin(an) * 0.2f), euler = Vector3.zero, s = Vector3.one }); }
                    parts.Add(new Part { m = sph, p = new Vector3(0, 0.55f, 0), euler = Vector3.zero, s = Vector3.one * 0.22f }); break;
            }
            a = Combine(parts); if (parts2.Count > 0) b = Combine(parts2);
        }

        /// <summary>頭の行列(HumanoidRenderer.LastHead)の位置にぼうしを描く。</summary>
        public void DrawHat(int id, Matrix4x4 head)
        {
            if (id <= 0 || id >= hatMeshes.Length) return;
            var rp = new RenderParams(hatMats[id]) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            Graphics.RenderMesh(rp, hatMeshes[id], 0, head);
            if (hatMeshes2[id] != null) { var rp2 = new RenderParams(hatMats2[id]) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false }; Graphics.RenderMesh(rp2, hatMeshes2[id], 0, head); }
        }

        /// <summary>トレイルの位置を更新。kind=0なら出さない。</summary>
        public void UpdateTrail(int kind, Vector3 pos, bool on)
        {
            if (kind != trailKind)
            {
                trailKind = kind; trail.Clear();
                if (kind > 0)
                {
                    var t = Meta.Trails[kind]; var g = new Gradient();
                    if (kind == Meta.Trails.Length - 1)
                        g.SetKeys(new[] { new GradientColorKey(new Color(1f, .3f, .3f), 0f), new GradientColorKey(new Color(1f, .9f, .2f), .25f), new GradientColorKey(new Color(.3f, 1f, .5f), .5f), new GradientColorKey(new Color(.3f, .6f, 1f), .75f), new GradientColorKey(new Color(.8f, .4f, 1f), 1f) },
                                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                    else g.SetKeys(new[] { new GradientColorKey(t.a, 0f), new GradientColorKey(t.b, 1f) }, new[] { new GradientAlphaKey(t.a.a, 0f), new GradientAlphaKey(0f, 1f) });
                    trail.colorGradient = g; trail.widthCurve = new AnimationCurve(new Keyframe(0, 0.55f), new Keyframe(1, 0f));
                }
            }
            trail.emitting = kind > 0 && on; trail.transform.position = pos + Vector3.up * 0.25f;
        }
    }
}
