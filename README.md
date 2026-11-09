# SHIBUYA RUSH

ハチ公前から、大混雑の渋谷を最速で目的地まで走り抜ける3Dゲーム(Unity 6000.4.4f1 / URP)。
街は実際の渋谷のOSMデータ(道路・建物)から生成し、道という道を3万人規模の人混みが歩いています。

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

## 人混み
- 各人が約300か所のハブ(目的地)から1つを選び、道路グラフ上の最短経路で歩く。着いたら別のハブを選び直し、永遠に続く。
- 既定は30,000人(`-crowd N` で変更)。M4 Proで約100〜110fps、40,000人で約80fps、20,000人で約160fps(Botでの実測、1280x720)。
- 近くは関節つきの人形(胴・頭・両腕・両脚が歩行位相で振れる)、遠くは簡易形状。GPUインスタンシングで描画。

## 作り直す
```bash
python3 tools/fetch_osm.py     # Overpass から取得(data/raw)
python3 tools/build_city.py    # game/Assets/Resources/city.json を生成(ハチ公像が原点、x=東 z=北 [m])
# ビルド(約1〜2分)
/Applications/Unity/Hub/Editor/6000.4.4f1/Unity.app/Contents/MacOS/Unity -batchmode -quit \
  -projectPath game -executeMethod ProjectSetup.BuildMac -logFile build.log
```

## 自動プレイ(検証用Bot)
```bash
tools/run_bot.sh <目的地0-5> <出力名> [-crowd N]   # Shots/<出力名>/ にスクショとresult.json
```
Botは道路グラフのA*で目的地に向かい、詰まるとダッシュ・すり抜け・かき分けを使います。直接起動するとウィンドウが隠れて止まることがあるため、スクリプトは `open` で前面に出して起動します。

## 既知の制約
- 標高は未反映(平坦)。渋谷の坂や、駅の立体構造(高架・地下)は再現していない。
- 建物はOSMの輪郭を高さで押し出しただけ(高さ情報がないものは14m)。PLATEAUは未使用。
- 人どうしの当たりは簡易的な押し合いのみ。

地図データ: © OpenStreetMap contributors (ODbL)
