using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ShibuyaRush
{
    public enum GameState { Menu, Countdown, Playing, Finished }

    /// <summary>ゲーム全体の進行・カメラ・HUD・テスト用Bot。シーンにはこのコンポーネントを持つ空のオブジェクトが1つあれば動く。</summary>
    public class GameManager : MonoBehaviour
    {
        const float GoalRadius = 6f;
        CityData data; CityBuilder city; PedGraph graph; Crowd crowd; Player player;
        HumanoidRenderer crowdHr, playerHr; Camera cam, miniCam; RenderTexture miniRt;
        GameState state = GameState.Menu; float stateT, raceT;
        int poiIndex; Vector2 goal; string goalName = ""; GameObject beam;
        float camYaw; int crowdCount = 30000; float hb;
        GUIStyle big, mid, small, box;
        // Bot / 計測
        bool autoplay; string shotDir; float quitAfter = 150f; List<Vector2> botPath; int botIdx; float stuckT;
        readonly List<float> frameMs = new List<float>(); float nextShot; int shotNo; string resultNote = "";

        Material M(string n) => Resources.Load<Material>("Mats/" + n);

        void Awake()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-autoplay" && i + 1 < args.Length) { autoplay = true; poiIndex = int.Parse(args[i + 1]); }
                if (args[i] == "-shotdir" && i + 1 < args.Length) shotDir = args[i + 1];
                if (args[i] == "-crowd" && i + 1 < args.Length) crowdCount = int.Parse(args[i + 1]);
                if (args[i] == "-quitafter" && i + 1 < args.Length) quitAfter = float.Parse(args[i + 1]);
            }
            QualitySettings.vSyncCount = autoplay ? 0 : 1; Application.targetFrameRate = autoplay ? -1 : 60;
            cam = Camera.main;
        }

        void Start()
        {
            data = CityData.Load();
            city = new CityBuilder();
            var pal = new Material[CityBuilder.PaletteSize];
            for (int i = 0; i < pal.Length; i++) pal[i] = M("Bld" + i);
            city.Build(data, M("Ground"), M("Road"), M("Walkway"), pal);
            graph = new PedGraph(data);
            city.Exempt = graph.OnWalkway;
            var lm = new List<Vector2>(); foreach (var q in data.pois) lm.Add(new Vector2(q.x, q.z));
            lm.Add(Vector2.zero);
            graph.BuildHubs(300, 7, lm);
            var tints = new Material[10]; for (int i = 0; i < tints.Length; i++) tints[i] = M("Tint" + i);
            crowdHr = new HumanoidRenderer(tints, M("Pants"), M("Skin"));
            playerHr = new HumanoidRenderer(new[] { M("PlayerTint") }, M("PlayerPants"), M("Skin"));
            crowd = new Crowd(graph, crowdHr, crowdCount, 12345);
            player = new Player(city, crowd, Vector2.zero) { yaw = 0 };
            beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(beam.GetComponent<Collider>());
            beam.GetComponent<MeshRenderer>().sharedMaterial = M("Goal"); beam.SetActive(false);
            miniRt = new RenderTexture(256, 256, 16); var mg = new GameObject("MiniCam");
            miniCam = mg.AddComponent<Camera>(); miniCam.orthographic = true; miniCam.orthographicSize = 110; miniCam.targetTexture = miniRt;
            miniCam.transform.rotation = Quaternion.Euler(90, 0, 0); miniCam.clearFlags = CameraClearFlags.SolidColor;
            miniCam.backgroundColor = new Color(0.8f, 0.85f, 0.8f); miniCam.farClipPlane = 500; miniCam.depth = -5;
            camYaw = 0; UpdateCamera(1f);
            Debug.Log($"[Shibuya] roads={data.roads.Length} buildings={city.BuildingCount} nodes={graph.nodes.Count} main={graph.MainCount} edges={graph.edges.Count} hubs={graph.hubNodes.Length} crowd={crowd.Count}");
            if (autoplay) StartRace(poiIndex); else if (shotDir != null) { Directory.CreateDirectory(shotDir); }
        }

        void StartRace(int idx)
        {
            poiIndex = idx; var poi = data.pois[idx]; goalName = poi.name;
            goal = graph.nodes[graph.NearestNode(new Vector2(poi.x, poi.z))];
            player.pos = Vector2.zero; player.yaw = 0; raceT = 0; state = GameState.Countdown; stateT = 0;
            beam.SetActive(true); beam.transform.position = new Vector3(goal.x, 40, goal.y); beam.transform.localScale = new Vector3(2.4f, 40f, 2.4f);
            if (autoplay) { botPath = graph.Path(player.pos, goal); botIdx = 0; Debug.Log($"[Bot] route nodes={botPath.Count} dest={goalName}"); if (shotDir != null) Directory.CreateDirectory(shotDir); }
            Cursor.lockState = autoplay ? CursorLockMode.None : CursorLockMode.Locked;
        }

        Vector2 ReadWish(out bool dash, out bool slip, out bool shove)
        {
            dash = Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift);
            slip = Input.GetKeyDown(KeyCode.Space); shove = Input.GetKeyDown(KeyCode.E);
            var inp = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (inp.sqrMagnitude < 0.01f) return Vector2.zero;
            float y = camYaw * Mathf.Deg2Rad; var f = new Vector2(Mathf.Sin(y), Mathf.Cos(y)); var r = new Vector2(f.y, -f.x);
            return f * inp.y + r * inp.x;
        }

        Vector2 BotWish(out bool dash, out bool slip, out bool shove)
        {
            dash = slip = shove = false;
            if (botPath == null || botPath.Count == 0) return Vector2.zero;
            while (botIdx < botPath.Count - 1 && (botPath[botIdx] - player.pos).sqrMagnitude < 2.5f * 2.5f) botIdx++;
            var to = botPath[botIdx] - player.pos;
            if (player.speedNow < 1.2f) stuckT += Time.deltaTime; else stuckT = 0;
            if (player.slowFactor < 0.75f && player.dashCd <= 0) dash = true;
            if (stuckT > 0.6f && player.slipCd <= 0) { slip = true; stuckT = 0; }
            if (stuckT > 0.3f && player.shoveCd <= 0) shove = true;
            return to.normalized;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (state == GameState.Playing || state == GameState.Countdown) frameMs.Add(Time.unscaledDeltaTime * 1000f);
            stateT += dt;
            hb += Time.unscaledDeltaTime; if (autoplay && hb > 5f) { hb = 0; Debug.Log($"[HB] t={Time.timeSinceLevelLoad:F0} state={state} race={raceT:F1} pos={player.pos} fps={1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f):F0}"); }
            bool dash, slip, shove; Vector2 wish = Vector2.zero;
            if (state == GameState.Menu)
            {
                for (int k = 0; k < Mathf.Min(data.pois.Length, 9); k++) if (Input.GetKeyDown(KeyCode.Alpha1 + k)) StartRace(k);
                dash = slip = shove = false;
            }
            else if (state == GameState.Countdown)
            {
                dash = slip = shove = false;
                if (stateT >= 3f) { state = GameState.Playing; stateT = 0; }
            }
            else if (state == GameState.Playing)
            {
                wish = autoplay ? BotWish(out dash, out slip, out shove) : ReadWish(out dash, out slip, out shove);
                raceT += dt;
                if ((player.pos - goal).magnitude < GoalRadius)
                {
                    state = GameState.Finished; stateT = 0; Cursor.lockState = CursorLockMode.None;
                    string key = "best_" + data.pois[poiIndex].id; float best = PlayerPrefs.GetFloat(key, 9999f);
                    if (!autoplay && raceT < best) { PlayerPrefs.SetFloat(key, raceT); resultNote = "ベスト更新!"; }
                    Debug.Log($"[Result] dest={goalName} time={raceT:F1}s");
                    Shot("finish"); WriteResult();
                }
            }
            else
            {
                dash = slip = shove = false;
                if (Input.GetKeyDown(KeyCode.R)) StartRace(poiIndex);
                if (Input.GetKeyDown(KeyCode.M)) { state = GameState.Menu; beam.SetActive(false); player.pos = Vector2.zero; }
            }
            if (state == GameState.Menu || state == GameState.Countdown || state == GameState.Finished) { dash = slip = shove = false; wish = Vector2.zero; }

            if (!autoplay && Cursor.lockState == CursorLockMode.Locked && state != GameState.Finished) camYaw += Input.GetAxis("Mouse X") * 3f;
            if (!autoplay) { camYaw += (Input.GetKey(KeyCode.RightArrow) ? 1 : 0) * 120f * dt - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0) * 120f * dt; }
            if (Input.GetKeyDown(KeyCode.Escape)) Cursor.lockState = CursorLockMode.None;

            // 人混みはメニューでも動かす
            crowd.Step(dt, player.pos, !player.Slipping);
            player.Tick(dt, wish, dash, slip, shove);
            if (autoplay && state == GameState.Playing) camYaw = Mathf.LerpAngle(camYaw, player.yaw, 2f * dt);
            UpdateCamera(dt);
            crowd.Render(cam.transform.position, cam.transform.forward);
            float lean = Mathf.Clamp(player.speedNow * 1.6f, 0, 18f);
            playerHr.Add(new Vector3(player.pos.x, 0, player.pos.y), player.yaw, player.phase, Mathf.Clamp01(player.speedNow / 3f), 0, 1.05f, lean);
            crowdHr.Flush(); playerHr.Flush();
            beam.transform.Rotate(0, 60 * dt, 0);

            if (shotDir != null && state != GameState.Menu)
            {
                nextShot -= dt; if (nextShot <= 0) { nextShot = 9f; Shot("t" + shotNo.ToString("D2")); shotNo++; }
            }
            if (autoplay && (state == GameState.Finished && stateT > 1.5f || Time.timeSinceLevelLoad > quitAfter)) Finish();
        }

        void Shot(string tag)
        {
            if (shotDir == null) return;
            ScreenCapture.CaptureScreenshot(Path.Combine(shotDir, $"{tag}.png"));
        }

        void Finish() { WriteResult(); Application.Quit(); }

        void WriteResult()
        {
            frameMs.Sort(); int n = frameMs.Count;
            float avg = 0; foreach (var f in frameMs) avg += f; avg = n > 0 ? avg / n : 0;
            float p95 = n > 0 ? frameMs[Mathf.Min(n - 1, (int)(n * 0.95f))] : 0, worst = n > 0 ? frameMs[n - 1] : 0;
            var sb = new StringBuilder();
            sb.Append($"{{\"dest\":\"{goalName}\",\"finished\":{(state == GameState.Finished ? "true" : "false")},\"time\":{raceT:F2},");
            sb.Append($"\"frames\":{n},\"avgMs\":{avg:F2},\"avgFps\":{(avg > 0 ? 1000f / avg : 0):F1},\"p95Ms\":{p95:F2},\"worstMs\":{worst:F2},\"crowd\":{crowd.Count},\"trips\":{crowd.Trips},\"buildings\":{city.BuildingCount}}}");
            if (shotDir != null) File.WriteAllText(Path.Combine(shotDir, "result.json"), sb.ToString());
            Debug.Log("[Perf] " + sb);
        }

        void UpdateCamera(float dt)
        {
            var p = new Vector3(player.pos.x, 0, player.pos.y);
            float y = camYaw * Mathf.Deg2Rad; var back = new Vector3(-Mathf.Sin(y), 0, -Mathf.Cos(y));
            var want = p + back * 6.5f + Vector3.up * 4.2f;
            cam.transform.position = Vector3.Lerp(cam.transform.position, want, 1f - Mathf.Exp(-14f * dt));
            cam.transform.rotation = Quaternion.LookRotation((p + Vector3.up * 1.3f + new Vector3(Mathf.Sin(y), 0, Mathf.Cos(y)) * 2f) - cam.transform.position);
            miniCam.transform.position = new Vector3(player.pos.x, 300, player.pos.y);
        }

        // ---------------- HUD ----------------
        void InitStyles()
        {
            if (big != null) return;
            var font = Resources.Load<Font>("Fonts/NotoSansJP");
            GUIStyle S(int sz, FontStyle fs, TextAnchor a) { var s = new GUIStyle(GUI.skin.label) { font = font, fontSize = sz, fontStyle = fs, alignment = a, richText = true }; s.normal.textColor = Color.white; return s; }
            big = S(54, FontStyle.Bold, TextAnchor.MiddleCenter); mid = S(26, FontStyle.Bold, TextAnchor.MiddleLeft); small = S(18, FontStyle.Normal, TextAnchor.MiddleLeft);
            box = new GUIStyle(GUI.skin.box); var t = new Texture2D(1, 1); t.SetPixel(0, 0, new Color(0, 0, 0, 0.55f)); t.Apply(); box.normal.background = t;
        }

        void Label(Rect r, string s, GUIStyle st, Color c)
        {
            var sh = new Rect(r.x + 2, r.y + 2, r.width, r.height); st.normal.textColor = new Color(0, 0, 0, 0.8f); GUI.Label(sh, s, st);
            st.normal.textColor = c; GUI.Label(r, s, st);
        }

        void OnGUI()
        {
            if (data == null) return; InitStyles();
            float sc = Screen.height / 720f; GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1)); float W = Screen.width / sc, H = Screen.height / sc;
            if (state == GameState.Menu)
            {
                GUI.Box(new Rect(0, 0, W, H), "", box);
                Label(new Rect(0, 40, W, 80), "SHIBUYA RUSH", big, Color.white);
                Label(new Rect(0, 110, W, 40), "ハチ公前スタート。大混雑の渋谷を、最速で目的地へ。", small, new Color(1, 1, 1, 0.9f));
                var c = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
                Label(new Rect(0, 150, W, 30), "WASD:移動  マウス/←→:視点  Shift:ダッシュ  Space:すり抜け  E:かき分け", c, new Color(1, 0.95f, 0.6f));
                for (int i = 0; i < Mathf.Min(data.pois.Length, 9); i++)
                {
                    var r = new Rect(W / 2 - 260, 210 + i * 56, 520, 48);
                    float best = PlayerPrefs.GetFloat("best_" + data.pois[i].id, 9999f);
                    string b = best < 9000f ? $"  ベスト {best:F1}秒" : "";
                    var d = new Vector2(data.pois[i].x, data.pois[i].z).magnitude;
                    if (GUI.Button(r, $"{i + 1}.  {data.pois[i].name}   約{d:F0}m{b}")) StartRace(i);
                }
                return;
            }
            // 時間・速度・目的地
            GUI.Box(new Rect(16, 16, 470, 100), "", box);
            Label(new Rect(28, 18, 450, 40), $"{raceT:F2} s", mid, Color.white);
            Label(new Rect(28, 56, 450, 24), "▶ " + goalName, small, new Color(1, 0.9f, 0.4f));
            float dist = (player.pos - goal).magnitude;
            Label(new Rect(28, 80, 450, 24), $"残り {dist:F0} m   {player.speedNow * 3.6f:F0} km/h   人混み抵抗 {(1 - player.slowFactor) * 100:F0}%", small, Color.white);
            // スキル
            Skill(new Rect(16, H - 90, 200, 70), "Shift ダッシュ", player.dashCd, Player.DashCool, player.dashT > 0);
            Skill(new Rect(226, H - 90, 200, 70), "Space すり抜け", player.slipCd, Player.SlipCool, player.Slipping);
            Skill(new Rect(436, H - 90, 200, 70), "E かき分け", player.shoveCd, Player.ShoveCool, player.shoveFx > 0);
            // ミニマップ
            var mr = new Rect(W - 236, 16, 220, 220);
            GUI.DrawTexture(mr, miniRt, ScaleMode.StretchToFill, false);
            GUI.Box(new Rect(mr.center.x - 3, mr.center.y - 3, 6, 6), "", box);
            var rel = (goal - player.pos) / 110f; var gp = mr.center + new Vector2(rel.x, -rel.y) * 110f;
            gp = new Vector2(Mathf.Clamp(gp.x, mr.xMin + 6, mr.xMax - 6), Mathf.Clamp(gp.y, mr.yMin + 6, mr.yMax - 6));
            var old = GUI.color; GUI.color = new Color(1, 0.8f, 0.1f); GUI.DrawTexture(new Rect(gp.x - 6, gp.y - 6, 12, 12), Texture2D.whiteTexture); GUI.color = old;
            // ゴール方向インジケータ
            var sp = cam.WorldToScreenPoint(new Vector3(goal.x, 6, goal.y));
            bool onScreen = sp.z > 0 && sp.x > 0 && sp.x < Screen.width && sp.y > 0 && sp.y < Screen.height;
            if (!onScreen)
            {
                var ctr = new Vector2(Screen.width / 2f, Screen.height / 2f); var v = new Vector2(sp.x, sp.y) - ctr; if (sp.z < 0) v = -v;
                v = v.normalized * Mathf.Min(Screen.width, Screen.height) * 0.42f; var pt = ctr + v;
                Label(new Rect(pt.x / sc - 60, (Screen.height - pt.y) / sc - 20, 120, 40), "◉ " + dist.ToString("F0") + "m", mid, new Color(1, 0.85f, 0.2f));
            }
            else Label(new Rect(sp.x / sc - 250, (Screen.height - sp.y) / sc - 30, 500, 30), goalName + "  " + dist.ToString("F0") + "m", small, new Color(1, 0.9f, 0.3f));
            if (state == GameState.Countdown)
            {
                int n = Mathf.Max(1, 3 - (int)stateT); Label(new Rect(0, H / 2 - 100, W, 120), n.ToString(), big, Color.white);
            }
            if (state == GameState.Finished)
            {
                GUI.Box(new Rect(W / 2 - 280, H / 2 - 120, 560, 240), "", box);
                Label(new Rect(W / 2 - 280, H / 2 - 110, 560, 70), "到着!", big, new Color(1, 0.9f, 0.3f));
                var c = new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter };
                Label(new Rect(W / 2 - 280, H / 2 - 30, 560, 50), $"{goalName}  {raceT:F2} 秒  {resultNote}", c, Color.white);
                var c2 = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
                Label(new Rect(W / 2 - 280, H / 2 + 40, 560, 40), "R:もう一度   M:目的地を選ぶ", c2, Color.white);
            }
        }

        void Skill(Rect r, string name, float cd, float total, bool active)
        {
            GUI.Box(r, "", box);
            var old = GUI.color;
            GUI.color = active ? new Color(1, 0.8f, 0.2f, 0.9f) : (cd <= 0 ? new Color(0.3f, 0.9f, 0.5f, 0.8f) : new Color(0.5f, 0.5f, 0.55f, 0.8f));
            float f = cd <= 0 ? 1f : 1f - cd / total;
            GUI.DrawTexture(new Rect(r.x + 6, r.yMax - 14, (r.width - 12) * f, 8), Texture2D.whiteTexture);
            GUI.color = old;
            Label(new Rect(r.x + 6, r.y + 4, r.width - 8, 40), name, small, Color.white);
            Label(new Rect(r.x + 6, r.y + 30, r.width - 8, 28), active ? "発動中" : (cd <= 0 ? "READY" : $"{cd:F1}s"), small, active ? new Color(1, 0.9f, 0.3f) : Color.white);
        }
    }
}
