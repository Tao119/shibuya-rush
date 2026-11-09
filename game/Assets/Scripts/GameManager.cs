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
        CityData data; CityBuilder city; PedGraph graph, carGraph; Crowd crowd; Traffic traffic; Player player; float hitCd, hitFlash; int hits; int carCount = 800;
        HumanoidRenderer crowdHr, playerHr; Camera cam, miniCam; RenderTexture miniRt;
        GameState state = GameState.Menu; float stateT, raceT;
        int poiIndex; Vector2 goal; float goalLift; string goalName = ""; GameObject beam;
        float camYaw; int crowdCount = 50000; float hb;
        SkillFx fx; GameObject arrow3d; Texture2D arrowTex, vigTex; Material arrowMat;
        GUIStyle big, mid, small, box;
        // Bot / 計測
        bool autoplay, noSkills, selfTest, fxTest; bool f1, f2, f3; float shotAt; string shotTag; Vector2 startPos; float camBack = 6.5f, camUp = 4.2f, maxLift; string shotDir; float quitAfter = 150f; List<Vector2> botPath; int botIdx; float stuckT;
        readonly List<float> frameMs = new List<float>(); float nextShot; int shotNo; string resultNote = "";

        Material M(string n) => Resources.Load<Material>("Mats/" + n);

        void Awake()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-autoplay" && i + 1 < args.Length) { autoplay = true; poiIndex = int.Parse(args[i + 1]); }
                if (args[i] == "-noskills") noSkills = true;
                if (args[i] == "-selftest") selfTest = true;
                if (args[i] == "-fxtest") fxTest = true;
                if (args[i] == "-cars" && i + 1 < args.Length) carCount = int.Parse(args[i + 1]);
                if (args[i] == "-camback" && i + 1 < args.Length) camBack = float.Parse(args[i + 1]);
                if (args[i] == "-camup" && i + 1 < args.Length) camUp = float.Parse(args[i + 1]);
                if (args[i] == "-start" && i + 2 < args.Length) startPos = new Vector2(float.Parse(args[i + 1]), float.Parse(args[i + 2]));
                if (args[i] == "-shotdir" && i + 1 < args.Length) shotDir = args[i + 1];
                if (args[i] == "-crowd" && i + 1 < args.Length) crowdCount = int.Parse(args[i + 1]);
                if (args[i] == "-quitafter" && i + 1 < args.Length) quitAfter = float.Parse(args[i + 1]);
            }
            QualitySettings.vSyncCount = autoplay ? 0 : 1; Application.targetFrameRate = autoplay ? -1 : 60;
            cam = Camera.main;
        }

        void Start()
        {
            Heightfield.Load();
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
            carGraph = new PedGraph(data, true); carGraph.BuildHubs(Mathf.Min(150, Mathf.Max(2, carGraph.MainCount / 4)), 11, new List<Vector2>());
            var carMats = new Material[6]; for (int i = 0; i < carMats.Length; i++) carMats[i] = M("Car" + i);
            traffic = new Traffic(carGraph, graph, crowd, carCount, carMats, M("CarCabin"));
            player = new Player(city, crowd, graph, Vector2.zero) { yaw = 0 };
            fx = new SkillFx(this, cam, M("FxAdd"), new[] { M("GhostA"), M("GhostB"), M("GhostC") }, M("Skin"));
            arrow3d = new GameObject("GoalArrow"); arrow3d.AddComponent<MeshFilter>().sharedMesh = ArrowMesh();
            arrow3d.AddComponent<MeshRenderer>().sharedMaterial = M("Goal"); arrow3d.SetActive(false);
            arrowTex = MakeArrowTex(); vigTex = MakeVignetteTex();
            beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(beam.GetComponent<Collider>());
            beam.GetComponent<MeshRenderer>().sharedMaterial = M("Goal"); beam.SetActive(false);
            miniRt = new RenderTexture(256, 256, 16); var mg = new GameObject("MiniCam");
            miniCam = mg.AddComponent<Camera>(); miniCam.orthographic = true; miniCam.orthographicSize = 110; miniCam.targetTexture = miniRt;
            miniCam.transform.rotation = Quaternion.Euler(90, 0, 0); miniCam.clearFlags = CameraClearFlags.SolidColor;
            miniCam.backgroundColor = new Color(0.8f, 0.85f, 0.8f); miniCam.farClipPlane = 500; miniCam.depth = -5;
            camYaw = 0; UpdateCamera(1f);
            if (selfTest)
            {   // 車との当たり判定の単体確認(車は原点で+Z向き、長さ4.2m 幅1.8m)
                bool a = Traffic.Overlap(new Vector2(0, 2f), Vector2.zero, 0, out var p1);      // 前方2m: 当たる、前へ押し出し
                bool b = Traffic.Overlap(new Vector2(3, 0), Vector2.zero, 0, out _);            // 横3m: 当たらない
                bool c2 = Traffic.Overlap(new Vector2(0.9f, 0), Vector2.zero, 0, out var p3);    // 横0.9m: 当たる、横へ押し出し
                bool d2 = Traffic.Overlap(new Vector2(0, 2f), Vector2.zero, 180f, out var p4);   // 後ろ向きの車でも前後対称
                Debug.Log($"[SelfTest] frontHit={a} push={p1} (期待 True, y>0) sideFar={b} (期待 False) sideNear={c2} push={p3} (期待 True, x>0) rev={d2} push={p4} (期待 True)");
            }
            Debug.Log($"[Shibuya] roads={data.roads.Length} buildings={city.BuildingCount} nodes={graph.nodes.Count} main={graph.MainCount} edges={graph.edges.Count} hubs={graph.hubNodes.Length} crowd={crowd.Count} post={fx.PostOk}");
            if (autoplay) StartRace(poiIndex); else if (shotDir != null) { Directory.CreateDirectory(shotDir); }
        }

        void StartRace(int idx)
        {
            poiIndex = idx; var poi = data.pois[idx]; goalName = poi.name;
            int gn = graph.NearestNode(new Vector2(poi.x, poi.z)); goal = graph.nodes[gn]; goalLift = graph.nodeLift[gn];
            player.pos = startPos; player.yaw = 0; raceT = 0; player.lift = graph.nodeLift[graph.NearestNode(startPos)]; state = GameState.Countdown; stateT = 0;
            beam.SetActive(true); beam.transform.position = new Vector3(goal.x, Heightfield.Current.Height(goal.x, goal.y) + goalLift + 40, goal.y); beam.transform.localScale = new Vector3(2.4f, 40f, 2.4f);
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
            float udt = Time.unscaledDeltaTime;
            dash = slip = shove = false;
            if (botPath == null || botPath.Count == 0) return Vector2.zero;
            while (botIdx < botPath.Count - 1 && (botPath[botIdx] - player.pos).sqrMagnitude < 2.5f * 2.5f) botIdx++;
            var to = botPath[botIdx] - player.pos;
            if (player.speedNow < 1.2f) stuckT += udt; else stuckT = 0;
            if ((player.slowFactor < 0.6f || player.stagger > 0f) && player.dashCd <= 0) dash = true;
            if (stuckT > 0.6f && player.slipCd <= 0) { slip = true; stuckT = 0; }
            if (stuckT > 0.3f && player.shoveCd <= 0) shove = true;
            if (noSkills) dash = slip = shove = false;
            if (fxTest)
            {   // 演出の確認用: 時刻で3つのスキルを順に発動し、直後にスクリーンショットを撮る
                dash = slip = shove = false;
                if (raceT > 2.5f && !f1) { f1 = true; shove = true; shotAt = Time.unscaledTime + 0.18f; shotTag = "fx_shove"; }
                if (raceT > 5.5f && !f2) { f2 = true; dash = true; shotAt = Time.unscaledTime + 0.35f; shotTag = "fx_dash"; }
                if (raceT > 9.5f && !f3) { f3 = true; slip = true; shotAt = Time.unscaledTime + 0.45f; shotTag = "fx_slip"; }
            }
            return to.normalized;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f), rdt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (state == GameState.Playing || state == GameState.Countdown) frameMs.Add(Time.unscaledDeltaTime * 1000f);
            stateT += dt;
            hb += Time.unscaledDeltaTime; if (autoplay && hb > 5f) { hb = 0; Debug.Log($"[HB] t={Time.timeSinceLevelLoad:F0} wait={crowd.Waiting} state={state} race={raceT:F1} pos={player.pos} fps={1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f):F0}"); }
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
                if (!fx.ClockFrozen) raceT += dt;
                if ((player.pos - goal).magnitude < GoalRadius && Mathf.Abs(player.lift - goalLift) < 2.5f)
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
            crowd.Step(dt, player.pos, !player.Slipping, player.lift);
            traffic.Step(dt);
            player.Tick(rdt, wish, dash, slip, shove);
            var pw = new Vector3(player.pos.x, Heightfield.Current.Height(player.pos.x, player.pos.y) + player.lift, player.pos.y);
            var pdir = new Vector2(Mathf.Sin(player.yaw * Mathf.Deg2Rad), Mathf.Cos(player.yaw * Mathf.Deg2Rad));
            if (player.evDash) { fx.OnDash(pw, pdir); player.evDash = false; }
            if (player.evSlip) { fx.OnSlip(pw); player.evSlip = false; }
            if (player.evShove) { fx.OnShove(pw, pdir); player.evShove = false; }
            if (player.evBump) { fx.OnBump(pw, player.bumpDir); player.evBump = false; }
            fx.Tick(rdt, player, pw, state == GameState.Playing);
            hitCd = Mathf.Max(0, hitCd - rdt); hitFlash = Mathf.Max(0, hitFlash - rdt);
            if (traffic.Collide(player.pos, player.lift, out var push, out bool fast))
            {
                var np = player.pos + push; if (!city.Blocked(np.x, np.y)) player.Shove(push);
                if (fast && hitCd <= 0 && state == GameState.Playing) { hitCd = 2f; hitFlash = 1.5f; hits++; raceT += 3f; player.Knock(push.normalized * 9f); }
            }
            if (autoplay && state == GameState.Playing) camYaw = Mathf.LerpAngle(camYaw, player.yaw, 2f * dt);
            UpdateCamera(rdt);
            UpdateArrow();
            crowd.Render(cam.transform.position, cam.transform.forward); traffic.Render(cam.transform.position);
            float lean = Mathf.Clamp(player.speedNow * 1.6f, 0, 18f);
            playerHr.Add(new Vector3(player.pos.x, Heightfield.Current.Height(player.pos.x, player.pos.y) + player.lift, player.pos.y), player.yaw, player.phase, Mathf.Clamp01(player.speedNow / 3f), 0, 1.05f, lean);
            maxLift = Mathf.Max(maxLift, player.lift);
            crowdHr.Flush(); playerHr.Flush();
            beam.transform.Rotate(0, 60 * dt, 0);

            if (shotTag != null && Time.unscaledTime >= shotAt) { Shot(shotTag); shotTag = null; }
            if (shotDir != null && state != GameState.Menu && !fxTest)
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
            sb.Append($"\"frames\":{n},\"avgMs\":{avg:F2},\"avgFps\":{(avg > 0 ? 1000f / avg : 0):F1},\"p95Ms\":{p95:F2},\"worstMs\":{worst:F2},\"crowd\":{crowd.Count},\"trips\":{crowd.Trips},\"waitingEnd\":{crowd.Waiting},\"cars\":{traffic.Count},\"carsStopped\":{traffic.Stopped},\"carHits\":{hits},\"onDeck\":{crowd.OnDeck},\"maxPlayerLift\":{maxLift:F1},\"bumps\":{player.bumps},\"plowed\":{player.plowed},\"buildings\":{city.BuildingCount}}}");
            if (shotDir != null) File.WriteAllText(Path.Combine(shotDir, "result.json"), sb.ToString());
            Debug.Log("[Perf] " + sb);
        }

        void UpdateCamera(float dt)
        {
            var p = new Vector3(player.pos.x, Heightfield.Current.Height(player.pos.x, player.pos.y) + player.lift, player.pos.y);
            float y = camYaw * Mathf.Deg2Rad; var back = new Vector3(-Mathf.Sin(y), 0, -Mathf.Cos(y));
            var want = p + back * camBack + Vector3.up * camUp;
            cam.transform.position = Vector3.Lerp(cam.transform.position, want, 1f - Mathf.Exp(-14f * dt)) + (fx != null ? fx.ShakeOffset : Vector3.zero);
            cam.fieldOfView = 62f + (fx != null ? fx.FovAdd : 0f);
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
            DrawOverlays(W, H);
            // 時間・速度・目的地
            GUI.Box(new Rect(16, 16, 470, 100), "", box);
            Label(new Rect(28, 18, 450, 40), fx.ClockFrozen ? $"❚❚ {raceT:F2} s   TIME STOP" : $"{raceT:F2} s", mid, fx.ClockFrozen ? new Color(0.5f, 0.9f, 1f) : Color.white);
            Label(new Rect(28, 56, 450, 24), "▶ " + goalName, small, new Color(1, 0.9f, 0.4f));
            float dist = (player.pos - goal).magnitude;
            Label(new Rect(28, 80, 450, 24), $"残り {dist:F0} m   {player.speedNow * 3.6f:F0} km/h   衝突 {player.bumps} 回  吹き飛ばし {player.plowed} 人", small, Color.white);
            if (hitFlash > 0) Label(new Rect(0, H / 2 + 40, W, 60), "車に接触!  +3秒", big, new Color(1, 0.35f, 0.3f));
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
            DrawGoalArrow(W, H, dist);
            DrawBanner(W, H);
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

        // ---------------- 演出・矢印 ----------------
        void DrawOverlays(float W, float H)
        {
            var old = GUI.color; var full = new Rect(0, 0, W, H);
            var vc = fx.VignetteColor; if (vc.a > 0.01f) { GUI.color = vc; GUI.DrawTexture(full, vigTex, ScaleMode.StretchToFill, true); }
            if (fx.SpeedLines > 0.02f)
            {   // 中心から外へ走る線
                var mat = GUI.matrix; var ctr = new Vector2(W / 2, H / 2); float t = Time.unscaledTime;
                for (int i = 0; i < 56; i++)
                {
                    float ang = i * (360f / 56f) + Mathf.Sin(i * 12.9898f + Mathf.Floor(t * 24f)) * 3f;
                    float r0 = H * (0.36f + 0.1f * Mathf.Abs(Mathf.Sin(i * 3.1f + Mathf.Floor(t * 30f)))), len = H * (0.18f + 0.3f * Mathf.Abs(Mathf.Sin(i * 7.7f)));
                    GUI.matrix = mat; GUIUtility.RotateAroundPivot(ang, ctr);
                    GUI.color = new Color(1, 1, 1, 0.55f * fx.SpeedLines * (0.4f + 0.6f * Mathf.Abs(Mathf.Sin(i * 5.3f))));
                    GUI.DrawTexture(new Rect(ctr.x - 1.5f, ctr.y - r0 - len, 3f, len), Texture2D.whiteTexture);
                }
                GUI.matrix = mat;
            }
            var fl = fx.Flash; if (fl.a > 0.01f) { GUI.color = fl; GUI.DrawTexture(full, Texture2D.whiteTexture); }
            GUI.color = old;
        }

        void DrawBanner(float W, float H)
        {
            if (fx.BannerT <= 0f) return;
            float a = Mathf.Clamp01(fx.BannerT / 0.35f), age = 1f - Mathf.Clamp01(fx.BannerT / 1.4f);
            float pop = 1f + 0.9f * Mathf.Exp(-age * 14f);                          // 出現時にドンと大きく
            var mat = GUI.matrix; var piv = new Vector2(W / 2, H * 0.36f);
            GUIUtility.ScaleAroundPivot(new Vector2(pop, pop), piv);
            var c = fx.BannerColor; c.a = a;
            Label(new Rect(0, piv.y - 45, W, 90), fx.Banner, big, c);
            var sub = new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter }; var cs = new Color(1, 1, 1, a);
            Label(new Rect(0, piv.y + 30, W, 40), fx.BannerSub, sub, cs);
            GUI.matrix = mat;
        }

        /// <summary>画面上部に、目的地の方向を示す大きな矢印を常に出す(カメラの向きに対する相対角)。</summary>
        void DrawGoalArrow(float W, float H, float dist)
        {
            var to = goal - player.pos; float bearing = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
            float rel = Mathf.DeltaAngle(camYaw, bearing);
            var ctr = new Vector2(W / 2, 92); var mat = GUI.matrix; var old = GUI.color;
            float pulse = 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 6f);
            GUIUtility.RotateAroundPivot(rel, ctr);
            float sz = 96f * pulse;
            GUI.color = new Color(0, 0, 0, 0.6f); GUI.DrawTexture(new Rect(ctr.x - sz / 2 - 6, ctr.y - sz / 2 - 6, sz + 12, sz + 12), arrowTex);
            GUI.color = new Color(1f, 0.85f, 0.15f, 1f); GUI.DrawTexture(new Rect(ctr.x - sz / 2, ctr.y - sz / 2, sz, sz), arrowTex);
            GUI.matrix = mat; GUI.color = old;
            Label(new Rect(W / 2 - 160, ctr.y + 46, 320, 34), $"{dist:F0} m", mid, new Color(1, 0.9f, 0.3f));
        }

        /// <summary>プレイヤーの前方に浮かぶ3Dの矢印。目的地の方向を指す。</summary>
        void UpdateArrow()
        {
            bool on = state == GameState.Countdown || state == GameState.Playing;
            arrow3d.SetActive(on); if (!on) return;
            var to = goal - player.pos; var dir = to.sqrMagnitude > 1e-4f ? to.normalized : Vector2.up;
            var p = new Vector3(player.pos.x, Heightfield.Current.Height(player.pos.x, player.pos.y) + player.lift, player.pos.y);
            arrow3d.transform.position = p + new Vector3(dir.x, 0, dir.y) * 1.9f + Vector3.up * (2.5f + 0.12f * Mathf.Sin(Time.unscaledTime * 5f));
            arrow3d.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y));
            float k = 1.1f + 0.1f * Mathf.Sin(Time.unscaledTime * 5f); arrow3d.transform.localScale = new Vector3(k, k, k);
        }

        static Mesh ArrowMesh()
        {
            var v = new List<Vector3>(); var t = new List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c) { int s = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(s); t.Add(s + 1); t.Add(s + 2); }
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(a, b, c); Tri(a, c, d); }
            float w = 0.13f, h = 0.09f, L = 0.9f;
            // 軸(+Z方向に伸びる箱)
            Quad(new Vector3(-w, h, 0), new Vector3(-w, h, L), new Vector3(w, h, L), new Vector3(w, h, 0));       // 上
            Quad(new Vector3(-w, -h, 0), new Vector3(w, -h, 0), new Vector3(w, -h, L), new Vector3(-w, -h, L));   // 下
            Quad(new Vector3(-w, -h, 0), new Vector3(-w, -h, L), new Vector3(-w, h, L), new Vector3(-w, h, 0));   // 左
            Quad(new Vector3(w, -h, 0), new Vector3(w, h, 0), new Vector3(w, h, L), new Vector3(w, -h, L));       // 右
            Quad(new Vector3(-w, -h, 0), new Vector3(-w, h, 0), new Vector3(w, h, 0), new Vector3(w, -h, 0));     // 後ろ
            // 頭(ピラミッド)
            float hw = 0.42f, hh = 0.12f, tip = 1.7f;
            var a0 = new Vector3(-hw, -hh, L); var b0 = new Vector3(hw, -hh, L); var c0 = new Vector3(hw, hh, L); var d0 = new Vector3(-hw, hh, L); var ap = new Vector3(0, 0, tip);
            Quad(a0, d0, c0, b0);
            Tri(a0, ap, d0); Tri(d0, ap, c0); Tri(c0, ap, b0); Tri(b0, ap, a0);
            var m = new Mesh { name = "GoalArrow" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }

        static Texture2D MakeArrowTex()
        {
            const int N = 128; var tx = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float u = (x + .5f) / N - .5f, vv = (y + .5f) / N;            // vv: 下0→上1 (画面では上向き)
                bool head = vv > 0.5f && Mathf.Abs(u) < (1f - vv) * 0.9f + 0.0f && vv < 0.97f;
                bool shaft = vv <= 0.55f && vv > 0.08f && Mathf.Abs(u) < 0.12f;
                tx.SetPixel(x, y, head || shaft ? Color.white : Color.clear);
            }
            tx.Apply(); return tx;
        }

        static Texture2D MakeVignetteTex()
        {
            const int N = 128; var tx = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float u = (x + .5f) / N * 2 - 1, vv = (y + .5f) / N * 2 - 1; float r = Mathf.Sqrt(u * u + vv * vv) / 1.2f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 0.45f) / 0.55f));
                tx.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tx.Apply(); return tx;
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
