using UnityEngine;
using System.Collections.Generic;

public enum CardinalDir { North = 0, East = 1, South = 2, West = 3 }

[System.Serializable]
public class GridCell
{
    // dinding di sekitar cell (N,E,S,W)
    public bool[] walls = new bool[4];
    public bool visited;
}

public class GridMap2D : MonoBehaviour
{
    [Header("Grid Size (jumlah cell)")]
    public int width  = 32;   // jumlah cell X
    public int height = 32;   // jumlah cell Y

    [Header("World ↔ Grid")]
    public Vector2 worldOrigin = Vector2.zero; // posisi world untuk (0,0)
    public float cellSize = 1.0f;

    GridCell[,] cells;

    void Awake()
    {
        cells = new GridCell[width, height];
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            cells[x, y] = new GridCell();
    }

    // --------------------------------------------------
    // KONVERSI POSISI WORLD → INDEX GRID
    // --------------------------------------------------
    public bool WorldToCell(Vector2 worldPos, out int cx, out int cy)
    {
        Vector2 local = worldPos - worldOrigin;
        cx = Mathf.FloorToInt(local.x / cellSize);
        cy = Mathf.FloorToInt(local.y / cellSize);

        return (cx >= 0 && cx < width && cy >= 0 && cy < height);
    }

    public Vector2 CellToWorldCenter(int cx, int cy)
    {
        return worldOrigin + new Vector2(
            (cx + 0.5f) * cellSize,
            (cy + 0.5f) * cellSize
        );
    }

    public GridCell GetCell(int cx, int cy)
    {
        if (cx < 0 || cx >= width || cy < 0 || cy >= height) return null;
        return cells[cx, cy];
    }

    // --------------------------------------------------
    // SET WALL (otomatis set tetangga juga)
    // --------------------------------------------------
    public void SetWall(int cx, int cy, CardinalDir dir, bool present)
    {
        GridCell c = GetCell(cx, cy);
        if (c == null) return;

        int d = (int)dir;
        c.walls[d] = present;

        // tetangga
        int nx = cx, ny = cy;
        CardinalDir opposite = CardinalDir.North;

        switch (dir)
        {
            case CardinalDir.North: ny += 1; opposite = CardinalDir.South; break;
            case CardinalDir.East:  nx += 1; opposite = CardinalDir.West;  break;
            case CardinalDir.South: ny -= 1; opposite = CardinalDir.North; break;
            case CardinalDir.West:  nx -= 1; opposite = CardinalDir.East;  break;
        }

        GridCell n = GetCell(nx, ny);
        if (n != null)
            n.walls[(int)opposite] = present;
    }

    // --------------------------------------------------
    // Cek boleh jalan dari (cx,cy) ke arah dir ?
    // --------------------------------------------------
    public bool CanMove(int cx, int cy, CardinalDir dir)
    {
        GridCell c = GetCell(cx, cy);
        if (c == null) return false;

        // Kalau di cell ini ada dinding di arah tsb → tidak boleh lewat
        if (c.walls[(int)dir]) return false;

        int nx = cx, ny = cy;
        switch (dir)
        {
            case CardinalDir.North: ny += 1; break;
            case CardinalDir.East:  nx += 1; break;
            case CardinalDir.South: ny -= 1; break;
            case CardinalDir.West:  nx -= 1; break;
        }

        // Batas grid
        if (nx < 0 || nx >= width || ny < 0 || ny >= height) return false;

        return true;
    }

    // --------------------------------------------------
    // Ambil neighbor yang bisa dicapai dari cell
    // --------------------------------------------------
    public IEnumerable<Vector2Int> GetNeighbors(Vector2Int cell)
    {
        int cx = cell.x;
        int cy = cell.y;

        foreach (CardinalDir d in System.Enum.GetValues(typeof(CardinalDir)))
        {
            if (!CanMove(cx, cy, d)) continue;

            int nx = cx, ny = cy;
            switch (d)
            {
                case CardinalDir.North: ny += 1; break;
                case CardinalDir.East:  nx += 1; break;
                case CardinalDir.South: ny -= 1; break;
                case CardinalDir.West:  nx -= 1; break;
            }

            yield return new Vector2Int(nx, ny);
        }
    }

    // --------------------------------------------------
    // BFS: cari path dari start → goal (dalam koordinat cell)
    // --------------------------------------------------
    public List<Vector2Int> FindPath(Vector2Int start, Vector2Int goal)
    {
        // Safety
        if (GetCell(start.x, start.y) == null ||
            GetCell(goal.x,  goal.y)  == null)
        {
            Debug.LogWarning($"[GridMap2D] FindPath invalid start/goal: {start} -> {goal}");
            return null;
        }

        Queue<Vector2Int> q = new Queue<Vector2Int>();
        q.Enqueue(start);

        Dictionary<Vector2Int, Vector2Int> parent =
            new Dictionary<Vector2Int, Vector2Int>();

        parent[start] = start;

        bool found = false;

        while (q.Count > 0)
        {
            Vector2Int cur = q.Dequeue();

            if (cur == goal)
            {
                found = true;
                break;
            }

            foreach (var nb in GetNeighbors(cur))
            {
                if (parent.ContainsKey(nb)) continue; // sudah dikunjungi
                parent[nb] = cur;
                q.Enqueue(nb);
            }
        }

        if (!found)
        {
            Debug.LogWarning($"[GridMap2D] No path found {start} -> {goal}");
            return null;
        }

        // Rekonstruksi path dari goal → start
        List<Vector2Int> path = new List<Vector2Int>();
        Vector2Int node = goal;
        while (true)
        {
            path.Add(node);
            if (node == start) break;
            node = parent[node];
        }

        path.Reverse(); // jadi start → goal
        return path;
    }

#if UNITY_EDITOR
    // --------------------------------------------------
    // Debug tampilan grid + dinding di SceneView
    // --------------------------------------------------
    void OnDrawGizmos()
    {
        if (cells == null) return;

        float half = cellSize * 0.5f;

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            GridCell c = cells[x, y];
            if (c == null) continue;

            Vector2 center = CellToWorldCenter(x, y);

            // kotak cell
            Gizmos.color = new Color(1f, 1f, 1f, 0.05f);
            Gizmos.DrawWireCube(center, Vector3.one * cellSize);

            Gizmos.color = Color.red;

            if (c.walls[(int)CardinalDir.North])
                Gizmos.DrawLine(center + new Vector2(-half, +half), center + new Vector2(+half, +half));

            if (c.walls[(int)CardinalDir.South])
                Gizmos.DrawLine(center + new Vector2(-half, -half), center + new Vector2(+half, -half));

            if (c.walls[(int)CardinalDir.East])
                Gizmos.DrawLine(center + new Vector2(+half, -half), center + new Vector2(+half, +half));

            if (c.walls[(int)CardinalDir.West])
                Gizmos.DrawLine(center + new Vector2(-half, -half), center + new Vector2(-half, +half));
        }
    }
#endif
}