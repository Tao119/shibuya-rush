using UnityEngine;

namespace ShibuyaRush
{
    /// <summary>国土地理院DEMから作った標高格子(ハチ公像を0とした相対標高)。無ければ平坦。</summary>
    public class Heightfield
    {
        public static Heightfield Current = new Heightfield();
        public readonly float minX, minZ, step; public readonly int nx, nz; readonly float[] h;
        public bool Loaded => h != null;

        Heightfield() { }
        Heightfield(byte[] b)
        {
            minX = System.BitConverter.ToSingle(b, 0); minZ = System.BitConverter.ToSingle(b, 4); step = System.BitConverter.ToSingle(b, 8);
            nx = System.BitConverter.ToInt32(b, 12); nz = System.BitConverter.ToInt32(b, 16);
            h = new float[nx * nz]; System.Buffer.BlockCopy(b, 20, h, 0, nx * nz * 4);
        }

        public static void Load()
        {
            var ta = Resources.Load<TextAsset>("terrain");
            Current = ta != null ? new Heightfield(ta.bytes) : new Heightfield();
        }

        public float At(int i, int j) => h[Mathf.Clamp(j, 0, nz - 1) * nx + Mathf.Clamp(i, 0, nx - 1)];

        public float Height(float x, float z)
        {
            if (h == null) return 0f;
            float fx = (x - minX) / step, fz = (z - minZ) / step;
            int i = Mathf.FloorToInt(fx), j = Mathf.FloorToInt(fz); float ax = fx - i, az = fz - j;
            // 描かれる地面の三角形(各マスを(i+1,j)-(i,j+1)の対角線で2分割)と同じ補間にする。物が地面に埋まったり浮いたりしないように
            float h00 = At(i, j), h10 = At(i + 1, j), h01 = At(i, j + 1), h11 = At(i + 1, j + 1);
            return ax + az <= 1f ? h00 + ax * (h10 - h00) + az * (h01 - h00) : h11 + (1f - ax) * (h01 - h11) + (1f - az) * (h10 - h11);
        }

        /// <summary>(x,z)の地面の傾き(高さの変化/距離)。崖や急な斜面は大きい値になる。</summary>
        public float Slope(float x, float z)
        {
            if (h == null) return 0f;
            float dx = (Height(x + 1f, z) - Height(x - 1f, z)) * 0.5f, dz = (Height(x, z + 1f) - Height(x, z - 1f)) * 0.5f;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
