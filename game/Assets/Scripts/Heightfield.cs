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
            return Mathf.Lerp(Mathf.Lerp(At(i, j), At(i + 1, j), ax), Mathf.Lerp(At(i, j + 1), At(i + 1, j + 1), ax), az);
        }
    }
}
