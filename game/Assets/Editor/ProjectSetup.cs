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

    /// <summary>窓つきの外壁テクスチャ(壁は白、窓は暗い青。素材色で壁の色が決まる)。</summary>
    static Texture2D MakeFacade()
    {
        const string path = "Assets/Resources/Facade.png"; const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            bool win = x >= 12 && x < 52 && y >= 18 && y < 50;
            bool sill = x >= 10 && x < 54 && y >= 15 && y < 18;
            tex.SetPixel(x, y, win ? new Color(.30f, .38f, .50f) : sill ? new Color(.78f, .78f, .78f) : Color.white);
        }
        File.WriteAllBytes(path, tex.EncodeToPNG()); AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.wrapMode = TextureWrapMode.Repeat; imp.mipmapEnabled = true; imp.filterMode = FilterMode.Bilinear; imp.anisoLevel = 4; imp.sRGBTexture = true;
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
        Mat("Ground", new Color(0.60f, 0.63f, 0.58f)); Mat("Road", new Color(0.27f, 0.28f, 0.31f)); Mat("Walkway", new Color(0.74f, 0.71f, 0.65f));
        Color[] b = { new Color(.88f,.86f,.82f), new Color(.76f,.81f,.88f), new Color(.90f,.79f,.76f), new Color(.78f,.88f,.81f), new Color(.92f,.89f,.74f), new Color(.82f,.77f,.88f) };
        var facade = MakeFacade();
        for (int i = 0; i < b.Length; i++) { var bm = Mat("Bld" + i, b[i], 0.15f); bm.SetTexture("_BaseMap", facade); }
        Color[] t = { new Color(.90f,.25f,.25f), new Color(.2f,.5f,.9f), new Color(.25f,.75f,.4f), new Color(.95f,.8f,.2f), new Color(.7f,.35f,.85f),
                      new Color(.95f,.55f,.7f), new Color(.15f,.75f,.8f), new Color(.92f,.92f,.92f), new Color(.25f,.25f,.3f), new Color(.6f,.4f,.25f) };
        for (int i = 0; i < t.Length; i++) Mat("Tint" + i, t[i]);
        Mat("Pants", new Color(.2f, .22f, .32f)); Mat("PlayerPants", new Color(.08f, .08f, .1f)); Mat("Skin", new Color(.96f, .78f, .64f));
        Mat("PlayerTint", new Color(1f, .42f, .08f)); Mat("Goal", new Color(1f, .85f, .2f), 0.2f, new Color(2.5f, 1.8f, .3f));
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" }; var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.78f, 0.95f); cam.farClipPlane = 700; cam.fieldOfView = 62;
        camGo.AddComponent<AudioListener>(); camGo.AddComponent<UniversalAdditionalCameraData>().antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var sun = new GameObject("Sun"); var l = sun.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.1f; l.color = new Color(1f, .96f, .88f);
        l.shadows = LightShadows.Soft; l.shadowStrength = 0.75f; sun.transform.rotation = Quaternion.Euler(52, -35, 0);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.42f, .47f, .56f);
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = new Color(.62f, .78f, .95f);
        RenderSettings.fogStartDistance = 160; RenderSettings.fogEndDistance = 520;
        new GameObject("GameManager").AddComponent<GameManager>();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };

        PlayerSettings.productName = "Shibuya Rush"; PlayerSettings.companyName = "tao";
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed; PlayerSettings.defaultScreenWidth = 1280; PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "jp.tao.shibuyarush");
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        Debug.Log("SETUP_OK");
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
