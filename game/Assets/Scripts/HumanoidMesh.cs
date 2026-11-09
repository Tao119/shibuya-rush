using UnityEngine;

namespace ShibuyaRush
{
    /// <summary>デフォルメ人形のパーツ。単色の箱と球を、関節(肩・腰)を回転軸にして動かす。</summary>
    public static class HumanoidMesh
    {
        public static Mesh Box(Vector3 size, bool pivotTop)
        {
            var m = new Mesh { name = "Box" };
            float hx = size.x / 2, hz = size.z / 2;
            float y0 = pivotTop ? -size.y : 0, y1 = pivotTop ? 0 : size.y;
            var c = new[] {
                new Vector3(-hx,y0,-hz), new Vector3(hx,y0,-hz), new Vector3(hx,y0,hz), new Vector3(-hx,y0,hz),
                new Vector3(-hx,y1,-hz), new Vector3(hx,y1,-hz), new Vector3(hx,y1,hz), new Vector3(-hx,y1,hz) };
            int[][] faces = { new[]{0,1,2,3}, new[]{4,7,6,5}, new[]{0,4,5,1}, new[]{1,5,6,2}, new[]{2,6,7,3}, new[]{3,7,4,0} };
            var v = new System.Collections.Generic.List<Vector3>(); var t = new System.Collections.Generic.List<int>();
            foreach (var f in faces)
            {
                int s = v.Count; foreach (int i in f) v.Add(c[i]);
                t.Add(s); t.Add(s + 2); t.Add(s + 1); t.Add(s); t.Add(s + 3); t.Add(s + 2);
            }
            // 面の向きが逆の場合に備えて法線から判定
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals();
            var n = m.normals; var cen = new Vector3(0, (y0 + y1) / 2, 0);
            if (Vector3.Dot(n[0], v[0] - cen) < 0) { t.Reverse(); m.SetTriangles(t, 0); m.RecalculateNormals(); }
            m.RecalculateBounds(); return m;
        }

        /// <summary>カプセル(丸みのある棒)。pivotTop=trueなら上端が原点で下へ伸び、falseなら下端が原点で上へ伸びる。</summary>
        public static Mesh Capsule(float radius, float height, bool pivotTop, int seg = 12, int ring = 4)
        {
            float hc = Mathf.Max(0f, height - 2f * radius);
            var prof = new System.Collections.Generic.List<(float y, float r, float ny, float nr)>();
            for (int i = 0; i <= ring; i++) { float a = -Mathf.PI / 2 + (Mathf.PI / 2) * i / ring; prof.Add((radius + radius * Mathf.Sin(a), radius * Mathf.Cos(a), Mathf.Sin(a), Mathf.Cos(a))); }
            for (int i = 0; i <= ring; i++) { float a = (Mathf.PI / 2) * i / ring; prof.Add((radius + hc + radius * Mathf.Sin(a), radius * Mathf.Cos(a), Mathf.Sin(a), Mathf.Cos(a))); }
            var v = new System.Collections.Generic.List<Vector3>(); var n = new System.Collections.Generic.List<Vector3>(); var t = new System.Collections.Generic.List<int>();
            float off = pivotTop ? -height : 0f;
            foreach (var q in prof)
                for (int j = 0; j <= seg; j++)
                {
                    float ph = 2f * Mathf.PI * j / seg; float cx = Mathf.Cos(ph), sz = Mathf.Sin(ph);
                    v.Add(new Vector3(cx * q.r, q.y + off, sz * q.r)); n.Add(new Vector3(cx * q.nr, q.ny, sz * q.nr).normalized);
                }
            for (int i = 0; i + 1 < prof.Count; i++)
                for (int j = 0; j < seg; j++)
                {
                    int a = i * (seg + 1) + j, b = a + 1, c = a + (seg + 1), d = c + 1;
                    t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d);
                }
            var m = new Mesh { name = "Capsule" }; m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }

        public static Mesh Sphere(float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var src = go.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(go);
            var m = Object.Instantiate(src); var v = m.vertices;
            for (int i = 0; i < v.Length; i++) v[i] *= radius * 2f;
            m.vertices = v; m.RecalculateBounds(); return m;
        }
    }
}
