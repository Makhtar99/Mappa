using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(EntityField))]
public sealed class WallBuilder : MonoBehaviour
{
    public int quarters = 4;
    public int stripsPerQuarter = 16;
    public int stripStride = 300;
    public int quarterOffset = 5000;
    public int baseId = 100;
    public int visiblePerHalf = 128;

    public float worldWidth = 2f;
    public float worldHeight = 2f;

    private void Start()
    {
        int ledsPerStrip = 2 * visiblePerHalf + 3;
        int totalCols = quarters * stripsPerQuarter * 2;
        int rows = visiblePerHalf;
        float colDen = Mathf.Max(1, totalCols - 1);
        float rowDen = Mathf.Max(1, rows - 1);

        var ids = new List<int>();
        var norm = new List<Vector2>();
        var world = new List<Vector3>();

        int g = 0;
        for (int q = 0; q < quarters; q++)
        {
            for (int s = 0; s < stripsPerQuarter; s++, g++)
            {
                int start = baseId + q * quarterOffset + s * stripStride;
                int upCol = g * 2;
                int downCol = g * 2 + 1;

                for (int k = 0; k < ledsPerStrip; k++)
                {
                    int id = start + k;
                    int col, row;
                    if (k >= 1 && k <= visiblePerHalf) { col = upCol; row = k - 1; }
                    else if (k >= visiblePerHalf + 2 && k <= 2 * visiblePerHalf + 1) { col = downCol; row = (2 * visiblePerHalf + 1) - k; }
                    else { col = upCol; row = 0; }

                    float nx = col / colDen;
                    float ny = row / rowDen;
                    ids.Add(id);
                    norm.Add(new Vector2(nx, ny));
                    world.Add(new Vector3((nx - 0.5f) * worldWidth, (ny - 0.5f) * worldHeight, 0f));
                }
            }
        }

        GetComponent<EntityField>().Build(ids.ToArray(), norm.ToArray(), world.ToArray());
        Debug.Log($"WallBuilder: {ids.Count} entites, {totalCols}x{rows}");
    }
}
