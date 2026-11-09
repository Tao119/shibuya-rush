using System.Collections.Generic;

namespace ShibuyaRush
{
    /// <summary>最小ヒープ(優先度付きキュー)。.NET Standard 2.1 には PriorityQueue が無いため自前。</summary>
    public class PriorityQueue<T, P> where P : System.IComparable<P>
    {
        readonly List<(T item, P pri)> h = new List<(T, P)>();
        public int Count => h.Count;
        public void Enqueue(T item, P pri)
        {
            h.Add((item, pri)); int i = h.Count - 1;
            while (i > 0) { int p = (i - 1) / 2; if (h[p].pri.CompareTo(h[i].pri) <= 0) break; (h[p], h[i]) = (h[i], h[p]); i = p; }
        }
        public bool TryDequeue(out T item, out P pri)
        {
            if (h.Count == 0) { item = default; pri = default; return false; }
            item = h[0].item; pri = h[0].pri; var last = h[h.Count - 1]; h.RemoveAt(h.Count - 1);
            if (h.Count > 0)
            {
                h[0] = last; int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < h.Count && h[l].pri.CompareTo(h[m].pri) < 0) m = l;
                    if (r < h.Count && h[r].pri.CompareTo(h[m].pri) < 0) m = r;
                    if (m == i) break; (h[m], h[i]) = (h[i], h[m]); i = m;
                }
            }
            return true;
        }
    }
}
