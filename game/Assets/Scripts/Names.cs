using System.Collections.Generic;

namespace ShibuyaRush
{
    /// <summary>
    /// 施設名の扱い。公共の場所・通りの名前などはそのまま使い、企業・商業施設の名前はパロディ名にして表示する。
    /// (実在の商標をそのまま出さないため。地図データの名前を読み込むときに一括で置き換える。)
    /// </summary>
    public static class Names
    {
        static readonly (string from, string to)[] Parody =
        {
            ("メガドンキ", "メガドンコ"), ("ドン・キホーテ", "ドン・キホーク"), ("ドンキ", "ドンコ"),
            ("SHIBUYA TSUTAYA", "SHIBUYA TSUDAYA"), ("TSUTAYA", "TSUDAYA"), ("ツタヤ", "ツダヤ"),
            ("渋谷ヒカリエ", "渋谷ヒカルエ"), ("ヒカリエ", "ヒカルエ"),
            ("渋谷スクランブルスクエア", "渋谷スクランブラースクエア"), ("スクランブルスクエア", "スクランブラースクエア"),
            ("渋谷マークシティ", "渋谷マークシチー"), ("マークシティ", "マークシチー"),
            ("Shibuya 109", "SHIBUYA 108"), ("SHIBUYA109", "SHIBUYA108"), ("渋谷109", "渋谷108"), ("109", "108"),
            ("渋谷ストリーム", "渋谷ストリーミ"), ("渋谷フクラス", "渋谷フクラッス"), ("セルリアンタワー", "セルリアンタワァ"),
            ("ビックカメラ", "ビッグカメレオン"), ("ヤマダ電機", "ヤマタ電器"), ("LABI", "LABO"),
            ("パルコ", "パルク"), ("PARCO", "PARK-O"), ("西武", "西舞"), ("東急", "東級"), ("東映", "東栄"),
            ("セブン-イレブン", "セブン-エレブン"), ("セブンイレブン", "セブンエレブン"), ("ローソン", "ローゾン"), ("ファミリーマート", "ファミリィマーケット"),
            ("カラオケ館", "カラオケ殿"), ("マクドナルド", "マクドナルズ"), ("スターバックス", "スターバックス風"), ("ユニクロ", "ユニグロ"),
            ("ロフト", "ロフテ"), ("モディ", "モデイ"), ("BINGO", "BINGOO"), ("MAGNET", "MAGNETO"),
        };

        /// <summary>地図データの名前を、表示してよい名前にする。</summary>
        public static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            foreach (var (from, to) in Parody) if (s.Contains(from)) s = s.Replace(from, to);
            return s;
        }

        public static void Apply(CityData d)
        {
            if (d.pois != null) foreach (var p in d.pois) p.name = Safe(p.name);
            if (d.buildings != null) foreach (var b in d.buildings) b.name = Safe(b.name);
            if (d.roads != null) foreach (var r in d.roads) r.name = Safe(r.name);
        }
    }
}
