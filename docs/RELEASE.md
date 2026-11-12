# SHIBUYA RUSH 公開までの課題と進み具合

## 公開の方針(更新)

アプリ(App Store)ではなく、Webゲームとして公開する。tao-matsumr.com に作るゲーム集約ページ(my-web-site リポジトリ)から /shibuya-rush/ で遊べる。広告は Google AdSense のゲーム向け広告(動画のご褒美と全画面広告)を使う。

- Web版のビルド: `Unity -batchmode -quit -projectPath game -executeMethod ProjectSetup.BuildWeb` で Build/Web に出力。my-web-site の public/shibuya-rush にコピーする。
- 広告の有効化: game/Assets/WebGLTemplates/Shibuya/ads-config.js の SHIBUYA_ADSENSE_CLIENT に AdSense のパブリッシャーID(ca-pub-…)を入れる。審査中は SHIBUYA_ADS_TEST を true のままにする。
- AdSense のサイト承認後に、ads.txt を my-web-site の public に置く。
- プライバシーポリシーは my-web-site の /privacy に作成済み(公開後に内容を確認する)。
- Webビルドの落とし穴: WebGLの例外処理は ExplicitlyThrownExceptionsOnly にする(None だとエンジン内部の例外で止まる)。CreatePrimitive は使わず Prim.cs を使う。ビルドを作り直したら、ブラウザのキャッシュを避けて確認する(古いビルドのファイルが使われて、読み込みエラーに見える)。

現在の状態: **まだ公開できる水準ではない**。実機(iPhone)で遊べるところまでは来ているが、下の「公開前に必須」がそろっていない。

## 公開前に必須(ブロッカー)

| 項目 | 状態 | 内容 |
|---|---|---|
| 実機での安定性・性能 | 未確認 | iPhone で長時間遊んでの落ち・発熱・fps。起動時の読み込みは段階表示に変更済み(OSに止められにくくした)。 |
| 当たり判定・地形の不具合 | 対応中 | 自動検証で壁・柱・斜面・最短経路を確認。ボットの回帰テスト(レベル1〜16)を継続。 |
| 実在の施設名・商標 | 対応済み | 公共の場所・通りの名前はそのまま、企業・商業施設の名前はパロディ名に置き換え(`Names.cs`。例: メガドンコ、SHIBUYA TSUDAYA、渋谷ヒカルエ、SHIBUYA108)。置き換え表は `Names.cs` で増やせる。 |
| 地図データの表示義務 | 対応済み | 設定 > クレジット・規約に OpenStreetMap (ODbL)、PLATEAU (CC BY 4.0)、国土地理院、フォント (OFL) を表示。 |
| プライバシーポリシーのページ | **未作成** | 公開用のURLが必要(広告を使うため必須)。`docs/` に置いて GitHub Pages で公開するのが手軽。 |
| 広告SDK | 準備済み・未接続 | `Ads.cs` に窓口(動画広告・全画面広告・広告を消す)を作成済み。AdMob は公開テスト用IDで動く形。実際のSDKの導入と、自分のAdMobアカウントのID設定が必要。 |
| 広告を消す(課金) | 擬似 | `Purchases.cs` は擬似購入。App Store Connect の商品登録と Unity IAP の接続が必要。 |
| 同意・追跡の許可 | 未対応 | iOS の ATT(追跡の許可)と、EU向けの同意(UMP)。AdMob SDK 導入時に合わせて入れる。 |
| Apple Developer Program | **要アカウント** | App Store 提出・TestFlight には有料アカウント(年額)が必要。署名と Bundle ID の確定も。 |
| ストア素材 | 未作成 | スクリーンショット、説明文、年齢区分、アプリのプライバシー情報の申告。 |

## 公開前にやりたい(品質)

- 初回プレイの流れ(チュートリアル)を、実機で見直す。
- スマホの操作感(スティック・カメラ・スキルボタン)の調整。
- 音(BGM・効果音)の聞き比べと音量バランス。
- 街の再現度(駅周辺の大きな建物、屋根の形、細かい地形)。
- ボットで全レベルが完走できること(制限時間の調整)。

## 広告の方針(実装済みの動き)

- 動画広告(任意視聴): クリア報酬の倍率、失敗後の +15 秒、ログインボーナス ×2、ショップの +150 コイン。
- 全画面広告: 序盤(3勝まで)は出さない。2回の挑戦ごと・150秒以上あけて・動画広告の直後90秒は出さない。
- 広告を消す(課金)で、全画面広告を止める。動画広告は任意視聴のまま残す。

## 次の作業の順番

1. 実機の不具合を直す(報告ベース)。
2. 架空名への差し替えの判断。
3. AdMob SDK の導入(テスト広告で実機確認)。
4. プライバシーポリシーの公開、ATT・同意の対応。
5. 課金(Unity IAP)の接続。
6. ストア素材の作成と TestFlight での配布。
