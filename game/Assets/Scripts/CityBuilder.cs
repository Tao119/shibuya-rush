using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShibuyaRush
{
    /// <summary>city.json から地面・道路・建物のメッシュを作り、建物との衝突判定も提供する。</summary>
    public class CityBuilder
    {
        public const int PaletteSize = 6;
        const float Cell = 25f;
        readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
        readonly List<Vector2[]> footprints = new List<Vector2[]>();
        readonly List<Vector4> boxes = new List<Vector4>(); // minx,minz,maxx,maxz
        public int BuildingCount => footprints.Count;
        public System.Func<float, float, bool> Exempt; // 建物の下をくぐる道など、通れる場所

        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        public GameObject Build(CityData city, Material ground, Material road, Material walkway, Material[] palette)
        {
            var root = new GameObject("City");
            MakeGround(root.transform, ground);
            MakeRoads(root.transform, city, road, walkway);
            MakeBuildings(root.transform, city, palette);
            return root;
        }

        void MakeGround(Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "Ground"; go.transform.SetParent(parent);
            go.transform.localScale = new Vector3(400, 1, 400);
            go.transform.position = new Vector3(0, -0.05f, 0);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static GameObject MeshObject(Transform parent, string name, Mesh m, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(parent);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            return go;
        }

        void MakeRoads(Transform parent, CityData city, Material road, Material walkway)
        {
            var rv = new List<Vector3>(); var ri = new List<int>();
            var wv = new List<Vector3>(); var wi = new List<int>();
            foreach (var r in city.roads)
            {
                if (r.tunnel) continue;
                bool footway = r.cls == "footway" || r.cls == "path" || r.cls == "steps" || r.cls == "pedestrian" || r.cls == "cycleway";
                var v = footway ? wv : rv; var idx = footway ? wi : ri;
                float y = footway ? 0.04f : (r.bridge ? 0.03f : 0.02f);
                float half = r.w * 0.5f;
                for (int i = 0; i + 3 < r.p.Length; i += 2)
                {
                    var a = new Vector2(r.p[i], r.p[i + 1]); var b = new Vector2(r.p[i + 2], r.p[i + 3]);
                    var d = b - a; if (d.sqrMagnitude < 1e-4f) continue; d.Normalize();
                    var n = new Vector2(-d.y, d.x) * half;
                    int s = v.Count;
                    v.Add(new Vector3(a.x - n.x, y, a.y - n.y)); v.Add(new Vector3(a.x + n.x, y, a.y + n.y));
                    v.Add(new Vector3(b.x + n.x, y, b.y + n.y)); v.Add(new Vector3(b.x - n.x, y, b.y - n.y));
                    idx.Add(s); idx.Add(s + 1); idx.Add(s + 2); idx.Add(s); idx.Add(s + 2); idx.Add(s + 3);
                    // 継ぎ目を丸める簡易ジョイント(四角形の隙間を埋める)
                    AddDisc(v, idx, b, half, y);
                }
            }
            MeshObject(parent, "Roads", ToMesh(rv, ri), road);
            MeshObject(parent, "Walkways", ToMesh(wv, wi), walkway);
        }

        static void AddDisc(List<Vector3> v, List<int> idx, Vector2 c, float r, float y)
        {
            const int N = 8; int s = v.Count;
            v.Add(new Vector3(c.x, y, c.y));
            for (int i = 0; i < N; i++)
            {
                float a = i * Mathf.PI * 2f / N;
                v.Add(new Vector3(c.x + Mathf.Cos(a) * r, y, c.y + Mathf.Sin(a) * r));
            }
            for (int i = 0; i < N; i++) { idx.Add(s); idx.Add(s + 1 + (i + 1) % N); idx.Add(s + 1 + i); }
        }

        static Mesh ToMesh(List<Vector3> v, List<int> idx)
        {
            var m = new Mesh { indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetTriangles(idx, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        void MakeBuildings(Transform parent, CityData city, Material[] palette)
        {
            var verts = new List<Vector3>[palette.Length]; var tris = new List<int>[palette.Length];
            for (int i = 0; i < palette.Length; i++) { verts[i] = new List<Vector3>(); tris[i] = new List<int>(); }
            int bi = 0;
            foreach (var b in city.buildings)
            {
                int n = b.p.Length / 2; if (n < 3) continue;
                var poly = new Vector2[n];
                for (int i = 0; i < n; i++) poly[i] = new Vector2(b.p[i * 2], b.p[i * 2 + 1]);
                if (SignedArea(poly) < 0) System.Array.Reverse(poly); // 反時計回りに揃える
                int pal = Mathf.Abs((int)(poly[0].x * 7.13f + poly[0].y * 3.77f)) % palette.Length;
                Extrude(poly, b.h, verts[pal], tris[pal]);
                Register(poly);
                bi++;
            }
            for (int i = 0; i < palette.Length; i++)
            {
                if (verts[i].Count == 0) continue;
                MeshObject(parent, "Buildings" + i, ToMesh(verts[i], tris[i]), palette[i]);
            }
        }

        static float SignedArea(Vector2[] p)
        {
            float a = 0; for (int i = 0; i < p.Length; i++) { var q = p[(i + 1) % p.Length]; a += p[i].x * q.y - q.x * p[i].y; }
            return a * 0.5f;
        }

        static void Extrude(Vector2[] poly, float h, List<Vector3> v, List<int> t)
        {
            int n = poly.Length;
            for (int i = 0; i < n; i++) // 壁(頂点を共有しないのでフラットシェード)
            {
                var a = poly[i]; var b = poly[(i + 1) % n]; int s = v.Count;
                v.Add(new Vector3(a.x, 0, a.y)); v.Add(new Vector3(b.x, 0, b.y));
                v.Add(new Vector3(b.x, h, b.y)); v.Add(new Vector3(a.x, h, a.y));
                t.Add(s); t.Add(s + 3); t.Add(s + 2); t.Add(s); t.Add(s + 2); t.Add(s + 1);
            }
            int roof = v.Count;
            for (int i = 0; i < n; i++) v.Add(new Vector3(poly[i].x, h, poly[i].y));
            foreach (var tri in EarClip(poly)) { t.Add(roof + tri.x); t.Add(roof + tri.z); t.Add(roof + tri.y); }
        }

        /// <summary>反時計回りの単純多角形を耳切りで三角形分割する。</summary>
        public static List<Vector3Int> EarClip(Vector2[] p)
        {
            var res = new List<Vector3Int>(); int n = p.Length;
            var idx = new List<int>(n); for (int i = 0; i < n; i++) idx.Add(i);
            int guard = n * n + 10;
            while (idx.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int i0 = idx[(k + idx.Count - 1) % idx.Count], i1 = idx[k], i2 = idx[(k + 1) % idx.Count];
                    var a = p[i0]; var b = p[i1]; var c = p[i2];
                    if (Cross(b - a, c - b) <= 1e-6f) continue; // 凹頂点
                    bool inside = false;
                    foreach (int j in idx)
                    {
                        if (j == i0 || j == i1 || j == i2) continue;
                        if (PointInTri(p[j], a, b, c)) { inside = true; break; }
                    }
                    if (inside) continue;
                    res.Add(new Vector3Int(i0, i1, i2)); idx.RemoveAt(k); clipped = true; break;
                }
                if (!clipped) { // 退化: 最初の頂点で強制的に切る
                    res.Add(new Vector3Int(idx[0], idx[1], idx[2])); idx.RemoveAt(1);
                }
            }
            if (idx.Count == 3) res.Add(new Vector3Int(idx[0], idx[1], idx[2]));
            return res;
        }
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static bool PointInTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a), d2 = Cross(c - b, p - b), d3 = Cross(a - c, p - c);
            return d1 >= 0 && d2 >= 0 && d3 >= 0;
        }

        // ---- 衝突判定(建物の中に入れない) ----
        void Register(Vector2[] poly)
        {
            int id = footprints.Count; footprints.Add(poly);
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            foreach (var q in poly) { x0 = Mathf.Min(x0, q.x); z0 = Mathf.Min(z0, q.y); x1 = Mathf.Max(x1, q.x); z1 = Mathf.Max(z1, q.y); }
            boxes.Add(new Vector4(x0, z0, x1, z1));
            for (int cx = Mathf.FloorToInt(x0 / Cell); cx <= Mathf.FloorToInt(x1 / Cell); cx++)
                for (int cz = Mathf.FloorToInt(z0 / Cell); cz <= Mathf.FloorToInt(z1 / Cell); cz++)
                {
                    long k = Key(cx, cz);
                    if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
                    l.Add(id);
                }
        }

        /// <summary>点(x,z)を半径rで膨らませた円が建物に当たるか。</summary>
        public bool Blocked(float x, float z, float r = 0.35f)
        {
            if (!grid.TryGetValue(Key(Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell)), out var l)) return false;
            foreach (int id in l)
            {
                var bx = boxes[id];
                if (x < bx.x - r || x > bx.z + r || z < bx.y - r || z > bx.w + r) continue;
                var p = footprints[id]; bool hit = Inside(p, x, z);
                if (!hit) for (int i = 0; i < p.Length; i++) if (DistSeg(new Vector2(x, z), p[i], p[(i + 1) % p.Length]) < r) { hit = true; break; }
                if (hit && !(Exempt != null && Exempt(x, z))) return true;
            }
            return false;
        }

        static bool Inside(Vector2[] p, float x, float z)
        {
            bool c = false;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                if ((p[i].y > z) != (p[j].y > z) && x < (p[j].x - p[i].x) * (z - p[i].y) / (p[j].y - p[i].y) + p[i].x) c = !c;
            return c;
        }
        static float DistSeg(Vector2 q, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return (q - (a + ab * t)).magnitude;
        }
    }
}
