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
        bool autoplay, noSkills, selfTest, fxTest, menuShot, finShot2; float menuQuitAt; bool f1, f2, f3; float shotAt; string shotTag; Vector2 startPos; float camBack = 6.5f, camUp = 4.2f, maxLift; string shotDir; float quitAfter = 150f; List<Vector2> botPath; int botIdx; float stuckT;
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
                if (args[i] == "-menushot") menuShot = true;
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
            city.Build(data, M("Ground"), M("Road"), M("Walkway"), pal, M("Marking"));
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
            ringMat = new Material(M("FxAdd")); ringMat.SetTexture("_BaseMap", SkillFx.Radial(128, 0.40f, 0.50f, true));
            GameObject Quad(string n) { var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>()); q.name = n; q.GetComponent<MeshRenderer>().sharedMaterial = new Material(ringMat); q.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; q.SetActive(false); return q; }
            playerRing = Quad("PlayerRing"); goalRing = Quad("GoalRing");
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
            if (selfTest) RunCollisionTests();
            Debug.Log($"[Shibuya] roads={data.roads.Length} buildings={city.BuildingCount} nodes={graph.nodes.Count} main={graph.MainCount} edges={graph.edges.Count} hubs={graph.hubNodes.Length} crowd={crowd.Count} post={fx.PostOk}");
            if (autoplay) StartRace(poiIndex); else if (shotDir != null) { Directory.CreateDirectory(shotDir); }
        }

        /// <summary>壁・人・車にめり込まないことの自動検証。結果は [SelfTest] としてログに出る。</summary>
        void RunCollisionTests()
        {
            // (1) 建物の壁: 外3mから壁に向かって3秒走り続け、建物の内側に入った件数を数える(高架や通路の例外の近くは除く)
            var empty = new Crowd(graph, crowdHr, 0, 1); int tested = 0, penetrated = 0; float worst = 0f;
            foreach (var (mid, outw) in city.WallSamples(400, 7))
            {
                var start = mid + outw * 3f; if (city.Blocked(start.x, start.y, 0.4f) || city.Exempt.Invoke(start.x, start.y) || city.Exempt.Invoke(mid.x, mid.y)) continue;
                var pl = new Player(city, empty, graph, start); tested++;
                for (int f = 0; f < 180; f++) pl.Tick(1f / 60f, -outw, f > 20 && f < 60 && false, false, false);
                if (city.InsideBuilding(pl.pos.x, pl.pos.y)) { penetrated++; worst = Mathf.Max(worst, Vector2.Distance(pl.pos, mid)); }
            }
            Debug.Log($"[SelfTest] building: tested={tested} penetrated={penetrated} (期待 0)");
            // (2) 人: 止めた人に向かって2秒走る。最も近づいた距離が体の半径(0.72m)近くで止まるか
            crowd.Step(0.001f, new Vector2(99999, 99999), true, 0f); int ct = 0, cp = 0; float minAll = 9f;
            var rng = new System.Random(11);
            for (int k = 0; k < 150; k++)
            {
                var q = crowd.PosOf(rng.Next(crowd.Count)); if (city.Blocked(q.x, q.y, 0.4f)) continue;
                float ang = (float)rng.NextDouble() * 6.283f; var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)); var start = q + dir * 3f;
                if (city.Blocked(start.x, start.y, 0.4f)) continue;
                var pl = new Player(city, crowd, graph, start); pl.lift = 0f; float minD = 9f; ct++;
                for (int f = 0; f < 120; f++) { pl.Tick(1f / 60f, -dir, false, false, false); minD = Mathf.Min(minD, Vector2.Distance(pl.pos, q)); }
                minAll = Mathf.Min(minAll, minD); if (minD < 0.4f) cp++;
            }
            Debug.Log($"[SelfTest] crowd: tested={ct} closer_than_0.4m={cp} (期待 ほぼ0) minDistance={minAll:F2}");
        }

        void StartRace(int idx)
        {
            poiIndex = idx; var poi = data.pois[idx]; goalName = poi.name;
            int gn = graph.NearestNode(new Vector2(poi.x, poi.z)); goal = graph.nodes[gn]; goalLift = graph.nodeLift[gn];
            player.pos = startPos; player.yaw = 0; raceT = 0; player.lift = graph.nodeLift[graph.NearestNode(startPos)]; state = GameState.Countdown; stateT = 0;
            beam.SetActive(true); beam.transform.position = new Vector3(goal.x, Heightfield.Current.Height(goal.x, goal.y) + goalLift + 40, goal.y); beam.transform.localScale = new Vector3(2.4f, 40f, 2.4f);
            goalDist = (goal - startPos).magnitude; par3 = 0.19f * goalDist + 5f; par2 = 0.3f * goalDist + 9f;
            combo = 0; comboT = 0; comboBonus = 0; lastHits = player.plowed + player.shoved; lastCount = -1; resultNote = ""; starsEarned = 0; pops.Clear();
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
                int ci = (int)stateT; if (ci != lastCount && ci < 3) { lastCount = ci; fx.PlayReady(); }
                if (stateT >= 3f) { state = GameState.Playing; stateT = 0; fx.PlayGo(); fx.Banner = "GO!"; fx.BannerSub = ""; fx.BannerColor = new Color(.5f, 1f, .6f); fx.BannerT = 0.8f; }
            }
            else if (state == GameState.Playing)
            {
                wish = autoplay ? BotWish(out dash, out slip, out shove) : ReadWish(out dash, out slip, out shove);
                if (!fx.ClockFrozen) raceT += dt;
                if ((player.pos - goal).magnitude < GoalRadius && Mathf.Abs(player.lift - goalLift) < 2.5f)
                {
                    state = GameState.Finished; stateT = 0; Cursor.lockState = CursorLockMode.None;
                    string key = "best_" + data.pois[poiIndex].id; float best = PlayerPrefs.GetFloat(key, 9999f);
                    starsEarned = raceT <= par3 ? 3 : raceT <= par2 ? 2 : 1;
                    if (!autoplay)
                    {
                        if (raceT < best) { PlayerPrefs.SetFloat(key, raceT); resultNote = "NEW BEST!"; }
                        string sk = "stars_" + data.pois[poiIndex].id; if (starsEarned > PlayerPrefs.GetInt(sk, 0)) PlayerPrefs.SetInt(sk, starsEarned);
                    }
                    fx.Confetti(new Vector3(player.pos.x, Heightfield.Current.Height(player.pos.x, player.pos.y) + player.lift, player.pos.y)); fx.PlayWin();
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
            if (player.evBump) { fx.OnBump(pw, player.bumpDir); player.evBump = false; player.evBump2 = true; }
            fx.Tick(rdt, player, pw, state == GameState.Playing);
            // コンボ: 突進・かき分けで人を弾き飛ばすたびに増え、途切れると終わる。25ごとに時計を1秒戻す
            int hitsNow = player.plowed + player.shoved; int dh = hitsNow - lastHits; lastHits = hitsNow;
            if (state == GameState.Playing && dh > 0)
            {
                combo += dh; comboT = 1.6f; comboPop = Time.unscaledTime; fx.PlayPop((int)combo);
                if (combo >= 2 && Time.frameCount % 3 == 0) Pop("+" + dh, pw + Vector3.up * 2.4f, new Color(1f, .95f, .4f), 0.8f);
                while ((int)combo / 25 > comboBonus)
                {
                    comboBonus++; raceT = Mathf.Max(0f, raceT - 1f); Pop("-1.0s", pw + Vector3.up * 3f, new Color(.5f, 1f, .6f), 1.5f);
                    if (fx.BannerT <= 0f) { fx.Banner = new[] { "NICE!", "GREAT!", "AMAZING!", "UNSTOPPABLE!" }[Mathf.Min(comboBonus - 1, 3)]; fx.BannerSub = "COMBO x" + (int)combo + "  時間ボーナス!"; fx.BannerColor = new Color(1f, .9f, .3f); fx.BannerT = 1.0f; }
                }
            }
            if (comboT > 0f) { comboT -= rdt; if (comboT <= 0f) { combo = 0; comboBonus = 0; } }
            foreach (var pp in pops) pp.age += rdt; pops.RemoveAll(pp => pp.age >= pp.life);
            // つぶれ・伸び(バネ)とカメラの傾き
            if (player.evBump2) { sqV -= 2.8f; player.evBump2 = false; }
            sqV += (-sq * 220f - sqV * 16f) * rdt; sq += sqV * rdt;
            float yawRate = Mathf.DeltaAngle(prevYaw, player.yaw) / Mathf.Max(rdt, 1e-3f); prevYaw = player.yaw;
            roll = Mathf.Lerp(roll, Mathf.Clamp(-yawRate * 0.035f, -5f, 5f) * Mathf.Clamp01(player.speedNow / 6f), 1f - Mathf.Exp(-6f * rdt));
            hitCd = Mathf.Max(0, hitCd - rdt); hitFlash = Mathf.Max(0, hitFlash - rdt);
            if (!player.Slipping && traffic.Collide(player.pos, player.lift, out var push, out bool fast))
            {
                var np = player.pos + push; if (!city.Blocked(np.x, np.y)) player.Shove(push);
                if (fast && hitCd <= 0 && state == GameState.Playing) { hitCd = 2f; hitFlash = 1.5f; hits++; raceT += 3f; player.Knock(push.normalized * 9f); }
            }
            if (autoplay && state == GameState.Playing) camYaw = Mathf.LerpAngle(camYaw, player.yaw, 2f * dt);
            UpdateCamera(rdt);
            UpdateArrow(); UpdateRings();
            crowd.Render(cam.transform.position, cam.transform.forward); traffic.Render(cam.transform.position);
            float lean = Mathf.Clamp(player.speedNow * 1.6f, 0, 18f);
            playerHr.Add(new Vector3(player.pos.x, Heightfield.Current.Height(player.pos.x, player.pos.y) + player.lift, player.pos.y), player.yaw, player.phase, Mathf.Clamp01(player.speedNow / 3f), 0, 1.15f, lean, Mathf.Clamp(1f + sq + (player.dashT > 0 ? 0.1f : 0f), 0.6f, 1.4f));
            maxLift = Mathf.Max(maxLift, player.lift);
            crowdHr.Flush(); playerHr.Flush();
            beam.transform.Rotate(0, 60 * dt, 0);

            if (state == GameState.Finished && shotDir != null && !finShot2 && stateT > 2.3f) { finShot2 = true; Shot("finish2"); }
            if (menuShot && state == GameState.Menu && Time.timeSinceLevelLoad > 3f) { Shot("menu"); menuShot = false; menuQuitAt = Time.timeSinceLevelLoad + 1.5f; }
            if (menuQuitAt > 0f && Time.timeSinceLevelLoad > menuQuitAt) Application.Quit();
            if (shotTag != null && Time.unscaledTime >= shotAt) { Shot(shotTag); shotTag = null; }
            if (shotDir != null && state != GameState.Menu && !fxTest)
            {
                nextShot -= dt; if (nextShot <= 0) { nextShot = 9f; Shot("t" + shotNo.ToString("D2")); shotNo++; }
            }
            if (autoplay && (state == GameState.Finished && stateT > 2.9f || Time.timeSinceLevelLoad > quitAfter)) Finish();
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
            cam.fieldOfView = 62f + Mathf.Min(8f, player.speedNow * 0.6f) + (fx != null ? fx.FovAdd : 0f);
            cam.transform.rotation = Quaternion.LookRotation((p + Vector3.up * 1.3f + new Vector3(Mathf.Sin(y), 0, Mathf.Cos(y)) * 2f) - cam.transform.position) * Quaternion.AngleAxis(roll, Vector3.forward);
            miniCam.transform.position = new Vector3(player.pos.x, 300, player.pos.y);
        }

        // ---------------- HUD(ハイパーカジュアル風: 丸い面、太い縁取り文字、鮮やかな色) ----------------
        GUIStyle round, huge; Texture2D circleTex, gradTex; Material ringMat;
        class FloatText { public string s; public Vector3 w; public Color c; public float age, life, size; }
        readonly List<FloatText> pops = new List<FloatText>();
        float combo, comboT, comboPop; int lastHits, comboBonus, lastCount = -1; float par3, par2, goalDist;
        int starsEarned; float roll, prevYaw, sq, sqV; GameObject playerRing, goalRing;
        static readonly Color[] Candy = { new Color(1f, .42f, .5f), new Color(1f, .68f, .25f), new Color(.38f, .8f, .5f), new Color(.35f, .65f, 1f), new Color(.7f, .5f, 1f), new Color(1f, .5f, .78f) };

        void InitStyles()
        {
            if (big != null) return;
            var font = Resources.Load<Font>("Fonts/NotoSansJP");
            GUIStyle S(int sz, FontStyle fs, TextAnchor a) { var st = new GUIStyle(GUI.skin.label) { font = font, fontSize = sz, fontStyle = fs, alignment = a, richText = true, wordWrap = false }; st.normal.textColor = Color.white; return st; }
            big = S(54, FontStyle.Bold, TextAnchor.MiddleCenter); mid = S(26, FontStyle.Bold, TextAnchor.MiddleLeft); small = S(18, FontStyle.Bold, TextAnchor.MiddleLeft);
            huge = S(88, FontStyle.Bold, TextAnchor.MiddleCenter);
            box = new GUIStyle(GUI.skin.box); var t = new Texture2D(1, 1); t.SetPixel(0, 0, new Color(0, 0, 0, 0.55f)); t.Apply(); box.normal.background = t;
            // 角丸の面(9分割)
            const int N = 64, R = 26; var rt = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float cx = Mathf.Clamp(x + .5f, R, N - R), cy = Mathf.Clamp(y + .5f, R, N - R);
                float d = Mathf.Sqrt((x + .5f - cx) * (x + .5f - cx) + (y + .5f - cy) * (y + .5f - cy));
                rt.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(R - d + 0.5f)));
            }
            rt.Apply(); round = new GUIStyle { border = new RectOffset(R, R, R, R) }; round.normal.background = rt;
            circleTex = SkillFx.Radial(128, 0f, 0.5f, false);
            var cs = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) { float r = Mathf.Sqrt(((x + .5f) / 128 - .5f) * ((x + .5f) / 128 - .5f) + ((y + .5f) / 128 - .5f) * ((y + .5f) / 128 - .5f)); cs.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((0.5f - r) * 128f * 0.5f))); }
            cs.Apply(); circleTex = cs;
            gradTex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 64; y++) gradTex.SetPixel(0, y, Color.Lerp(new Color(.42f, .5f, 1f, .93f), new Color(1f, .62f, .45f, .93f), y / 63f));
            gradTex.Apply();
        }

        /// <summary>縁取りつき文字(太い黒縁で読みやすく)。</summary>
        void Label(Rect r, string s, GUIStyle st, Color c)
        {
            var sh = new Color(0.1f, 0.08f, 0.2f, 0.9f); st.normal.textColor = sh;
            float k = Mathf.Clamp(st.fontSize / 26f, 1f, 3.2f);   // 大きい文字ほど縁取りを太く
            foreach (var o in new[] { new Vector2(-2, 0), new Vector2(2, 0), new Vector2(0, -2), new Vector2(0, 2), new Vector2(0, 4), new Vector2(-1.5f, 1.5f), new Vector2(1.5f, 1.5f), new Vector2(-1.5f, -1.5f), new Vector2(1.5f, -1.5f) })
                GUI.Label(new Rect(r.x + o.x * k, r.y + o.y * k, r.width, r.height), s, st);
            st.normal.textColor = c; GUI.Label(r, s, st);
        }

        /// <summary>角丸の面。下に濃い影を重ねて立体的なボタン風にする。</summary>
        void Panel(Rect r, Color c, float depth = 5f)
        {
            var old = GUI.color; GUI.color = new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.55f, c.a); GUI.Box(new Rect(r.x, r.y + depth, r.width, r.height), GUIContent.none, round);
            GUI.color = c; GUI.Box(r, GUIContent.none, round); GUI.color = old;
        }

        void Disc(Rect r, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, circleTex); GUI.color = old; }

        void Pop(string text, Vector3 world, Color c, float size = 1f) { pops.Add(new FloatText { s = text, w = world, c = c, life = 1.0f, size = size }); if (pops.Count > 24) pops.RemoveAt(0); }

        void OnGUI()
        {
            if (data == null) return; InitStyles();
            float sc = Screen.height / 720f; GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1)); float W = Screen.width / sc, H = Screen.height / sc;
            if (state == GameState.Menu) { DrawMenu(W, H); return; }
            DrawOverlays(W, H);
            DrawTopLeft(W, H);
            DrawGoalArrow(W, H, (player.pos - goal).magnitude);
            DrawMinimap(W, H);
            DrawCombo(W, H);
            DrawSkills(W, H);
            DrawPops(W, H, sc);
            if (hitFlash > 0) Label(new Rect(0, H / 2 + 50, W, 60), "車に接触!  +3秒", big, new Color(1, 0.35f, 0.3f));
            DrawBanner(W, H);
            if (state == GameState.Countdown) DrawCountdown(W, H);
            if (state == GameState.Finished) DrawFinish(W, H);
        }

        void DrawMenu(float W, float H)
        {
            GUI.DrawTexture(new Rect(0, 0, W, H), gradTex, ScaleMode.StretchToFill, true);
            float t = Time.unscaledTime; string title = "SHIBUYA RUSH";
            var st = new GUIStyle(huge) { fontSize = 78 }; float total = 0f; var wd = new float[title.Length];
            for (int i = 0; i < title.Length; i++) { wd[i] = title[i] == ' ' ? 30f : st.CalcSize(new GUIContent(title[i].ToString())).x + 4f; total += wd[i]; }
            float xc = W / 2 - total / 2;
            for (int i = 0; i < title.Length; i++)
            {
                if (title[i] != ' ') { float dy = Mathf.Sin(t * 3f + i * 0.55f) * 7f; Label(new Rect(xc, 28 + dy, wd[i], 100), title[i].ToString(), st, Color.Lerp(Color.white, Candy[i % Candy.Length], 0.55f)); }
                xc += wd[i];
            }
            Label(new Rect(0, 128, W, 36), "ハチ公前スタート。大混雑の渋谷を、最速で目的地へ!", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 20 }, Color.white);
            for (int i = 0; i < Mathf.Min(data.pois.Length, 6); i++)
            {
                var r = new Rect(W / 2 - 310, 180 + i * 76, 620, 66);
                bool hover = r.Contains(Event.current.mousePosition);
                float k = hover ? 1.03f : 1f; var rr = new Rect(r.center.x - r.width * k / 2, r.center.y - r.height * k / 2, r.width * k, r.height * k);
                var col = Candy[i % Candy.Length]; Panel(rr, hover ? Color.Lerp(col, Color.white, 0.25f) : col, 6f);
                Disc(new Rect(rr.x + 12, rr.y + 9, 48, 48), new Color(1, 1, 1, 0.95f));
                Label(new Rect(rr.x + 12, rr.y + 9, 48, 48), (i + 1).ToString(), new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter }, col);
                string nm = data.pois[i].name; var d = new Vector2(data.pois[i].x, data.pois[i].z).magnitude;
                Label(new Rect(rr.x + 76, rr.y + 6, 400, 34), nm, new GUIStyle(mid) { fontSize = 22 }, Color.white);
                Label(new Rect(rr.x + 76, rr.y + 36, 300, 24), $"約{d:F0}m", small, new Color(1, 1, 1, 0.9f));
                int stars = PlayerPrefs.GetInt("stars_" + data.pois[i].id, 0); float best = PlayerPrefs.GetFloat("best_" + data.pois[i].id, 9999f);
                string ss = ""; for (int k2 = 0; k2 < 3; k2++) ss += k2 < stars ? "★" : "☆";
                Label(new Rect(rr.xMax - 190, rr.y + 4, 180, 36), ss, new GUIStyle(mid) { alignment = TextAnchor.MiddleRight, fontSize = 30 }, stars > 0 ? new Color(1f, .93f, .35f) : new Color(1, 1, 1, 0.6f));
                if (best < 9000f) Label(new Rect(rr.xMax - 190, rr.y + 38, 180, 24), $"BEST {best:F1}s", new GUIStyle(small) { alignment = TextAnchor.MiddleRight }, Color.white);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) StartRace(i);
            }
            Label(new Rect(0, H - 52, W, 30), "WASD 移動   Shift ダッシュ   Space すり抜け   E かき分け   (1〜6キーでも選べます)", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter }, new Color(1, 1, 1, 0.95f));
        }

        void DrawTopLeft(float W, float H)
        {
            bool frozen = fx.ClockFrozen;
            Panel(new Rect(16, 14, 250, 76), frozen ? new Color(.2f, .55f, .95f, .92f) : new Color(.18f, .17f, .36f, .8f), 4f);
            Label(new Rect(26, 16, 232, 70), frozen ? $"❚❚ {raceT:F2}" : $"{raceT:F2}", new GUIStyle(huge) { fontSize = 50, alignment = TextAnchor.MiddleLeft }, Color.white);
            Label(new Rect(196, 38, 60, 40), "秒", small, new Color(1, 1, 1, 0.85f));
            Panel(new Rect(16, 98, 300, 34), new Color(1f, .78f, .2f, .92f), 3f);
            Label(new Rect(28, 99, 284, 30), "▶ " + goalName, new GUIStyle(small) { fontSize = 16 }, new Color(.25f, .15f, .05f));
        }

        void DrawMinimap(float W, float H)
        {
            var mr = new Rect(W - 236, 16, 220, 220);
            Panel(new Rect(mr.x - 5, mr.y - 5, mr.width + 10, mr.height + 10), new Color(1, 1, 1, 0.9f), 4f);
            GUI.DrawTexture(mr, miniRt, ScaleMode.StretchToFill, false);
            Disc(new Rect(mr.center.x - 6, mr.center.y - 6, 12, 12), new Color(1f, .45f, .1f));
            var rel = (goal - player.pos) / 110f; var gp = mr.center + new Vector2(rel.x, -rel.y) * 110f;
            gp = new Vector2(Mathf.Clamp(gp.x, mr.xMin + 8, mr.xMax - 8), Mathf.Clamp(gp.y, mr.yMin + 8, mr.yMax - 8));
            float pu = 1f + 0.2f * Mathf.Sin(Time.unscaledTime * 6f); Disc(new Rect(gp.x - 8 * pu, gp.y - 8 * pu, 16 * pu, 16 * pu), new Color(1, .82f, .1f));
        }

        void DrawCombo(float W, float H)
        {
            if (combo < 2) return;
            float k = 1f + 0.55f * Mathf.Exp(-(Time.unscaledTime - comboPop) * 9f);
            float lv = Mathf.Clamp01(combo / 60f); var col = Color.Lerp(Color.Lerp(Color.white, new Color(1f, .9f, .2f), Mathf.Clamp01(lv * 2)), new Color(1f, .35f, .55f), Mathf.Clamp01(lv * 2 - 1));
            var mat = GUI.matrix; var piv = new Vector2(W - 120, 330); GUIUtility.ScaleAroundPivot(new Vector2(k, k), piv);
            Label(new Rect(piv.x - 130, piv.y - 70, 260, 34), "COMBO", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 22 }, new Color(1, 1, 1, 0.95f));
            Label(new Rect(piv.x - 130, piv.y - 40, 260, 90), "x" + (int)combo, new GUIStyle(huge) { fontSize = 84 }, col);
            var old = GUI.color; GUI.color = new Color(1, 1, 1, 0.9f); GUI.DrawTexture(new Rect(piv.x - 90, piv.y + 56, 180 * Mathf.Clamp01(comboT / 1.6f), 8), Texture2D.whiteTexture); GUI.color = old;
            GUI.matrix = mat;
        }

        void DrawSkills(float W, float H)
        {
            Skill(new Vector2(W / 2 - 130, H - 84), "SHIFT", "ダッシュ", player.dashCd, Player.DashCool, player.dashT > 0, new Color(1f, .72f, .2f));
            Skill(new Vector2(W / 2, H - 84), "SPACE", "すり抜け", player.slipCd, Player.SlipCool, player.Slipping, new Color(.4f, .85f, 1f));
            Skill(new Vector2(W / 2 + 130, H - 84), "E", "かき分け", player.shoveCd, Player.ShoveCool, player.shoveFx > 0, new Color(1f, .45f, .5f));
        }

        void Skill(Vector2 c, string key, string name, float cd, float total, bool active, Color col)
        {
            const float R = 40f; bool ready = cd <= 0f; float f = ready || active ? 1f : 1f - cd / total;
            var rect = new Rect(c.x - R, c.y - R, R * 2, R * 2);
            float pulse = ready ? 1f + 0.05f * Mathf.Sin(Time.unscaledTime * 7f) : 1f; if (active) pulse = 1.2f;
            var rr = new Rect(c.x - R * pulse - 5, c.y - R * pulse - 5, (R * pulse + 5) * 2, (R * pulse + 5) * 2);
            Disc(rr, ready || active ? new Color(1, 1, 1, 0.95f) : new Color(1, 1, 1, 0.55f));
            var inner = new Rect(c.x - R * pulse, c.y - R * pulse, R * 2 * pulse, R * 2 * pulse);
            Disc(inner, new Color(.16f, .14f, .3f, 0.95f));
            GUI.BeginGroup(new Rect(inner.x, inner.y + inner.height * (1f - f), inner.width, inner.height * f));
            Disc(new Rect(0, -inner.height * (1f - f), inner.width, inner.height), active ? Color.white : col);
            GUI.EndGroup();
            Label(rect, ready || active ? key : $"{cd:F1}", new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter, fontSize = ready ? 17 : 24 }, Color.white);
            Label(new Rect(c.x - 70, c.y + R + 2, 140, 24), name, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 15 }, new Color(1, 1, 1, 0.95f));
        }

        void DrawPops(float W, float H, float sc)
        {
            foreach (var p in pops)
            {
                var sp = cam.WorldToScreenPoint(p.w + Vector3.up * (p.age * 2.2f)); if (sp.z < 0) continue;
                float k = (1f + 0.8f * Mathf.Exp(-p.age * 12f)) * p.size; float a = Mathf.Clamp01((p.life - p.age) / 0.35f);
                var mat = GUI.matrix; var pv = new Vector2(sp.x / sc, (Screen.height - sp.y) / sc); GUIUtility.ScaleAroundPivot(new Vector2(k, k), pv);
                var c = p.c; c.a = a; Label(new Rect(pv.x - 100, pv.y - 20, 200, 40), p.s, new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter, fontSize = 28 }, c);
                GUI.matrix = mat;
            }
        }

        void DrawCountdown(float W, float H)
        {
            int n = Mathf.Max(1, 3 - (int)stateT); float frac = stateT - Mathf.Floor(stateT);
            float k = 1f + 1.2f * Mathf.Exp(-frac * 9f); var mat = GUI.matrix; var piv = new Vector2(W / 2, H / 2 - 40); GUIUtility.ScaleAroundPivot(new Vector2(k, k), piv);
            Label(new Rect(0, piv.y - 70, W, 140), n.ToString(), new GUIStyle(huge) { fontSize = 130 }, Candy[(3 - n) % Candy.Length]);
            GUI.matrix = mat;
        }

        void DrawFinish(float W, float H)
        {
            var p = new Rect(W / 2 - 300, H / 2 - 210, 600, 400); float t = stateT;
            Panel(p, new Color(.2f, .18f, .42f, .96f), 8f);
            float k = 1f + 0.5f * Mathf.Exp(-t * 8f); var mat = GUI.matrix; GUIUtility.ScaleAroundPivot(new Vector2(k, k), new Vector2(W / 2, p.y + 60));
            Label(new Rect(p.x, p.y + 14, p.width, 90), "到着!", new GUIStyle(huge) { fontSize = 70 }, new Color(1, .9f, .3f)); GUI.matrix = mat;
            Label(new Rect(p.x, p.y + 100, p.width, 36), goalName, new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter }, Color.white);
            float shown = Mathf.Min(raceT, raceT * Mathf.Clamp01(t / 0.8f));
            Label(new Rect(p.x, p.y + 132, p.width, 90), $"{shown:F2} 秒", new GUIStyle(huge) { fontSize = 66 }, Color.white);
            for (int i = 0; i < 3; i++)
            {
                float st = 0.9f + i * 0.4f; bool on = i < starsEarned && t > st; float ks = on ? 1f + 1.0f * Mathf.Exp(-(t - st) * 9f) : 1f;
                var mat2 = GUI.matrix; var pv = new Vector2(W / 2 + (i - 1) * 96, p.y + 270); GUIUtility.ScaleAroundPivot(new Vector2(ks, ks), pv);
                Label(new Rect(pv.x - 50, pv.y - 45, 100, 90), on ? "★" : "☆", new GUIStyle(huge) { fontSize = 84 }, on ? new Color(1f, .88f, .2f) : new Color(1, 1, 1, 0.4f));
                GUI.matrix = mat2;
            }
            if (resultNote != "") Label(new Rect(p.x, p.y + 318, p.width, 30), resultNote, new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter }, new Color(.5f, 1f, .6f));
            Label(new Rect(p.x, p.y + 350, p.width, 30), "R もう一度     M 目的地を選ぶ", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter }, new Color(1, 1, 1, 0.9f));
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
            Label(new Rect(W / 2 - 160, ctr.y + 50, 320, 34), $"{dist:F0} m", new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter }, new Color(1, 0.92f, 0.35f));
        }

        /// <summary>プレイヤーの前方に浮かぶ3Dの矢印。目的地の方向を指す。</summary>
        void UpdateRings()
        {
            bool on = state == GameState.Countdown || state == GameState.Playing;
            playerRing.SetActive(on); goalRing.SetActive(on);
            if (!on) return;
            float t = Time.unscaledTime; var hf = Heightfield.Current;
            playerRing.transform.position = new Vector3(player.pos.x, hf.Height(player.pos.x, player.pos.y) + player.lift + 0.12f, player.pos.y);
            playerRing.transform.rotation = Quaternion.Euler(90, 0, 0); float ps = 2.4f * (1f + 0.07f * Mathf.Sin(t * 8f)); playerRing.transform.localScale = new Vector3(ps, ps, 1);
            playerRing.GetComponent<MeshRenderer>().material.SetColor("_BaseColor", new Color(1f, .75f, .15f, 0.95f));
            goalRing.transform.position = new Vector3(goal.x, hf.Height(goal.x, goal.y) + goalLift + 0.15f, goal.y);
            goalRing.transform.rotation = Quaternion.Euler(90, 0, 0); float gs = GoalRadius * 2f * (1f + 0.06f * Mathf.Sin(t * 4f)); goalRing.transform.localScale = new Vector3(gs, gs, 1);
            goalRing.GetComponent<MeshRenderer>().material.SetColor("_BaseColor", new Color(1f, .85f, .2f, 0.9f));
        }

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
    }
}
