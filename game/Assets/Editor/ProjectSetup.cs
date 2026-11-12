using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ShibuyaRush;

public static class ProjectSetup
{
    const string Mats = "Assets/Resources/Mats";

    static Material Mat(string name, Color c, float smooth = 0.05f, Color? emission = null)
    {
        string path = $"{Mats}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
        m.enableInstancing = true;
        if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        EditorUtility.SetDirty(m); return m;
    }

    /// <summary>透明(または加算)描画にするURPマテリアル設定。</summary>
    static Material Transparent(string name, string shader, Color c, bool additive)
    {
        string path = $"{Mats}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c); m.enableInstancing = true;
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", additive ? 2f : 0f);
        m.SetFloat("_SrcBlend", 5f); m.SetFloat("_DstBlend", additive ? 1f : 10f); m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
        m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 3000;
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetShaderPassEnabled("ShadowCaster", false);
        EditorUtility.SetDirty(m); return m;
    }

    /// <summary>窓つきの外壁テクスチャ(壁は白、窓は暗い青。素材色で壁の色が決まる)。</summary>
    static Texture2D MakeFacade()
    {
        const string path = "Assets/Resources/Facade.png"; const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            bool win = x >= 12 && x < 52 && y >= 18 && y < 50; bool frame = !win && x >= 9 && x < 55 && y >= 15 && y < 53 && (x < 12 || x >= 52 || y < 18 || y >= 50);
            bool sill = x >= 10 && x < 54 && y >= 15 && y < 18;
            tex.SetPixel(x, y, win ? new Color(.46f, .64f, .88f) : frame ? new Color(1f, 1f, 1f) : sill ? new Color(.82f, .82f, .84f) : new Color(1f, 1f, 1f));
        }
        File.WriteAllBytes(path, tex.EncodeToPNG()); AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.wrapMode = TextureWrapMode.Repeat; imp.mipmapEnabled = true; imp.filterMode = FilterMode.Bilinear; imp.anisoLevel = 4; imp.sRGBTexture = true;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>窓だけ光る発光マスク(夕暮れの窓明かり)。</summary>
    static Texture2D MakeFacadeEmission()
    {
        const string path = "Assets/Resources/FacadeEmission.png"; const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            tex.SetPixel(x, y, (x >= 14 && x < 50 && y >= 20 && y < 48) ? new Color(1f, 1f, 1f) : Color.black);
        File.WriteAllBytes(path, tex.EncodeToPNG()); AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.wrapMode = TextureWrapMode.Repeat; imp.mipmapEnabled = true; imp.filterMode = FilterMode.Bilinear; imp.sRGBTexture = true;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void SetupUrp()
    {
        Directory.CreateDirectory("Assets/Settings");
        var rdPath = "Assets/Settings/UrpRenderer.asset"; var rpPath = "Assets/Settings/UrpAsset.asset";
        var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(rpPath);
        if (rp == null)
        {
            var rd = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(rd, rdPath);
            rp = UniversalRenderPipelineAsset.Create(rd); AssetDatabase.CreateAsset(rp, rpPath);
        }
        rp.shadowDistance = 140f; rp.msaaSampleCount = 4; rp.supportsHDR = true;
        EditorUtility.SetDirty(rp);
        GraphicsSettings.defaultRenderPipeline = rp;
        for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = rp; }
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Shibuya/Setup")]
    public static void Setup()
    {
        Directory.CreateDirectory(Mats); Directory.CreateDirectory("Assets/Scenes");
        SetupUrp(); PlayerSettings.colorSpace = ColorSpace.Linear;
        Mat("Ground", new Color(0.40f, 0.64f, 0.36f)); Mat("Road", new Color(0.19f, 0.20f, 0.24f), 0.12f).SetFloat("_Cull", 0f); Mat("Walkway", new Color(0.76f, 0.57f, 0.38f)).SetFloat("_Cull", 0f);
        Mat("Marking", new Color(0.98f, 0.98f, 0.96f), 0.1f).SetFloat("_Cull", 0f);
        Color[] b = { new Color(1f,.86f,.78f), new Color(.78f,.95f,.86f), new Color(.76f,.87f,1f), new Color(.87f,.80f,1f),
                      new Color(1f,.95f,.74f), new Color(1f,.80f,.88f), new Color(.97f,.93f,.86f), new Color(.74f,.88f,.90f) };
        var facade = MakeFacade(); var emis = MakeFacadeEmission();
        for (int i = 0; i < b.Length; i++)
        {
            var bm = Mat("Bld" + i, b[i], 0.2f); bm.SetTexture("_BaseMap", facade);
            bm.EnableKeyword("_EMISSION"); bm.SetTexture("_EmissionMap", emis); bm.SetColor("_EmissionColor", new Color(1.0f, 0.72f, 0.38f) * 0.55f);
            bm.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        Color[] t = { new Color(.90f,.25f,.25f), new Color(.2f,.5f,.9f), new Color(.25f,.75f,.4f), new Color(.95f,.8f,.2f), new Color(.7f,.35f,.85f),
                      new Color(.95f,.55f,.7f), new Color(.15f,.75f,.8f), new Color(.92f,.92f,.92f), new Color(.25f,.25f,.3f), new Color(.6f,.4f,.25f) };
        for (int i = 0; i < t.Length; i++) Mat("Tint" + i, t[i]);
        Color[] cc = { new Color(.92f,.92f,.94f), new Color(.1f,.1f,.12f), new Color(.7f,.1f,.12f), new Color(.15f,.3f,.65f), new Color(.75f,.75f,.78f), new Color(.95f,.8f,.2f) };
        for (int i = 0; i < cc.Length; i++) Mat("Car" + i, cc[i], 0.45f);
        Mat("CarCabin", new Color(.12f, .16f, .22f), 0.7f);
        Transparent("GhostA", "Universal Render Pipeline/Unlit", new Color(0.45f, 0.85f, 1f, 0.55f), false);
        Transparent("GhostB", "Universal Render Pipeline/Unlit", new Color(0.7f, 0.6f, 1f, 0.34f), false);
        Transparent("GhostC", "Universal Render Pipeline/Unlit", new Color(0.9f, 0.7f, 1f, 0.18f), false);
        Transparent("FxAdd", "Universal Render Pipeline/Particles/Unlit", Color.white, true);
        Mat("TreeTrunk", new Color(.45f, .32f, .24f)); Mat("TreeLeaf0", new Color(.40f, .74f, .38f), 0.15f); Mat("TreeLeaf1", new Color(.52f, .80f, .36f), 0.15f); Mat("TreeLeaf2", new Color(.30f, .62f, .42f), 0.15f);
        Mat("Pole", new Color(.62f, .64f, .70f), 0.3f); Mat("LampWarm", new Color(1f, .9f, .6f), 0.2f, new Color(2.4f, 1.8f, .8f));
        Mat("SigGreen", new Color(.2f, 1f, .5f), 0.2f, new Color(.4f, 3f, 1.1f)); Mat("SigRed", new Color(1f, .25f, .2f), 0.2f, new Color(3f, .5f, .4f));
        Mat("Bronze", new Color(.72f, .52f, .26f), 0.45f); Mat("Stone", new Color(.74f, .73f, .72f), 0.15f);
        Color[] neon = { new Color(1f, .25f, .6f), new Color(.2f, .9f, 1f), new Color(1f, .85f, .2f), new Color(.6f, .4f, 1f), new Color(.3f, 1f, .5f), new Color(1f, .5f, .2f), new Color(1f, .3f, .3f), new Color(.95f, .95f, 1f) };
        for (int i = 0; i < neon.Length; i++) Mat("Neon" + i, neon[i] * 0.6f, 0.2f, neon[i] * 2.0f).SetFloat("_Cull", 0f);
        Mat("Pants", new Color(.2f, .22f, .32f)); Mat("PlayerPants", new Color(.08f, .08f, .1f)); Mat("Skin", new Color(.96f, .78f, .64f));
        Mat("PlayerTint", new Color(1f, .42f, .08f)); Mat("Goal", new Color(1f, .85f, .2f), 0.2f, new Color(2.5f, 1.8f, .3f));
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" }; var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox; cam.backgroundColor = new Color(0.62f, 0.78f, 0.95f); cam.farClipPlane = 700; cam.fieldOfView = 62;
        camGo.AddComponent<AudioListener>(); camGo.AddComponent<UniversalAdditionalCameraData>().antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var sun = new GameObject("Sun"); var l = sun.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.45f; l.color = new Color(1f, .82f, .62f);   // 夕方の暖かい光
        l.shadows = LightShadows.Soft; l.shadowStrength = 0.6f; sun.transform.rotation = Quaternion.Euler(30, -48, 0);
        var sky = AssetDatabase.LoadAssetAtPath<Material>($"{Mats}/Sky.mat"); if (sky == null) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, $"{Mats}/Sky.mat"); }
        sky.SetFloat("_SunSize", 0.06f); sky.SetFloat("_SunSizeConvergence", 5f); sky.SetFloat("_AtmosphereThickness", 1.25f); sky.SetColor("_SkyTint", new Color(.55f, .62f, .95f)); sky.SetColor("_GroundColor", new Color(.85f, .75f, .72f)); sky.SetFloat("_Exposure", 1.35f);
        RenderSettings.skybox = sky; RenderSettings.sun = l;
        RenderSettings.ambientMode = AmbientMode.Trilight; RenderSettings.ambientSkyColor = new Color(.58f, .70f, .98f); RenderSettings.ambientEquatorColor = new Color(.95f, .72f, .66f); RenderSettings.ambientGroundColor = new Color(.5f, .42f, .44f);
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = new Color(1f, .83f, .74f);
        RenderSettings.fogStartDistance = 140; RenderSettings.fogEndDistance = 560;
        new GameObject("GameManager").AddComponent<GameManager>();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };

