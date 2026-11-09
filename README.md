# SHIBUYA RUSH

ハチ公前から、大混雑の渋谷を最速で目的地まで走り抜ける3Dゲーム(Unity 6000.4.4f1 / URP)。
街は実際の渋谷のデータから生成します。道路はOSM、建物の高さはPLATEAU、標高は国土地理院DEM。道という道を3万人規模の人混みが歩いています。

## 遊ぶ
`Build/ShibuyaRush.app` を開く(未ビルドなら下の「ビルド」)。メニューで目的地を選ぶ(1〜6キーでも可)。

| 操作 | |
|---|---|
| WASD | 移動(カメラ基準) |
| マウス / ←→ | 視点 |
| Shift | ダッシュ(1.4秒、人混みの抵抗が軽くなる。CT 4.5秒) |
| Space | すり抜け(0.9秒、人にぶつからず少し速い。CT 7秒) |
| E | かき分け(前方の人を左右に弾く。CT 5秒) |
| R / M | もう一度 / 目的地選択 |

目的地は OSM の実在地点: メガドンキ、SHIBUYA TSUTAYA、TSUTAYA(東口側)、スクランブルスクエア、ヒカリエ、マークシティ。
建物の中には入れず、道に限らず建物の隙間は通れます。

## 街
- 建物の外壁は窓つき(テクスチャ)。高さはPLATEAU。
- 高架: OSMの bridge / layer から、歩道橋・駅のデッキ・首都高(高さ5.2m×layer、首都高は10.4m)を持ち上げて描く。両端は地面から滑らかに上り、18mおきに柱を立てる。階段は両端の高さをつなぐ斜面。人混みの約5%とプレイヤーは高架の上も歩ける。
- 車: 800台が左側通行で走る。信号つき横断歩道の近くでは歩行者青の間止まり、それ以外の交差点では東西・南北を交互に通す。前の車と、前方の歩行者を避けて止まる。走行中の車(時速7km超)に当たると +3秒 と弾き飛ばし。止まっている車は通り抜けられる。
- 歩行者は、車道では道の両端(歩道側)を歩く。

## 人混み
- 各人が約300か所のハブ(目的地)から1つを選び、道路グラフ上の最短経路で歩く。着いたら別のハブを選び直し、永遠に続く。
- 既定は30,000人と車800台(`-crowd N` `-cars N` で変更)。M4 Proで約77〜91fps(Botでの実測、1280x720、6か所の目的地の平均で約86fps)。車なしなら約105〜112fps。
- 信号つき横断歩道(OSMのcrossing=traffic_signals、91本)では、赤の間は手前で待ち、青で一斉に渡る(周期90秒、青38秒。80m四方の格子ごとに位相をそろえている)。車は走っていない。
- 近くは関節つきの人形(胴・頭・両腕・両脚が歩行位相で振れる)、遠くは簡易形状。GPUインスタンシングで描画。

## 作り直す
```bash
python3 tools/fetch_osm.py     # Overpass から取得(data/raw)
pip3 install mapbox-vector-tile && python3 tools/fetch_plateau.py   # PLATEAUの建物高さ(data/raw/plateau.json)
python3 tools/build_terrain.py # 国土地理院DEMから game/Assets/Resources/terrain.bytes を生成
python3 tools/build_city.py    # game/Assets/Resources/city.json を生成(ハチ公像が原点、x=東 z=北 [m])
# ビルド(約1〜2分)
/Applications/Unity/Hub/Editor/6000.4.4f1/Unity.app/Contents/MacOS/Unity -batchmode -quit \
  -projectPath game -executeMethod ProjectSetup.BuildMac -logFile build.log
```

## 自動プレイ(検証用Bot)
```bash
tools/run_bot.sh <目的地0-5> <出力名> [-crowd N] [-cars N] [-noskills] [-start x z] [-selftest]   # Shots/<出力名>/ にスクショとresult.json
```
Botは道路グラフのA*で目的地に向かい、詰まるとダッシュ・すり抜け・かき分けを使います。直接起動するとウィンドウが隠れて止まることがあるため、スクリプトは `open` で前面に出して起動します。

## 検証の記録(Botでの自動プレイ、30,000人)
- スキルの効果: メガドンキ 50.7秒(スキルあり)/60.0秒(なし)、ヒカリエ 75.8秒/90.5秒。どちらも約15%短縮。Botの1回ずつの測定なので目安。`-noskills` で無効にできる。
- 建物高さの出どころ: 4,459棟のうち PLATEAU 4,299、OSM 54、既定値(14m) 106。
- 標高: ハチ公像の地面を0として -6.7〜+23.4m(格子4m)。

## 既知の制約
- 標高は4m格子。地下(地下街・地下鉄)は再現していない。高架はOSMにあるものだけで、駅ビルの内部通路は無い。
- 建物は輪郭を高さで押し出した箱。使っているMVT(indigo-lab配信)には輪郭と高さしかなく、屋根の形などPLATEAU LOD2の形状は入っていない(形状まで使うにはCityGML/3D Tilesの取得が必要)。
- 信号を守る義務はプレイヤーにはない(車に当たると+3秒)。車の種類はセダン1種類のみ。
- 人どうしの当たりは簡易的な押し合いのみ。

出典: 地図データ © OpenStreetMap contributors (ODbL) / 標高 国土地理院 / 建物高さ 国土交通省 Project PLATEAU のデータを加工(indigo-lab/plateau-lod2-mvt)
