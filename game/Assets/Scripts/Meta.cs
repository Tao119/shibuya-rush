using System;
using UnityEngine;

namespace ShibuyaRush
{
    /// <summary>ショップの品揃え・ミッション・実績の定義。</summary>
    public static class Meta
    {
        public struct Upgrade { public string name, desc; public int baseCost, max; public float step; }
        public static readonly Upgrade[] Upgrades =
        {
            new Upgrade { name = "あしの速さ",     desc = "歩く・走る速さ +4%/Lv",        baseCost = 120, max = 10, step = 0.04f },
            new Upgrade { name = "ダッシュ回復",   desc = "ダッシュの待ち時間 -6%/Lv",    baseCost = 100, max = 10, step = 0.06f },
            new Upgrade { name = "すり抜け回復",   desc = "すり抜けの待ち時間 -6%/Lv",    baseCost = 100, max = 10, step = 0.06f },
            new Upgrade { name = "かき分け範囲",   desc = "かき分けの範囲と回復を強化",   baseCost = 100, max = 10, step = 0.05f },
            new Upgrade { name = "コイン磁石",     desc = "コインを吸い寄せる範囲 +0.6m/Lv", baseCost = 80, max = 10, step = 0.6f },
            new Upgrade { name = "コインボーナス", desc = "クリア報酬 +8%/Lv",            baseCost = 150, max = 10, step = 0.08f },
        };
        public static int UpgradeCost(int i, int lv) => Mathf.RoundToInt(Upgrades[i].baseCost * Mathf.Pow(1.38f, lv) / 5f) * 5;

        public struct Skin { public string name; public Color shirt, pants; public int price; }
        public static readonly Skin[] Skins =
        {
            new Skin { name = "オレンジ",   shirt = new Color(1f, .42f, .08f),  pants = new Color(.08f, .08f, .1f),   price = 0 },
            new Skin { name = "ミント",     shirt = new Color(.2f, .85f, .62f), pants = new Color(.1f, .2f, .3f),     price = 300 },
            new Skin { name = "ハチ公",     shirt = new Color(.78f, .6f, .4f),  pants = new Color(.95f, .92f, .85f),  price = 600 },
            new Skin { name = "さくら",     shirt = new Color(1f, .6f, .78f),   pants = new Color(.45f, .2f, .35f),   price = 800 },
            new Skin { name = "スカイ",     shirt = new Color(.3f, .65f, 1f),   pants = new Color(.95f, .95f, 1f),    price = 1000 },
            new Skin { name = "ニンジャ",   shirt = new Color(.12f, .12f, .16f), pants = new Color(.55f, .08f, .12f), price = 1500 },
            new Skin { name = "ゴールド",   shirt = new Color(1f, .8f, .2f),    pants = new Color(.6f, .35f, .1f),    price = 2500 },
            new Skin { name = "ネオン",     shirt = new Color(.75f, .35f, 1f),  pants = new Color(.1f, .9f, .95f),    price = 3500 },
        };

        public struct Hat { public string name; public int price; public Color color, color2; }
        public static readonly Hat[] Hats =
        {
            new Hat { name = "なし" },
            new Hat { name = "キャップ",   price = 200,  color = new Color(.9f, .2f, .25f) },
            new Hat { name = "ネコ耳",     price = 400,  color = new Color(.95f, .75f, .85f) },
            new Hat { name = "わかば",     price = 500,  color = new Color(.35f, .85f, .4f) },
            new Hat { name = "シルクハット", price = 900, color = new Color(.15f, .15f, .2f), color2 = new Color(.9f, .2f, .3f) },
            new Hat { name = "いぬ耳",     price = 700,  color = new Color(.55f, .38f, .22f) },
            new Hat { name = "ヘイロー",   price = 1500, color = new Color(1f, .9f, .4f) },
            new Hat { name = "おうかん",   price = 3000, color = new Color(1f, .8f, .15f) },
        };

