using System.Collections.Generic;
using UnityEngine;

namespace ShibuyaRush
{
    /// <summary>
    /// 基本形(板・四角・円柱・球)を自前で作る。GameObject.CreatePrimitive は当たり判定(Collider)も付けるため、
    /// WebGLで物理モジュールが削られた環境では失敗する。見た目だけのこのゲームには当たり判定は要らない。
    /// </summary>
    public static class Prim
    {
        static readonly Dictionary<PrimitiveType, Mesh> cache = new Dictionary<PrimitiveType, Mesh>();

        public static GameObject Make(PrimitiveType type)
        {
            var go = new GameObject(type.ToString());
            go.AddComponent<MeshFilter>().sharedMesh = MeshOf(type);
            go.AddComponent<MeshRenderer>();
            return go;
        }

        public static Mesh MeshOf(PrimitiveType type)
        {
            if (cache.TryGetValue(type, out var m) && m != null) return m;
            switch (type)
            {
                case PrimitiveType.Plane: m = BuildPlane(); break;
                case PrimitiveType.Cylinder: m = BuildCylinder(); break;
                case PrimitiveType.Sphere: m = BuildSphere(); break;
                default: m = BuildQuad(); break;
            }
            m.name = type.ToString(); cache[type] = m; return m;
        }

        static Mesh BuildQuad()
        {   // 1x1、-Z側が表(Unityの標準Quadと同じ)
            var m = new Mesh();
            m.SetVertices(new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0) });
            m.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            m.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            m.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0); m.RecalculateBounds(); return m;
        }

        static Mesh BuildPlane()
        {   // 10x10、上向き(Unityの標準Planeと同じ大きさ)
            var m = new Mesh();
            m.SetVertices(new[] { new Vector3(-5, 0, -5), new Vector3(5, 0, -5), new Vector3(-5, 0, 5), new Vector3(5, 0, 5) });
            m.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            m.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            m.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0); m.RecalculateBounds(); return m;
        }

        static Mesh BuildCylinder()
        {   // 半径0.5・高さ2(Unityの標準Cylinderと同じ)
            const int N = 24; var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= N; i++)
            {
                float a = i / (float)N * Mathf.PI * 2f; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * .5f + Vector3.down); n.Add(d); uv.Add(new Vector2(i / (float)N, 0));
                v.Add(d * .5f + Vector3.up); n.Add(d); uv.Add(new Vector2(i / (float)N, 1));
            }
            for (int i = 0; i < N; i++) { int b = i * 2; t.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 }); }
            for (int cap = 0; cap < 2; cap++)
            {
                float y = cap == 0 ? -1f : 1f; var nn = cap == 0 ? Vector3.down : Vector3.up; int c = v.Count;
                v.Add(new Vector3(0, y, 0)); n.Add(nn); uv.Add(new Vector2(.5f, .5f));
                for (int i = 0; i <= N; i++) { float a = i / (float)N * Mathf.PI * 2f; v.Add(new Vector3(Mathf.Cos(a) * .5f, y, Mathf.Sin(a) * .5f)); n.Add(nn); uv.Add(new Vector2(.5f + Mathf.Cos(a) * .5f, .5f + Mathf.Sin(a) * .5f)); }
                for (int i = 0; i < N; i++) { if (cap == 0) t.AddRange(new[] { c, c + 1 + i, c + 2 + i }); else t.AddRange(new[] { c, c + 2 + i, c + 1 + i }); }
            }
            var m = new Mesh(); m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }

        static Mesh BuildSphere()
        {   // 半径0.5(Unityの標準Sphereと同じ)
            const int LON = 24, LAT = 16; var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int y = 0; y <= LAT; y++)
                for (int x = 0; x <= LON; x++)
                {
                    float th = y / (float)LAT * Mathf.PI, ph = x / (float)LON * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    v.Add(d * .5f); n.Add(d); uv.Add(new Vector2(x / (float)LON, 1f - y / (float)LAT));
                }
            for (int y = 0; y < LAT; y++)
                for (int x = 0; x < LON; x++)
                {
                    int a = y * (LON + 1) + x, b = a + LON + 1;
                    t.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            var m = new Mesh(); m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }
    }
}
