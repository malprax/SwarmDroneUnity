using System.Collections.Generic;
using UnityEngine;

public static class AStar2D
{
    private struct Node
    {
        public int x, y;
        public int g;
        public int f;
        public int parentIndex;
    }

    private static readonly Vector2Int[] Neigh8 =
    {
        new Vector2Int(1,0), new Vector2Int(-1,0),
        new Vector2Int(0,1), new Vector2Int(0,-1),
        new Vector2Int(1,1), new Vector2Int(1,-1),
        new Vector2Int(-1,1), new Vector2Int(-1,-1),
    };

    private static int Heu(Vector2Int a, Vector2Int b)
    {
        // Octile distance
        int dx = Mathf.Abs(a.x - b.x);
        int dy = Mathf.Abs(a.y - b.y);
        int dmin = Mathf.Min(dx, dy);
        int dmax = Mathf.Max(dx, dy);
        return 14 * dmin + 10 * (dmax - dmin);
    }

    public static bool FindPath(GridMap2D map, Vector2Int start, Vector2Int goal, List<Vector2Int> outPath)
    {
        outPath.Clear();
        if (!map.InBounds(start) || !map.InBounds(goal)) return false;
        if (map.GetCellInflated(start) == GridMap2D.Occupied) return false;
        if (map.GetCellInflated(goal) == GridMap2D.Occupied) return false;

        int w = map.Width;
        int h = map.Height;

        // arrays
        int[] bestG = new int[w * h];
        int[] parent = new int[w * h];
        bool[] closed = new bool[w * h];

        const int INF = 1_000_000_000;
        for (int i = 0; i < bestG.Length; i++) { bestG[i] = INF; parent[i] = -1; closed[i] = false; }

        // simple binary heap using SortedSet-like workaround (fast enough for 120x120)
        var open = new List<int>(2048); // store indices
        int sIdx = start.y * w + start.x;
        int gIdx = goal.y * w + goal.x;

        bestG[sIdx] = 0;
        open.Add(sIdx);

        while (open.Count > 0)
        {
            // pick best f (linear scan ok for medium grid)
            int bestIndex = 0;
            int bestF = INF;
            for (int i = 0; i < open.Count; i++)
            {
                int idx = open[i];
                int cx = idx % w;
                int cy = idx / w;
                int g = bestG[idx];
                int f = g + Heu(new Vector2Int(cx, cy), goal);
                if (f < bestF) { bestF = f; bestIndex = i; }
            }

            int cur = open[bestIndex];
            open.RemoveAt(bestIndex);

            if (cur == gIdx) break;
            if (closed[cur]) continue;
            closed[cur] = true;

            int x = cur % w;
            int y = cur / w;

            for (int k = 0; k < Neigh8.Length; k++)
            {
                int nx = x + Neigh8[k].x;
                int ny = y + Neigh8[k].y;
                var nc = new Vector2Int(nx, ny);
                if (!map.InBounds(nc)) continue;
                int nIdx = ny * w + nx;
                if (closed[nIdx]) continue;

                // Only traverse Free cells (Unknown dianggap belum aman, jadi jangan)
                byte cell = map.GetCellInflated(nc);
                if (cell != GridMap2D.Free) continue;

                int stepCost = (k < 4) ? 10 : 14;
                int ng = bestG[cur] + stepCost;

                if (ng < bestG[nIdx])
                {
                    bestG[nIdx] = ng;
                    parent[nIdx] = cur;
                    open.Add(nIdx);
                }
            }
        }

        if (parent[gIdx] == -1 && gIdx != sIdx) return false;

        // rebuild
        int walk = gIdx;
        outPath.Add(new Vector2Int(walk % w, walk / w));
        while (walk != sIdx && walk != -1)
        {
            walk = parent[walk];
            if (walk == -1) break;
            outPath.Add(new Vector2Int(walk % w, walk / w));
        }
        outPath.Reverse();
        return outPath.Count > 0;
    }
}