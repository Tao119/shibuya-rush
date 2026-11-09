using System;
using UnityEngine;

namespace ShibuyaRush
{
    [Serializable] public class RoadData { public string cls; public string name; public float w; public bool walk; public int sig; public bool bridge; public bool tunnel; public float[] p; }
    [Serializable] public class BuildingData { public float h; public string src; public string name; public float[] p; }
    [Serializable] public class PoiData { public long id; public string name; public float x; public float z; }
    [Serializable] public class CityData
    {
        public RoadData[] roads; public BuildingData[] buildings; public PoiData[] pois;
        public static CityData Load()
        {
            var ta = Resources.Load<TextAsset>("city");
            if (ta == null) throw new Exception("Resources/city.json がありません。tools/build_city.py を実行してください");
            return JsonUtility.FromJson<CityData>(ta.text);
        }
    }
}
