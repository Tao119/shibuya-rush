using System;
using UnityEngine;

namespace ShibuyaRush
{
    [Serializable] public class RoadData { public string cls; public string name; public float w; public bool walk; public int sig, cross; public float lift, l0, l1; public bool bridge; public bool tunnel; public float[] p; }
    [Serializable] public class BuildingData { public float h; public string src; public string name; public float[] p; }
    [Serializable] public class PoiData { public long id; public string name; public float x; public float z; }
    [Serializable] public class CityData
    {
        public RoadData[] roads; public BuildingData[] buildings; public PoiData[] pois;
        public static float PolyLen(float[] p)
        {
            float L = 0; for (int i = 0; i + 3 < p.Length; i += 2) L += Mathf.Sqrt((p[i + 2] - p[i]) * (p[i + 2] - p[i]) + (p[i + 3] - p[i + 1]) * (p[i + 3] - p[i + 1]));
            return L;
        }

        /// <summary>高架の道の、始点から距離sの位置での持ち上げ量[m]。両端が他の高架と接していなければ地面から滑らかに上る。</summary>
        public static float Lift(RoadData r, float s, float L)
        {
            if (r.lift <= 0f) return 0f;
            float R = Mathf.Max(1f, Mathf.Min(25f, L * 0.45f));
            float a = r.l0 < r.lift ? Mathf.SmoothStep(r.l0, r.lift, Mathf.Clamp01(s / R)) : r.lift;
            float b = r.l1 < r.lift ? Mathf.SmoothStep(r.l1, r.lift, Mathf.Clamp01((L - s) / R)) : r.lift;
            return Mathf.Min(a, b);
        }

        public static CityData Load()
        {
            var ta = Resources.Load<TextAsset>("city");
            if (ta == null) throw new Exception("Resources/city.json がありません。tools/build_city.py を実行してください");
            return JsonUtility.FromJson<CityData>(ta.text);
        }
    }
}
