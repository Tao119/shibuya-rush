using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ShibuyaRush
{
    /// <summary>
    /// スキル発動の演出。スローモーション・時計の一時停止・ヒットストップ・カメラの揺れとズーム・衝撃波・残像・火花・
    /// ポストエフェクト・効果音・画面の閃光とスピード線・大きな文字。すべて実時間(unscaled)で進める。
    /// </summary>
    public class SkillFx
    {
        public enum Kind { None, Dash, Slip, Shove }
        // ---- GameManagerが読む値 ----
        public bool ClockFrozen => clockFreeze > 0f;
        public float FovAdd { get; private set; }
        public Vector3 ShakeOffset { get; private set; }
        public Color Flash { get; private set; }                 // 画面全体の閃光(アルファが強さ)
        public Color VignetteColor { get; private set; }         // 周辺の暗がり・色(アルファが強さ)
        public float SpeedLines { get; private set; }            // 0..1
        public string Banner = ""; public string BannerSub = ""; public float BannerT; public Color BannerColor = Color.white;
        public Kind Active { get; private set; }

        readonly Camera cam; readonly MonoBehaviour host;
        float clockFreeze, hitStop, shake, fovTarget, flashT, flashLife = 0.3f, vigT, vigLife = 0.5f, linesT, trailT, sampleT;
        Color flashCol, vigCol; Kind lastKind;
        // 残像
        struct Pose { public Vector3 pos; public float yaw, phase; }
        readonly List<Pose> poses = new List<Pose>(); readonly HumanoidRenderer[] ghosts = new HumanoidRenderer[3];
        // 衝撃波リング
        class Ring { public Transform t; public Material m; public float age, life, size; public Color col; public bool on; }
        readonly List<Ring> rings = new List<Ring>();
        // 火花
        readonly ParticleSystem sparks;
        // ポストエフェクト
        readonly Bloom bloom; readonly Vignette vig; readonly ChromaticAberration ca; readonly LensDistortion ld; readonly ColorAdjustments colAdj;
        // 効果音
        readonly AudioSource audio; readonly AudioClip cDash, cSlip, cShove, cBump, cReady;
        public bool PostOk { get; private set; }

        public SkillFx(MonoBehaviour host, Camera cam, Material fxAdd, Material[] ghostMats, Material skin)
        {
            this.host = host; this.cam = cam; fovTarget = 0f;
            for (int i = 0; i < 3; i++) ghosts[i] = new HumanoidRenderer(new[] { ghostMats[i] }, ghostMats[i], ghostMats[i]);
            var ringTex = Radial(128, 0.40f, 0.50f, true); var softTex = Radial(64, 0f, 0.5f, false);
            for (int i = 0; i < 5; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad); UnityEngine.Object.Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(host.transform); go.SetActive(false);
                var m = new Material(fxAdd); m.SetTexture("_BaseMap", ringTex); go.GetComponent<MeshRenderer>().sharedMaterial = m;
                go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                rings.Add(new Ring { t = go.transform, m = m });
            }
            var pg = new GameObject("Sparks"); pg.transform.SetParent(host.transform); sparks = pg.AddComponent<ParticleSystem>();
            var main = sparks.main; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 6000; main.gravityModifier = 0.9f;
            main.startLifetime = 0.8f; main.startSize = 0.3f; var em = sparks.emission; em.enabled = false; var sh = sparks.shape; sh.enabled = false;
            var pr = pg.GetComponent<ParticleSystemRenderer>(); var pm = new Material(fxAdd); pm.SetTexture("_BaseMap", softTex); pr.sharedMaterial = pm; pr.renderMode = ParticleSystemRenderMode.Billboard;
            sparks.Play();
            // ポストエフェクト(URPのVolume)。描画されない環境でも、画面オーバーレイで演出は成立する
            try
            {
                cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                var vgo = new GameObject("FxVolume"); vgo.transform.SetParent(host.transform);
                var vol = vgo.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 5;
                var prof = ScriptableObject.CreateInstance<VolumeProfile>();
                bloom = prof.Add<Bloom>(true); bloom.threshold.Override(0.95f); bloom.intensity.Override(0.18f); bloom.scatter.Override(0.6f);
                vig = prof.Add<Vignette>(true); vig.intensity.Override(0.12f); vig.smoothness.Override(0.5f);
                ca = prof.Add<ChromaticAberration>(true); ca.intensity.Override(0f);
                ld = prof.Add<LensDistortion>(true); ld.intensity.Override(0f);
                colAdj = prof.Add<ColorAdjustments>(true); colAdj.saturation.Override(0f); colAdj.postExposure.Override(0f); colAdj.colorFilter.Override(Color.white);
                vol.sharedProfile = prof; PostOk = true;
            }
            catch (Exception e) { Debug.LogWarning("[Fx] post-processing unavailable: " + e.Message); }
            audio = host.gameObject.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.spatialBlend = 0f; audio.volume = 0.7f;
            var rng = new System.Random(5);
            cDash = Clip("dash", 0.55f, (t, d) => { float n = (float)(rng.NextDouble() * 2 - 1); float e = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / d)); return n * e * 0.5f * (1f - t / d * 0.6f) + Mathf.Sin(t * (180f + 900f * t / d) * 6.28f) * 0.18f * e; });
            cSlip = Clip("slip", 1.1f, (t, d) => { float e = Mathf.Exp(-t * 2.2f) * Mathf.Clamp01(t * 30f); float f = 110f * Mathf.Pow(8f, t / d); return (Mathf.Sin(t * f * 6.28f) * 0.35f + Mathf.Sin(t * f * 1.5f * 6.28f) * 0.2f + Mathf.Sin(t * 2400f * 6.28f) * 0.06f * Mathf.Sin(t * 14f)) * e; });
            cShove = Clip("shove", 0.6f, (t, d) => { float e = Mathf.Exp(-t * 7f); float n = (float)(rng.NextDouble() * 2 - 1); return (Mathf.Sin(t * (90f - 50f * t / d) * 6.28f) * 0.8f + n * 0.4f * Mathf.Exp(-t * 18f)) * e; });
            cBump = Clip("bump", 0.18f, (t, d) => { float e = Mathf.Exp(-t * 30f); return Mathf.Sin(t * 140f * 6.28f) * e * 0.7f; });
            cReady = Clip("ready", 0.2f, (t, d) => Mathf.Sin(t * 1320f * 6.28f) * Mathf.Exp(-t * 14f) * 0.25f);
        }

        static AudioClip Clip(string name, float dur, Func<float, float, float> f)
        {
            int rate = 44100, n = (int)(rate * dur); var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)rate, dur), -1f, 1f);
            var c = AudioClip.Create(name, n, 1, rate, false); c.SetData(data, 0); return c;
        }

        /// <summary>中心から外へ薄くなるリング/ソフトな円のテクスチャ。</summary>
        static Texture2D Radial(int n, float r0, float r1, bool ring)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float r = Mathf.Sqrt(((x + .5f) / n - .5f) * ((x + .5f) / n - .5f) + ((y + .5f) / n - .5f) * ((y + .5f) / n - .5f));
                float a = ring ? Mathf.Clamp01(1f - Mathf.Abs(r - (r0 + r1) * 0.5f) / ((r1 - r0) * 0.5f)) : Mathf.Clamp01(1f - r / r1);
                a = a * a; t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            t.Apply(); return t;
        }

        // ================== イベント ==================
        public void OnDash(Vector3 p, Vector2 dir)
        {
            Active = Kind.Dash; lastKind = Kind.Dash; hitStop = 0.07f; clockFreeze = 0.45f; shake = 1.0f; fovTarget = 16f; linesT = 1.2f; trailT = 1.6f;
            SetFlash(new Color(1f, 0.92f, 0.6f, 0.55f), 0.25f); SetVig(new Color(1f, 0.6f, 0.15f, 0.55f), 1.4f);
            SpawnRing(p, new Color(1f, 0.85f, 0.4f, 1f), 7f, 0.55f); SpawnRing(p, new Color(1f, 1f, 1f, 1f), 4f, 0.35f);
            Burst(p + Vector3.up * 0.2f, -new Vector3(dir.x, 0, dir.y), 60, 9f, new Color(1f, 0.8f, 0.3f), 0.35f);
            Banner = "ダッシュ!"; BannerSub = "押しのけて突き進め"; BannerColor = new Color(1f, 0.85f, 0.3f); BannerT = 1.0f;
            audio.PlayOneShot(cDash);
        }

        public void OnSlip(Vector3 p)
        {
            Active = Kind.Slip; lastKind = Kind.Slip; hitStop = 0.12f; clockFreeze = 0.9f; shake = 0.6f; fovTarget = -12f; linesT = 0.9f; trailT = 1.8f;
            SetFlash(new Color(0.6f, 0.9f, 1f, 0.7f), 0.45f); SetVig(new Color(0.1f, 0.5f, 1f, 0.7f), 1.9f);
            SpawnRing(p, new Color(0.4f, 0.85f, 1f, 1f), 10f, 1.0f); SpawnRing(p, new Color(0.8f, 0.6f, 1f, 1f), 6f, 0.8f); SpawnRing(p, new Color(1f, 1f, 1f, 1f), 3f, 0.5f);
            Burst(p + Vector3.up, Vector3.up, 140, 5f, new Color(0.5f, 0.9f, 1f), 0.3f);
            Banner = "すり抜け!!"; BannerSub = "TIME STOP"; BannerColor = new Color(0.5f, 0.9f, 1f); BannerT = 1.4f;
            audio.PlayOneShot(cSlip);
        }

        public void OnShove(Vector3 p, Vector2 dir)
        {
            Active = Kind.Shove; lastKind = Kind.Shove; hitStop = 0.09f; clockFreeze = 0.6f; shake = 1.4f; fovTarget = 9f; linesT = 0.5f;
            SetFlash(new Color(1f, 1f, 1f, 0.5f), 0.25f); SetVig(new Color(1f, 0.25f, 0.2f, 0.6f), 1.0f);
            SpawnRing(p, new Color(1f, 0.5f, 0.3f, 1f), 14f, 0.6f); SpawnRing(p, new Color(1f, 1f, 0.8f, 1f), 9f, 0.45f);
            Burst(p + Vector3.up * 0.5f, new Vector3(dir.x, 0.15f, dir.y), 160, 14f, new Color(1f, 0.55f, 0.25f), 0.4f);
            Banner = "かき分け!!"; BannerSub = "道を開けろ"; BannerColor = new Color(1f, 0.5f, 0.35f); BannerT = 1.1f;
            audio.PlayOneShot(cShove);
        }

        public void OnBump(Vector3 p, Vector2 dir)
        {
            shake = Mathf.Max(shake, 0.45f); SetFlash(new Color(1f, 1f, 1f, 0.12f), 0.1f);
            Burst(p + Vector3.up * 1f, new Vector3(dir.x, 0.3f, dir.y), 8, 4f, new Color(1f, 1f, 1f), 0.18f);
            audio.PlayOneShot(cBump, 0.6f);
        }

        public void PlayReady() { audio.PlayOneShot(cReady); }

        void SetFlash(Color c, float life) { flashCol = c; flashT = life; flashLife = life; }
        void SetVig(Color c, float life) { vigCol = c; vigT = life; vigLife = life; }

        void SpawnRing(Vector3 p, Color c, float size, float life)
        {
            foreach (var r in rings) if (!r.on)
            {
                r.on = true; r.age = 0; r.life = life; r.size = size; r.col = c; r.t.gameObject.SetActive(true);
                r.t.position = p + Vector3.up * 0.15f; r.t.rotation = Quaternion.Euler(90, 0, 0); return;
            }
        }

        void Burst(Vector3 p, Vector3 dir, int n, float speed, Color c, float size)
        {
            for (int i = 0; i < n; i++)
            {
                var v = (dir.normalized * 0.6f + UnityEngine.Random.insideUnitSphere).normalized * speed * UnityEngine.Random.Range(0.3f, 1f);
                var ep = new ParticleSystem.EmitParams { position = p + UnityEngine.Random.insideUnitSphere * 0.3f, velocity = v, startColor = c, startSize = size * UnityEngine.Random.Range(0.6f, 1.5f), startLifetime = UnityEngine.Random.Range(0.45f, 1.0f) };
                sparks.Emit(ep, 1);
            }
        }

        // ================== 毎フレーム ==================
        /// <summary>realDt: 実時間。スローモーションの制御(Time.timeScale)もここで行う。</summary>
        public void Tick(float realDt, Player pl, Vector3 playerWorld, bool playing)
        {
            clockFreeze = Mathf.Max(0, clockFreeze - realDt); flashT = Mathf.Max(0, flashT - realDt); vigT = Mathf.Max(0, vigT - realDt);
            BannerT = Mathf.Max(0, BannerT - realDt); linesT = Mathf.Max(0, linesT - realDt); trailT = Mathf.Max(0, trailT - realDt);
            shake = Mathf.Max(0, shake - realDt * 2.4f);
            if (!playing) { Time.timeScale = 1f; hitStop = 0; }
            else
            {
                float target = 1f;
                if (hitStop > 0f) { target = 0.02f; hitStop -= realDt; }
                else if (pl.Slipping) target = 0.26f;                       // すり抜け中はスローモーション
                Time.timeScale = target < Time.timeScale ? target : Mathf.MoveTowards(Time.timeScale, target, 5f * realDt);
            }
            if (!pl.Slipping && pl.dashT <= 0f && pl.shoveFx <= 0f && clockFreeze <= 0f) Active = Kind.None;
            // カメラ
            float want = pl.dashT > 0 ? 16f : pl.Slipping ? -12f : pl.shoveFx > 0 ? 9f : 0f;
            FovAdd = Mathf.Lerp(FovAdd, want + (shake > 0.7f ? shake * 3f : 0f), 1f - Mathf.Exp(-9f * realDt));
            ShakeOffset = shake > 0.001f ? UnityEngine.Random.insideUnitSphere * shake * 0.22f : Vector3.zero;
            // 閃光・周辺
            Flash = flashT > 0 ? new Color(flashCol.r, flashCol.g, flashCol.b, flashCol.a * (flashT / flashLife) * (flashT / flashLife)) : Color.clear;
            float vigBase = pl.Slipping ? 1f : (vigT > 0 ? vigT / vigLife : 0f);
            Color vc = pl.Slipping ? new Color(0.1f, 0.45f, 1f, 0.75f) : vigCol;
            VignetteColor = new Color(vc.r, vc.g, vc.b, vc.a * Mathf.Clamp01(vigBase));
            SpeedLines = pl.dashT > 0 ? Mathf.Clamp01(0.5f + pl.speedNow / 14f) : Mathf.Clamp01(linesT / 1.0f) * 0.7f;
            // リング
            foreach (var r in rings)
            {
                if (!r.on) continue; r.age += realDt; float k = r.age / r.life;
                if (k >= 1f) { r.on = false; r.t.gameObject.SetActive(false); continue; }
                float s = r.size * (1f - Mathf.Pow(1f - k, 3f)); r.t.localScale = new Vector3(s * 2f, s * 2f, 1f);
                r.m.SetColor("_BaseColor", new Color(r.col.r, r.col.g, r.col.b, (1f - k) * 0.9f));
            }
            // 残像の記録
            sampleT -= realDt;
            if ((pl.Slipping || pl.dashT > 0f || trailT > 0f) && sampleT <= 0f)
            {
                sampleT = 0.04f; poses.Add(new Pose { pos = playerWorld, yaw = pl.yaw, phase = pl.phase });
                if (poses.Count > 12) poses.RemoveAt(0);
            }
            else if (!(pl.Slipping || pl.dashT > 0f || trailT > 0f) && poses.Count > 0 && sampleT <= 0f) { sampleT = 0.04f; poses.RemoveAt(0); }
            for (int i = 0; i < poses.Count; i++) ghosts[i % 3].Add(poses[i].pos, poses[i].yaw, poses[i].phase, 1f, 0, 1.05f);
            foreach (var g in ghosts) g.Flush();
            // ポストエフェクト
            if (PostOk)
            {
                float sl = pl.Slipping ? 1f : 0f, ds = pl.dashT > 0 ? 1f : 0f, pulse = Mathf.Clamp01(flashT / Mathf.Max(0.01f, flashLife));
                float tCa = 0.15f + sl * 0.9f + ds * 0.55f + pulse * 0.6f, tLd = -0.05f - sl * 0.4f - ds * 0.2f - pulse * 0.25f;
                ca.intensity.value = Mathf.Lerp(ca.intensity.value, tCa, 1f - Mathf.Exp(-12f * realDt));
                ld.intensity.value = Mathf.Lerp(ld.intensity.value, tLd, 1f - Mathf.Exp(-12f * realDt));
                vig.intensity.value = Mathf.Lerp(vig.intensity.value, 0.12f + sl * 0.45f + ds * 0.2f, 1f - Mathf.Exp(-10f * realDt));
                bloom.intensity.value = Mathf.Lerp(bloom.intensity.value, 0.18f + sl * 1.1f + ds * 0.5f + pulse * 1.2f, 1f - Mathf.Exp(-12f * realDt));
                colAdj.saturation.value = Mathf.Lerp(colAdj.saturation.value, sl * -55f + ds * 25f, 1f - Mathf.Exp(-10f * realDt));
                colAdj.colorFilter.value = Color.Lerp(colAdj.colorFilter.value, sl > 0 ? new Color(0.75f, 0.9f, 1.15f) : Color.white, 1f - Mathf.Exp(-10f * realDt));
            }
        }
    }
}