        public struct Trail { public string name; public int price; public Color a, b; }
        public static readonly Trail[] Trails =
        {
            new Trail { name = "なし" },
            new Trail { name = "ホワイト",   price = 300,  a = new Color(1f, 1f, 1f, .9f),    b = new Color(1f, 1f, 1f, 0f) },
            new Trail { name = "ファイア",   price = 800,  a = new Color(1f, .8f, .2f, 1f),   b = new Color(1f, .15f, .05f, 0f) },
            new Trail { name = "アイス",     price = 1000, a = new Color(.7f, 1f, 1f, 1f),    b = new Color(.2f, .5f, 1f, 0f) },
            new Trail { name = "ピンク",     price = 1200, a = new Color(1f, .5f, .8f, 1f),   b = new Color(.7f, .3f, 1f, 0f) },
            new Trail { name = "レインボー", price = 2500, a = new Color(1f, .3f, .3f, 1f),   b = new Color(.3f, .4f, 1f, 0f) },
        };

        // ミッション種別 0..: 人をはじく / コイン / スキル / クリア数 / ノーダメ / コンボ / 星3 / ボス
        public struct Mission { public string text; public int[] targets; public int reward; }
        public static readonly Mission[] Missions =
        {
            new Mission { text = "人を{0}人はじきとばす",     targets = new[] { 60, 120, 200 }, reward = 80 },
            new Mission { text = "コインを{0}枚あつめる",     targets = new[] { 80, 150, 250 }, reward = 90 },
            new Mission { text = "スキルを{0}回つかう",       targets = new[] { 6, 10, 16 },    reward = 70 },
            new Mission { text = "{0}レベルをクリアする",     targets = new[] { 2, 3, 5 },      reward = 100 },
            new Mission { text = "ノーダメージでクリアする",  targets = new[] { 1 },            reward = 120 },
            new Mission { text = "コンボ{0}をたっせいする",   targets = new[] { 20, 35, 50 },   reward = 90 },
            new Mission { text = "星3つでクリアする",         targets = new[] { 1 },            reward = 130 },
            new Mission { text = "ライバルに勝つ",            targets = new[] { 1 },            reward = 150 },
        };
        public const int MPlow = 0, MCoin = 1, MSkill = 2, MClear = 3, MNoHit = 4, MCombo = 5, MStar3 = 6, MRival = 7;

        public struct Achievement { public string id, name, desc; public int reward; public Func<SaveData, bool> test; }
        public static readonly Achievement[] Achievements =
        {
            new Achievement { id = "first",   name = "はじめの一歩",   desc = "はじめてクリア",              reward = 50,  test = d => d.totalWins >= 1 },
            new Achievement { id = "lv10",    name = "ベテラン",       desc = "レベル10に到達",              reward = 200, test = d => d.level >= 10 },
            new Achievement { id = "lv30",    name = "渋谷マスター",   desc = "レベル30に到達",              reward = 600, test = d => d.level >= 30 },
            new Achievement { id = "plow100", name = "人間ボウリング", desc = "合計100人をはじく",           reward = 150, test = d => d.totalPlowed >= 100 },
            new Achievement { id = "plow1k",  name = "ストライク王",   desc = "合計1,000人をはじく",         reward = 500, test = d => d.totalPlowed >= 1000 },
            new Achievement { id = "combo50", name = "コンボ職人",     desc = "コンボ50を出す",              reward = 200, test = d => d.maxCombo >= 50 },
            new Achievement { id = "rich",    name = "お金持ち",       desc = "合計3,000コインを稼ぐ",       reward = 300, test = d => d.totalCoins >= 3000 },
            new Achievement { id = "star10",  name = "星あつめ",       desc = "星3つクリアを10回",           reward = 300, test = d => d.threeStars >= 10 },
            new Achievement { id = "clean5",  name = "安全運転",       desc = "ノーダメージクリアを5回",     reward = 250, test = d => d.noHitWins >= 5 },
            new Achievement { id = "skills",  name = "スキル使い",     desc = "スキルを50回つかう",          reward = 150, test = d => d.skillUses >= 50 },
            new Achievement { id = "rival",   name = "ライバル撃破",   desc = "ライバルに3回勝つ",           reward = 300, test = d => d.rivalWins >= 3 },
            new Achievement { id = "style",   name = "おしゃれ",       desc = "スキンを3つ持つ",             reward = 150, test = d => d.ownSkin.Count >= 3 },
        };
    }
}
