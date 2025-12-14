using System.Collections.Generic;
using UnityEngine;

public static class FrontierPlanner
{
    private static readonly Vector2Int[] Neigh4 =
    {
        new Vector2Int(1,0), new Vector2Int(-1,0),
        new Vector2Int(0,1), new Vector2Int(0,-1),
    };

    public static bool TryGetNearestFrontier(GridMap2D map, Vector2Int start, out Vector2Int frontierCell)
    {
        frontierCell = start;
        if (!map.InBounds(start)) return false;

        // BFS dari posisi drone: cari frontier terdekat
        int w = map.Width;
        int h = map.Height;
        bool[] vis = new bool[w * h];
        var q = new Queue<Vector2Int>(2048);

        q.Enqueue(start);
        vis[start.y * w + start.x] = true;

        while (q.Count > 0)
        {
            Vector2Int c = q.Dequeue();

            if (IsFrontier(map, c))
            {
                frontierCell = c;
                return true;
            }

            for (int i = 0; i < 4; i++)
            {
                Vector2Int n = c + Neigh4[i];
                if (!map.InBounds(n)) continue;
                int idx = n.y * w + n.x;
                if (vis[idx]) continue;

                // jalan BFS hanya lewat Free
                if (map.GetCellInflated(n) != GridMap2D.Free) continue;

                vis[idx] = true;
                q.Enqueue(n);
            }
        }

        return false;
    }

    private static bool IsFrontier(GridMap2D map, Vector2Int c)
    {
        if (map.GetCellInflated(c) != GridMap2D.Free) return false;

        // frontier = Free yang bersentuhan dengan Unknown (pakai raw grid)
        for (int i = 0; i < 4; i++)
        {
            Vector2Int n = c + Neigh4[i];
            if (!map.InBounds(n)) continue;
            if (map.GetCell(n) == GridMap2D.Unknown)
                return true;
        }
        return false;
    }
}