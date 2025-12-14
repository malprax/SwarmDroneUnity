using UnityEngine;

/// <summary>
/// Occupancy grid fixed-size:
/// 0=Unknown, 1=Free, 2=Occupied
/// </summary>
public class GridMap2D : MonoBehaviour
{
    public const byte Unknown = 0;
    public const byte Free = 1;
    public const byte Occupied = 2;

    [Header("Grid Settings (Fixed to Arena ~18x9)")]
    public float cellSize = 0.3f; // ✅ rekomendasi
    public int width = 60;        // 18 / 0.3 = 60
    public int height = 30;       // 9  / 0.3 = 30

    [Tooltip("World position of cell (0,0) bottom-left corner")]
    public Vector2 originWorld = new Vector2(-9f, -4.5f); // ✅ bottom-left arena

    [Header("Inflation (Safety)")]
    [Tooltip("Inflate occupied cells by N cells so path doesn't hug walls.")]
    [Range(0, 4)] public int inflateCells = 1;

    private byte[,] grid;
    private byte[,] inflatedGrid;

    public int Width => width;
    public int Height => height;
    public float CellSize => cellSize;

    private void Awake()
    {
        grid = new byte[width, height];
        inflatedGrid = new byte[width, height];

        // init unknown
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                grid[x, y] = Unknown;

        RebuildInflation();
    }

    public bool WorldToCell(Vector2 world, out Vector2Int cell)
    {
        int cx = Mathf.FloorToInt((world.x - originWorld.x) / cellSize);
        int cy = Mathf.FloorToInt((world.y - originWorld.y) / cellSize);
        cell = new Vector2Int(cx, cy);
        return InBounds(cell);
    }

    public Vector2 CellToWorldCenter(Vector2Int cell)
    {
        return new Vector2(
            originWorld.x + (cell.x + 0.5f) * cellSize,
            originWorld.y + (cell.y + 0.5f) * cellSize
        );
    }

    public bool InBounds(Vector2Int c)
    {
        return c.x >= 0 && c.x < width && c.y >= 0 && c.y < height;
    }

    public byte GetCell(Vector2Int c)
    {
        if (!InBounds(c)) return Occupied; // out of bounds dianggap dinding
        return grid[c.x, c.y];
    }

    public byte GetCellInflated(Vector2Int c)
    {
        if (!InBounds(c)) return Occupied;
        return inflatedGrid[c.x, c.y];
    }

    public void SetFree(Vector2Int c)
    {
        if (!InBounds(c)) return;
        if (grid[c.x, c.y] == Unknown) grid[c.x, c.y] = Free;
    }

    public void SetOccupied(Vector2Int c)
    {
        if (!InBounds(c)) return;
        if (grid[c.x, c.y] != Occupied)
        {
            grid[c.x, c.y] = Occupied;
        }
    }

    public void RebuildInflation()
    {
        // copy base -> inflated
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                inflatedGrid[x, y] = grid[x, y];

        if (inflateCells <= 0) return;

        // inflate occupied cells
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (grid[x, y] != Occupied) continue;

                for (int dx = -inflateCells; dx <= inflateCells; dx++)
                {
                    for (int dy = -inflateCells; dy <= inflateCells; dy++)
                    {
                        int nx = x + dx;
                        int ny = y + dy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                        inflatedGrid[nx, ny] = Occupied;
                    }
                }
            }
        }
    }

#if UNITY_EDITOR
    [Header("Debug Draw")]
    public bool drawDebug = false;
    public int debugDrawStep = 2;

    private void OnDrawGizmos()
    {
        if (!drawDebug) return;
        if (grid == null) return;

        float s = cellSize;
        for (int x = 0; x < width; x += Mathf.Max(1, debugDrawStep))
        {
            for (int y = 0; y < height; y += Mathf.Max(1, debugDrawStep))
            {
                byte v = grid[x, y];
                if (v == Unknown) continue;

                Vector2 c = CellToWorldCenter(new Vector2Int(x, y));
                Gizmos.color = (v == Occupied) ? Color.red : Color.green;
                Gizmos.DrawCube(c, new Vector3(s * 0.9f, s * 0.9f, 0.01f));
            }
        }
    }
#endif
}