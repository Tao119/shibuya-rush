using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ShibuyaRush
{
    public enum GameState { Menu, Countdown, Playing, Paused, Finished, Failed }

    /// <summary>ゲーム全体の進行・カメラ・HUD・テスト用Bot。シーンにはこのコンポーネントを持つ空のオブジェクトが1つあれば動く。</summary>
    public partial class GameManager : MonoBehaviour
    {
        const float GoalRadius = 6f;
        CityData data; CityBuilder city; PedGraph graph, carGraph; Crowd crowd; Traffic traffic; Props props; Player player; float hitCd, hitFlash; int hits; int carCount = 800;
        HumanoidRenderer crowdHr, playerHr; Camera cam, miniCam; RenderTexture miniRt;
        GameState state = GameState.Menu; float stateT, raceT;
        int poiIndex; Vector2 goal; float goalLift; string goalName = ""; GameObject beam;
        float camYaw; int crowdCount = 22000; float hb;
        SkillFx fx; GameObject arrow3d; Texture2D arrowTex, vigTex; Material arrowMat;
        readonly InputHub input = new InputHub(); bool touchMode, crowdArg, carsArg;
        // Bot / 計測
        bool autoplay, noSkills, selfTest, resetSave, noPersist, demoWin, demoFail; string panelArg = ""; int levelArg; bool fxTest, menuShot, finShot2, fakeJoy; float menuQuitAt; bool f1, f2, f3; float shotAt; string shotTag; Vector2 startPos = new Vector2(3.6f, -3.2f); float camBack = 6.5f, camUp = 4.2f, maxLift; string shotDir; float quitAfter = 150f; List<Vector2> botPath; List<float> botLift = new List<float>(); float nextReplan; int botIdx; float stuckT; bool stuckLogged;
        readonly List<float> frameMs = new List<float>(); float nextShot; int shotNo; string resultNote = "";

        string placeName = ""; float placeT; int giveCoins; HashSet<int> hidden = new HashSet<int>();
        Material M(string n) => Resources.Load<Material>("Mats/" + n);

        void Awake()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-autoplay" && i + 1 < args.Length) { autoplay = true; poiIndex = int.Parse(args[i + 1]); }
                if (args[i] == "-noskills") noSkills = true;
                if (args[i] == "-level" && i + 1 < args.Length) { autoplay = true; levelArg = int.Parse(args[i + 1]); poiIndex = -1; }
                if (args[i] == "-resetsave") resetSave = true;
                if (args[i] == "-nopersist") noPersist = true;
                if (args[i] == "-panel" && i + 1 < args.Length) panelArg = args[i + 1];
                if (args[i] == "-demo" && i + 1 < args.Length) { demoWin = args[i + 1] == "win"; demoFail = args[i + 1] == "fail"; }
                if (args[i] == "-givecoins" && i + 1 < args.Length) giveCoins = int.Parse(args[i + 1]);
                if (args[i] == "-selftest") selfTest = true;
                if (args[i] == "-fxtest") fxTest = true;
                if (args[i] == "-menushot") menuShot = true;
                if (args[i] == "-cars" && i + 1 < args.Length) { carCount = int.Parse(args[i + 1]); carsArg = true; }
                if (args[i] == "-mobile") touchMode = true;
                if (args[i] == "-fakejoy") fakeJoy = true;
                if (args[i] == "-camback" && i + 1 < args.Length) camBack = float.Parse(args[i + 1]);
                if (args[i] == "-camup" && i + 1 < args.Length) camUp = float.Parse(args[i + 1]);
                if (args[i] == "-start" && i + 2 < args.Length) startPos = new Vector2(float.Parse(args[i + 1]), float.Parse(args[i + 2]));
                if (args[i] == "-shotdir" && i + 1 < args.Length) shotDir = args[i + 1];
                if (args[i] == "-crowd" && i + 1 < args.Length) { crowdCount = int.Parse(args[i + 1]); crowdArg = true; }
                if (args[i] == "-quitafter" && i + 1 < args.Length) quitAfter = float.Parse(args[i + 1]);
            }
            if (Application.isMobilePlatform) touchMode = true;
            if (touchMode)
            {   // スマホ向けの軽量設定(人と車を減らし、描画距離と影を絞る)
                if (!crowdArg) crowdCount = 5000; if (!carsArg) carCount = 150;
                Screen.orientation = ScreenOrientation.AutoRotation; Screen.autorotateToPortrait = Screen.autorotateToLandscapeLeft = Screen.autorotateToLandscapeRight = true; Screen.autorotateToPortraitUpsideDown = false;
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
            }
            QualitySettings.vSyncCount = autoplay ? 0 : 1; Application.targetFrameRate = autoplay ? -1 : 60;
            cam = Camera.main;
        }

        void Start()
        {
            Heightfield.Load();
            data = CityData.Load();
            // 道が通っている建物は、道のほうを優先して取り除く(壁に見えて通れる場所をなくす)
            { var probe = new CityBuilder(); probe.RegisterOnly(data); var g0 = new PedGraph(data); var g1 = new PedGraph(data, true); var hitCount = new Dictionary<int, int>();
              foreach (var gg in new[] { g0, g1 })
                  foreach (var e in gg.edges)
                  {
                      if (e.width <= 0f || !gg.inMain[e.a]) continue; var a0 = gg.nodes[e.a];
                      for (float t = 1.5f; t < e.len - 1.5f; t += 1.5f) { var p = a0 + e.dir * t; int bid = probe.BuildingAt(p.x, p.y); float lv = Mathf.Lerp(e.lA, e.lB, t / e.len); if (bid >= 0 && (lv < 0.05f || data.buildings[bid].h > lv + 0.5f)) hitCount[bid] = hitCount.TryGetValue(bid, out var c) ? c + 1 : 1; }
                  }
              hidden = new HashSet<int>(); float hh = 0f; int hit2 = 0; foreach (var kv in hitCount) if (kv.Value >= 2) hit2++;
              foreach (var kv in hitCount) if (kv.Value >= 1) { hidden.Add(kv.Key); hh += data.buildings[kv.Key].h; }
              Debug.Log($"[Graph] buildings hidden (road passes through)={hidden.Count} (2点以上={hit2}) of {data.buildings.Length} avgH={(hidden.Count > 0 ? hh / hidden.Count : 0):F1}"); }
            city = new CityBuilder(); city.Hidden = hidden;
            var pal = new Material[CityBuilder.PaletteSize];
            for (int i = 0; i < pal.Length; i++) pal[i] = M("Bld" + i);
            city.Build(data, M("Ground"), M("Road"), M("Walkway"), pal, M("Marking"));
            graph = new PedGraph(data);
            city.Exempt = null;   // 例外は設けない: 壁は高さで判定する(高架の上は屋根より高ければ通れる)
            int pruned = graph.Prune(city.InsideBuilding, false);
            graph.ComputeFree((x, z) => city.Blocked(x, z, 0.5f)); city.MakeWalkable(graph, M("Walkway")); city.MakeStairs(graph, M("Walkway"));
            var lm = new List<Vector2>(); foreach (var q in data.pois) lm.Add(new Vector2(q.x, q.z));
            lm.Add(Vector2.zero);
            graph.BuildHubs(300, 7, lm);
            var tints = new Material[10]; for (int i = 0; i < tints.Length; i++) tints[i] = M("Tint" + i);
            crowdHr = new HumanoidRenderer(tints, M("Pants"), M("Skin"));
            playerHr = new HumanoidRenderer(new[] { M("PlayerTint") }, M("PlayerPants"), M("Skin"));
            crowd = new Crowd(graph, crowdHr, crowdCount, 12345, (x, z) => city.Blocked(x, z, 0.5f));
            if (touchMode) ApplyMobileProfile();
            props = new Props(data, city, graph, city.Root.transform, touchMode); if (touchMode) props.DrawDist = 80f;
            carGraph = new PedGraph(data, true); int prunedCar = carGraph.Prune(city.InsideBuilding, false); Debug.Log($"[Graph] pruned walk={pruned} car={prunedCar}"); carGraph.BuildHubs(Mathf.Min(150, Mathf.Max(2, carGraph.MainCount / 4)), 11, new List<Vector2>());
            var carMats = new Material[6]; for (int i = 0; i < carMats.Length; i++) carMats[i] = M("Car" + i);
            traffic = new Traffic(carGraph, graph, crowd, carCount, carMats, M("CarCabin"));
            player = new Player(city, crowd, graph, Vector2.zero) { yaw = 0 };
            fx = new SkillFx(this, cam, M("FxAdd"), new[] { M("GhostA"), M("GhostB"), M("GhostC") }, M("Skin"));
            Store.NoPersist = noPersist || autoplay; if (resetSave) Store.Reset();
            if (giveCoins > 0) { Store.D.coins += giveCoins; Store.Commit(); }
            fx.SetAudio(Store.D.music, Store.D.sfx);
            levels = new LevelGen(graph, data, city); pickups = new Pickups(); pickups.OnPick = OnPick; ghost = new Ghost(); cosm = new Cosmetics(transform, M("FxAdd"));
            rivalHr = new HumanoidRenderer(new[] { Cosmetics.Flat(new Color(.55f, .3f, 1f)) }, Cosmetics.Flat(new Color(.12f, .1f, .25f)), M("Skin"));
            RebuildPlayerRenderer(); ApplyUpgrades(); Store.EnsureMissions();
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
            if (selfTest) { crowd.Stream = false; RunCollisionTests(); crowd.Stream = true; }
            Debug.Log($"[Shibuya] roads={data.roads.Length} buildings={city.BuildingCount} nodes={graph.nodes.Count} main={graph.MainCount} edges={graph.edges.Count} hubs={graph.hubNodes.Length} crowd={crowd.Count} post={fx.PostOk} trees={props.TreeCount} lamps={props.LampCount} signals={props.SignalCount} neon={props.NeonCount}");
            if (shotDir != null) Directory.CreateDirectory(shotDir);
            if (selfTest) RunLevelTests();
            crowd.Recenter(Vector2.zero, 0f);
            if (autoplay) StartLevel(poiIndex >= 0 ? levels.MakeFree(poiIndex) : levels.Make(levelArg));
            else
            {
                homeSpec = levels.Make(Store.D.level); player.pos = homeSpec.start; player.lift = graph.nodeLift[graph.NearestNode(homeSpec.start)];
                if (panelArg == "shop") panel = UiPanel.Shop; else if (panelArg == "missions") panel = UiPanel.Missions; else if (panelArg == "settings") panel = UiPanel.Settings; else if (panelArg == "free") panel = UiPanel.Free;
                dailyPopup = (panelArg == "daily") || (panelArg == "" && Store.GiftReady && !menuShot && Store.D.plays > 0);
                if (Store.D.plays == 0 && panelArg == "" && !menuShot && !demoWin && !demoFail) StartLevel(homeSpec);   // 初回はメニューを見せず、すぐ走り出す
                if (demoWin || demoFail) StartLevel(homeSpec);
            }
        }

        /// <summary>壁・人・車にめり込まないことの自動検証。結果は [SelfTest] としてログに出る。</summary>
        void RunCollisionTests()
        {
            // (1) 建物の壁: 外3mから壁に向かって3秒走り続け、建物の内側に入った件数を数える(高架や通路の例外の近くは除く)
            var empty = new Crowd(graph, crowdHr, 0, 1); int tested = 0, penetrated = 0; float worst = 0f;
            foreach (var (mid, outw) in city.WallSamples(400, 7))
            {
                var start = mid + outw * 3f; if (city.Blocked(start.x, start.y, 0.4f) || (city.Exempt != null && (city.Exempt.Invoke(start.x, start.y) || city.Exempt.Invoke(mid.x, mid.y)))) continue;
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
            // (3) ランダムに走り回る(ダッシュ・すり抜け・かき分け込み)プレイヤーが、建物の中・柱の中に入らないか
            { var rf = new System.Random(77); int fz = 0, steps = 0, inPillar = 0;
              for (int k = 0; k < 150; k++)
              {
                  int pi = rf.Next(crowd.Count); int guardN = 0; while ((crowd.LiftOf(pi) > 0.3f || city.InsideBuilding(crowd.PosOf(pi).x, crowd.PosOf(pi).y)) && guardN++ < 200) pi = rf.Next(crowd.Count);   // 1階の人の位置から出発(2階の人の真下は建物の中のことがある)
                  var q = crowd.PosOf(pi); var pl = new Player(city, empty, graph, q); var dirv = Vector2.up;
                  for (int f = 0; f < 600; f++)
                  {
                      if (f % 40 == 0) { float an = (float)rf.NextDouble() * 6.283f; dirv = new Vector2(Mathf.Cos(an), Mathf.Sin(an)); }
                      pl.Tick(1f / 30f, dirv, f % 97 == 0, f % 151 == 0, f % 71 == 0); steps++;
                      city.QueryLift = pl.lift; bool bad = city.Blocked(pl.pos.x, pl.pos.y, 0f); city.QueryLift = 0f; if (bad) { fz++; if (fz <= 6) Debug.Log($"[SelfTest] fuzz violation: pos={pl.pos} lift={pl.lift:F1} inCorridor={graph.InCorridor(pl.pos.x, pl.pos.y, pl.lift)} inside={city.InsideBuilding(pl.pos.x, pl.pos.y)} pillar={city.HitPillar(pl.pos.x, pl.pos.y, 0f)} step={f} slip={pl.Slipping} dash={pl.dashT > 0}"); }
                      if (pl.lift < 2.2f && city.HitPillar(pl.pos.x, pl.pos.y, 0f)) inPillar++;
                  }
              }
              Debug.Log($"[SelfTest] fuzz: players=150 steps={steps} insideBuilding={fz} insidePillar={inPillar} (期待 0)"); }
            Debug.Log($"[SelfTest] crowd: tested={ct} closer_than_0.4m={cp} (期待 ほぼ0) minDistance={minAll:F2}");
        }

        Vector2 ReadWish(out bool dash, out bool slip, out bool shove)
        {
            dash = input.dash; slip = input.slip; shove = input.shove;
            var inp = input.stick; if (inp.sqrMagnitude < 0.0001f) return Vector2.zero;
            float y = camYaw * Mathf.Deg2Rad; var f = new Vector2(Mathf.Sin(y), Mathf.Cos(y)); var r = new Vector2(f.y, -f.x);
            return f * inp.y + r * inp.x;
        }

        /// <summary>スマホ向け: 人と車の描画距離、影、解像度を絞る。</summary>
        void ApplyMobileProfile()
        {
            crowd.NearDist = 28f; crowd.FarDist = 120f; crowd.ActiveRadius = 120f;
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
            { urp.shadowDistance = 55f; urp.renderScale = 0.85f; urp.msaaSampleCount = 2; }
        }

        Vector2 BotWish(out bool dash, out bool slip, out bool shove)
        {
            float udt = Time.unscaledDeltaTime;
            dash = slip = shove = false;
            if (botPath == null || botPath.Count == 0) return Vector2.zero;
            if (player.lift > 0.3f && Time.unscaledTime > nextReplan)
            {   // 2階にいる間は、いまの階から経路を引き直す(真下の1階の道に向かって走り続けないように)
                nextReplan = Time.unscaledTime + 1.5f; var np2 = graph.Path(player.pos, goal, player.lift); if (np2.Count >= 2) { botPath = np2; botLift = new List<float>(graph.lastPathLift); botIdx = 0; }
            }
            while (botIdx < botPath.Count - 1 && (botPath[botIdx] - player.pos).sqrMagnitude < 2.5f * 2.5f && (botIdx >= botLift.Count || Mathf.Abs(botLift[botIdx] - player.lift) < 2.6f)) botIdx++;
            // 経路の線分に沿って、現在位置の少し先(4m)を狙う。押し出されて線からそれても、線へ戻れる
            var sa = botPath[Mathf.Max(botIdx - 1, 0)]; var sb = botPath[botIdx]; var sd = sb - sa; float sl = sd.magnitude;
            Vector2 aim = sb;
            if (sl > 1e-3f) { float tp = Mathf.Clamp(Vector2.Dot(player.pos - sa, sd / sl), 0f, sl); aim = sa + sd / sl * Mathf.Min(sl, tp + 4f); }
            var to = aim - player.pos;
            if (player.speedNow < 1.2f) stuckT += udt; else stuckT = 0;
            if (stuckT > 6f && !stuckLogged)
            {
                stuckLogged = true; var wp = botPath[botIdx]; var np = player.pos + (wp - player.pos).normalized * 0.5f;
                Debug.Log($"[Stuck] pos={player.pos} lift={player.lift} wp={wp} blockedHere={city.Blocked(player.pos.x, player.pos.y)} insideBld={city.InsideBuilding(player.pos.x, player.pos.y)} exemptHere={city.Exempt(player.pos.x, player.pos.y)} blockedAhead={city.Blocked(np.x, np.y)} aheadInside={city.InsideBuilding(np.x, np.y)} aheadExempt={city.Exempt(np.x, np.y)} carHit={traffic.Collide(player.pos, player.lift, out var cp, out _)} idx={botIdx}/{botPath.Count}");
            }
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

        Vector3 pw0() => new Vector3(player.pos.x, Heightfield.Current.Height(player.pos.x, player.pos.y) + player.lift, player.pos.y);
        int lastRemI = -1; bool demoShot;

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f), rdt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            bool frozen = state == GameState.Paused; if (frozen) { dt = 0f; rdt = 0f; }
            ComputeLayout(); input.uiScale = L.S; input.playing = state == GameState.Playing && !autoplay;
            input.btnDash = new InputHub.Circle { c = L.sDash, r = L.sR }; input.btnSlip = new InputHub.Circle { c = L.sSlip, r = L.sR }; input.btnShove = new InputHub.Circle { c = L.sShove, r = L.sR };
            input.noDrive.Clear(); input.noDrive.Add(L.timer); input.noDrive.Add(L.chip); input.noDrive.Add(L.minimap); input.noDrive.Add(L.pause);
            input.Update(rdt);
            if (input.usingTouch && !touchMode) touchMode = true;
            if (fakeJoy) { input.stickActive = true; input.stickOrigin = new Vector2(L.x0 + 130f, L.y1 - 110f); input.stickPos = input.stickOrigin + new Vector2(38f, -30f); }
            if (state == GameState.Playing || state == GameState.Countdown) frameMs.Add(Time.unscaledDeltaTime * 1000f);
            stateT += dt;
            if (adT > 0f) { adT -= Time.unscaledDeltaTime; if (adT <= 0f) { adT = 0f; var cb = adCallback; adCallback = null; cb?.Invoke(); } }
            UpdateToasts();
            hb += Time.unscaledDeltaTime; if (autoplay && hb > 5f) { hb = 0; Debug.Log($"[HB] t={Time.timeSinceLevelLoad:F0} wait={crowd.Waiting} state={state} race={raceT:F1} pos={player.pos} lift={player.lift:F1} idx={botIdx}/{(botPath != null ? botPath.Count : 0)} spd={player.speedNow:F1} stag={player.stagger:F1} coins={runCoins} fps={1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f):F0}"); }
            bool dash = false, slip = false, shove = false; Vector2 wish = Vector2.zero;
            if (state == GameState.Menu)
            {
                if (panel == UiPanel.None && !dailyPopup && adT <= 0f && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))) StartLevel(homeSpec);
                if (panel == UiPanel.None && !dailyPopup) for (int k = 0; k < Mathf.Min(data.pois.Length, 9); k++) if (Input.GetKeyDown(KeyCode.Alpha1 + k)) StartLevel(levels.MakeFree(k));
                if (Input.GetKeyDown(KeyCode.Escape)) { if (panel != UiPanel.None) panel = UiPanel.None; dailyPopup = false; }
                camYaw += (panel == UiPanel.Shop ? 30f : 8f) * rdt;
            }
            else if (state == GameState.Countdown)
            {
                int ci = (int)stateT; if (ci != lastCount && ci < 3) { lastCount = ci; fx.PlayReady(); }
                if (stateT >= 3f) { state = GameState.Playing; stateT = 0; fx.PlayGo(); fx.Banner = "GO!"; fx.BannerSub = spec.modText; fx.BannerColor = new Color(.5f, 1f, .6f); fx.BannerT = 0.8f; }
            }
            else if (state == GameState.Playing)
            {
                if (!autoplay && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P))) Pause();
                wish = autoplay ? BotWish(out dash, out slip, out shove) : ReadWish(out dash, out slip, out shove);
                if (!fx.ClockFrozen) raceT += dt;
                pickups.Tick(dt, player.pos, player.lift, Mathf.Max(magnetBase, magnetT > 0f ? 8f : 0f));
                ghost.Record(dt, player.pos, player.yaw, player.lift); ghostShown = ghost.Sample(raceT);
                if (rival != null) rival.Tick(dt);
                trickT -= rdt; if (trickT <= 0f) trickN = 0;
                if (ghostShown && !ghostPassed && raceT > 3f && (player.pos - goal).magnitude < (ghost.pos - goal).magnitude - 3f) { ghostPassed = true; Pop("ゴーストを抜いた!", pw0() + Vector3.up * 3f, new Color(.8f, 1f, 1f), 1.4f); }
                magnetT = Mathf.Max(0f, magnetT - dt); coinStreakT -= dt; if (coinStreakT <= 0f) coinStreak = 0; nearCd -= rdt; hintT += dt;
                int remI = Mathf.CeilToInt(Remaining); if (!spec.free && remI != lastRemI && remI <= 5 && remI >= 1) fx.PlayReady(); lastRemI = remI;
                if ((player.pos - goal).magnitude < GoalRadius && Mathf.Abs(player.lift - goalLift) < 2.5f) OnReachGoal();
                else if (!spec.free && Remaining <= 0f) OnFail("TIME UP");
                if ((demoWin || demoFail) && stateT > 1.5f)
                {
                    runCoins = 47; combo = 31; runMaxCombo = 31; demoShot = true;
                    if (demoWin) { raceT = Mathf.Max(1f, par3 - 2f); demoWin = false; OnReachGoal(); } else { raceT = spec.limit + bonusTime; demoFail = false; OnFail("TIME UP"); }
                }
            }
            else if (state == GameState.Finished || state == GameState.Failed)
            {
                if (Input.GetKeyDown(KeyCode.R)) StartLevel(spec);
                if (state == GameState.Finished && !spec.free && Input.GetKeyDown(KeyCode.N)) StartLevel(levels.Make(Store.D.level));
                if (Input.GetKeyDown(KeyCode.M)) GoHome();
            }
            else if (state == GameState.Paused)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)) Resume();
            }
            if (state != GameState.Playing) { dash = slip = shove = false; wish = Vector2.zero; }
            if (state != GameState.Playing) coinStreak = 0;

            if (!autoplay && !frozen)
            {   // 視点: 手動で回す(右ドラッグ・←→・右側スワイプ)。それ以外は進む向きへ自動で追従する
                camYaw += input.camDelta;
                if (state == GameState.Playing && input.manualCamT <= 0f && input.stick.sqrMagnitude > 0.09f)
                {
                    float ang = Vector2.Angle(Vector2.up, input.stick); float k = input.kbMove ? (ang < 15f ? 2.2f : 0f) : (ang < 60f ? 2.2f : ang < 110f ? 0.7f : 0f);   // キー操作は斜め・横で視点を回さない(円を描いてしまうため)
                    if (k > 0f) camYaw = Mathf.LerpAngle(camYaw, player.yaw, 1f - Mathf.Exp(-k * rdt));
                }
            }

            // 人混みはメニューでも動かす
            crowd.Step(dt, player.pos, !player.Slipping, player.lift);
            UpdateRouteGuide(rdt);
            placeT -= rdt; if (placeT <= 0f) { placeT = 0.4f; placeName = levels.Place(player.pos, player.lift); }
            traffic.Step(dt);
            player.Tick(rdt, wish, dash, slip, shove);
            var pw = new Vector3(player.pos.x, Heightfield.Current.Height(player.pos.x, player.pos.y) + player.lift, player.pos.y);
            var pdir = new Vector2(Mathf.Sin(player.yaw * Mathf.Deg2Rad), Mathf.Cos(player.yaw * Mathf.Deg2Rad));
            if (player.evDash) { fx.OnDash(pw, pdir); player.evDash = false; if (state == GameState.Playing) Trick(pw); }
            if (player.evSlip) { fx.OnSlip(pw); player.evSlip = false; if (state == GameState.Playing) Trick(pw); }
            if (player.evShove) { fx.OnShove(pw, pdir); player.evShove = false; if (state == GameState.Playing) Trick(pw); }
            if (player.evBump) { fx.OnBump(pw, player.bumpDir); player.evBump = false; player.evBump2 = true; }
            fx.Tick(rdt, player, pw, state == GameState.Playing);
            // コンボ: 突進・かき分けで人を弾き飛ばすたびに増え、途切れると終わる。25ごとに時計を1秒戻す
            int hitsNow = player.plowed + player.shoved; int dh = hitsNow - lastHits; lastHits = hitsNow;
            if (state == GameState.Playing && dh > 0)
            {
                combo += dh; runMaxCombo = Mathf.Max(runMaxCombo, (int)combo); comboT = 1.6f; comboPop = Time.unscaledTime; fx.PlayPop((int)combo);
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
                if (fast && hitCd <= 0 && state == GameState.Playing)
                {
                    if (player.shieldT > 0f) { hitCd = 1f; fx.BigBurst(pw + Vector3.up, new Color(.8f, 1f, 1f)); Pop("シールド!", pw + Vector3.up * 2.5f, new Color(.85f, 1f, 1f), 1.2f); player.Knock(push.normalized * 5f); }
                    else { hitCd = 2f; hitFlash = 1.5f; hits++; raceT += 3f; player.Knock(push.normalized * 9f); Haptic(); if (!spec.free && spec.mod == 3) OnFail("NO DAMAGE"); }
                }
            }
            else if (state == GameState.Playing && nearCd <= 0f && !player.Slipping && player.speedNow > 2.5f && traffic.NearMiss(player.pos, player.lift))
            {
                nearCd = 1.4f; nearMiss++; runCoins += 3; fx.PlayCoin(10); Pop("ニアミス! +3", pw + Vector3.up * 2.6f, new Color(1f, .75f, .3f), 1.1f); coinPulse = Time.unscaledTime;
            }
            if (autoplay && state == GameState.Playing) camYaw = Mathf.LerpAngle(camYaw, player.yaw, 2f * dt);
            UpdateCamera(rdt);
            UpdateArrow(); UpdateRings();
            crowd.Render(cam.transform.position, cam.transform.forward); traffic.Render(cam.transform.position); props.Render(cam.transform.position, crowd.Clock);
            if (state != GameState.Menu) { pickups.Render(cam.transform.position); RenderRouteGuide(); }
            float lean = Mathf.Clamp(player.speedNow * 1.6f, 0, 18f);
            float headFit = player.lift > 0.3f ? Mathf.Clamp((graph.Clearance(player.pos.x, player.pos.y, player.lift) - 0.1f) / 2.4f, 0.55f, 1f) : 1f;
            playerHr.Add(pw, player.yaw, player.phase, Mathf.Clamp01(player.speedNow / 3f), 0, 1.15f * headFit, lean, Mathf.Clamp(1f + sq + (player.dashT > 0 ? 0.1f : 0f), 0.6f, 1.4f));
            cosm.DrawHat(Store.D.hat, playerHr.LastHead);
            cosm.UpdateTrail(Store.D.trail, pw, state == GameState.Playing && player.speedNow > 1.5f);
            if (rival != null && state != GameState.Menu) rivalHr.Add(new Vector3(rival.pos.x, Heightfield.Current.Height(rival.pos.x, rival.pos.y) + rival.lift, rival.pos.y), rival.yaw, rival.phase, 1f, 0, 1.2f, 14f);
            if (ghostShown && state != GameState.Menu) ghostHr.Add(new Vector3(ghost.pos.x, Heightfield.Current.Height(ghost.pos.x, ghost.pos.y) + ghost.lift, ghost.pos.y), ghost.yaw, ghost.phase, 1f, 0, 1.12f, 12f);
            maxLift = Mathf.Max(maxLift, player.lift);
            crowdHr.Flush(); playerHr.Flush(); rivalHr.Flush(); if (ghostHr != null) ghostHr.Flush();
            beam.transform.Rotate(0, 60 * dt, 0);

            if ((state == GameState.Finished || state == GameState.Failed) && shotDir != null && !finShot2 && stateT > 2.3f) { finShot2 = true; Shot("finish2"); if (demoShot) menuQuitAt = Time.timeSinceLevelLoad + 1.2f; }
            if (menuShot && state == GameState.Menu && Time.timeSinceLevelLoad > 3f) { Shot("menu" + (panelArg != "" ? "_" + panelArg : "")); menuShot = false; menuQuitAt = Time.timeSinceLevelLoad + 1.5f; }
            if (menuQuitAt > 0f && Time.timeSinceLevelLoad > menuQuitAt) Application.Quit();
            if (shotTag != null && Time.unscaledTime >= shotAt) { Shot(shotTag); shotTag = null; }
            if (shotDir != null && state != GameState.Menu && !fxTest && !demoShot)
            {
                nextShot -= dt; if (nextShot <= 0) { nextShot = 9f; Shot("t" + shotNo.ToString("D2")); shotNo++; }
            }
            if (autoplay && ((state == GameState.Finished || state == GameState.Failed) && stateT > 2.9f || Time.timeSinceLevelLoad > quitAfter)) Finish();
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
            sb.Append($"\"frames\":{n},\"avgMs\":{avg:F2},\"avgFps\":{(avg > 0 ? 1000f / avg : 0):F1},\"p95Ms\":{p95:F2},\"worstMs\":{worst:F2},\"crowd\":{crowd.Count},\"trips\":{crowd.Trips},\"waitingEnd\":{crowd.Waiting},\"cars\":{traffic.Count},\"carsStopped\":{traffic.Stopped},\"carHits\":{hits},\"onDeck\":{crowd.OnDeck},\"maxPlayerLift\":{maxLift:F1},\"bumps\":{player.bumps},\"plowed\":{player.plowed},\"buildings\":{city.BuildingCount},\"level\":{(spec != null ? spec.number : 0)},\"pathLen\":{(spec != null ? spec.pathLen : 0):F0},\"limit\":{(spec != null ? spec.limit : 0):F0},\"coins\":{runCoins},\"coinsTotal\":{pickups.CoinsTotal},\"stars\":{res.stars},\"reward\":{res.total},\"state\":\"{state}\"}}");
            if (shotDir != null) File.WriteAllText(Path.Combine(shotDir, "result.json"), sb.ToString());
            Debug.Log("[Perf] " + sb);
        }

        Rect camRectWant = new Rect(0, 0, 1, 1);

        void UpdateCamera(float dt)
        {
            var p = new Vector3(player.pos.x, Heightfield.Current.Height(player.pos.x, player.pos.y) + player.lift, player.pos.y);
            bool shop = state == GameState.Menu && panel == UiPanel.Shop;
            var wantRect = shop ? (L.portrait ? new Rect(0, 0.54f, 1, 0.46f) : new Rect(0, 0, 0.42f, 1)) : new Rect(0, 0, 1, 1);
            if (wantRect != camRectWant) { camRectWant = wantRect; cam.rect = wantRect; }
            float y = camYaw * Mathf.Deg2Rad; var back = new Vector3(-Mathf.Sin(y), 0, -Mathf.Cos(y));
            float narrow = Screen.width < Screen.height ? 1.45f : 1f;   // 縦画面は横幅が狭いので、引いて高くする
            var want = shop ? p + back * 3.6f + Vector3.up * 1.7f : p + back * camBack * narrow + Vector3.up * camUp * (narrow > 1f ? 1.35f : 1f);
            if (!shop && !city.InsideBuilding(player.pos.x, player.pos.y))
            {   // カメラが建物の壁にめり込まないよう、プレイヤー側に寄せる
                var head = p + Vector3.up * 1.5f; var dv = want - head;
                for (int k = 1; k <= 12; k++)
                {
                    var q = head + dv * (k / 12f);
                    if (city.InsideBuilding(q.x, q.z)) { float f = Mathf.Max(0.1f, (k - 1) / 12f - 0.04f); want = head + dv * f + Vector3.up * (1f - f) * 1.6f; break; }
                }
            }
            cam.transform.position = Vector3.Lerp(cam.transform.position, want, 1f - Exp14(dt)) + (fx != null ? fx.ShakeOffset : Vector3.zero);
            cam.fieldOfView = shop ? 40f : (Screen.width < Screen.height ? 76f : 62f) + Mathf.Min(8f, player.speedNow * 0.6f) + (fx != null ? fx.FovAdd : 0f);
            var look = shop ? p + Vector3.up * 1.0f : p + Vector3.up * 1.3f + new Vector3(Mathf.Sin(y), 0, Mathf.Cos(y)) * 2f;
            cam.transform.rotation = Quaternion.LookRotation(look - cam.transform.position) * Quaternion.AngleAxis(roll, Vector3.forward);
            miniCam.transform.position = new Vector3(player.pos.x, 300, player.pos.y);
        }
        static float Exp14(float dt) => Mathf.Exp(-14f * dt);
    }
}