        ApplyIcon();
        PlayerSettings.productName = "Shibuya Rush"; PlayerSettings.companyName = "tao";
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed; PlayerSettings.defaultScreenWidth = 1280; PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "jp.tao.shibuyarush");
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        Debug.Log("SETUP_OK");
    }

    /// <summary>アプリアイコン(Assets/Icons/AppIcon.png)を iOS と PC に設定する。</summary>
    static void ApplyIcon()
    {
        const string path = "Assets/Icons/AppIcon.png";
        if (!System.IO.File.Exists(path)) return;
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti != null) { ti.textureType = TextureImporterType.Default; ti.npotScale = TextureImporterNPOTScale.None; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.alphaSource = TextureImporterAlphaSource.None; ti.isReadable = false; ti.SaveAndReimport(); }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path); if (tex == null) return;
        try
        {
            var ios = PlayerSettings.GetPlatformIcons(NamedBuildTarget.iOS, UnityEditor.iOS.iOSPlatformIconKind.Application);
            foreach (var ic in ios) ic.SetTexture(tex);
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.iOS, UnityEditor.iOS.iOSPlatformIconKind.Application, ios);
        }
        catch (System.Exception e) { Debug.LogWarning("iOS icon: " + e.Message); }
        try { PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { tex }, IconKind.Application); } catch (System.Exception e) { Debug.LogWarning("default icon: " + e.Message); }
    }

    /// <summary>iPhone/iPad用のXcodeプロジェクトを出力する(Build/iOS)。署名とApp Storeへの提出はXcodeで行う。</summary>
    public static void BuildIOS()
    {
        Setup();
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "jp.tao.shibuyarush");
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetArchitecture(NamedBuildTarget.iOS, 1);                      // ARM64
        PlayerSettings.iOS.targetOSVersionString = "15.0"; PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = true; PlayerSettings.allowedAutorotateToLandscapeLeft = true; PlayerSettings.allowedAutorotateToLandscapeRight = true; PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.iOS.hideHomeButton = true;
        bool sim = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-simulator") >= 0;   // iOSシミュレーター用
        PlayerSettings.iOS.sdkVersion = sim ? iOSSdkVersion.SimulatorSDK : iOSSdkVersion.DeviceSDK;
        var opts = new BuildPlayerOptions { scenes = new[] { "Assets/Scenes/Main.unity" }, locationPathName = sim ? "../Build/iOSSim" : "../Build/iOS", target = BuildTarget.iOS, options = BuildOptions.None };
        var rep = BuildPipeline.BuildPlayer(opts);
        Debug.Log("BUILD_RESULT_IOS " + rep.summary.result + " size=" + rep.summary.totalSize);
        if (rep.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    /// <summary>Web版(WebGL)を Build/Web に出力する。広告つきのページ用テンプレートを使う。</summary>
    public static void BuildWeb()
    {
        Setup();
        PlayerSettings.WebGL.template = "PROJECT:Shibuya";
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;   // Noneだと、エンジン内部の例外で止まる
        bool wdev = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-webdev") >= 0;
        if (wdev) PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.FullWithStacktrace;
        PlayerSettings.WebGL.memorySize = 512;
        PlayerSettings.stripEngineCode = true;
        var opts = new BuildPlayerOptions { scenes = new[] { "Assets/Scenes/Main.unity" }, locationPathName = "../Build/Web", target = BuildTarget.WebGL, options = (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-webdev") >= 0 ? BuildOptions.Development : BuildOptions.None) };
        var rep = BuildPipeline.BuildPlayer(opts);
        Debug.Log("BUILD_RESULT_WEB " + rep.summary.result + " size=" + rep.summary.totalSize);
        if (rep.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    public static void BuildMac()
    {
        Setup();
        var opts = new BuildPlayerOptions { scenes = new[] { "Assets/Scenes/Main.unity" }, locationPathName = "../Build/ShibuyaRush.app", target = BuildTarget.StandaloneOSX, options = BuildOptions.None };
        var rep = BuildPipeline.BuildPlayer(opts);
        Debug.Log("BUILD_RESULT " + rep.summary.result + " size=" + rep.summary.totalSize);
        if (rep.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
