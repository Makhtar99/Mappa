using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-10000)]
public sealed class EntityField : MonoBehaviour
{
    public static EntityField Instance { get; private set; }

    public int[] Ids = new int[0];
    public Vector2[] Norm = new Vector2[0];
    public Vector3[] World = new Vector3[0];
    public Color32[] Colors = new Color32[0];

    public bool ClearEachFrame = true;

    private readonly Dictionary<int, int> _index = new Dictionary<int, int>();

    private void Awake() => Instance = this;

    private void Update()
    {
        if (ClearEachFrame) Clear();
    }

    public void Build(int[] ids, Vector2[] norm, Vector3[] world)
    {
        Ids = ids;
        Norm = norm;
        World = world;
        Colors = new Color32[ids.Length];
        _index.Clear();
        for (int i = 0; i < ids.Length; i++) _index[ids[i]] = i;
    }

    public void SetColor(int index, Color32 c)
    {
        if ((uint)index < (uint)Colors.Length) Colors[index] = c;
    }

    public void AddColor(int index, Color32 c)
    {
        if ((uint)index >= (uint)Colors.Length) return;
        Color32 cur = Colors[index];
        Colors[index] = new Color32(
            (byte)Mathf.Min(255, cur.r + c.r),
            (byte)Mathf.Min(255, cur.g + c.g),
            (byte)Mathf.Min(255, cur.b + c.b),
            (byte)Mathf.Min(255, cur.a + c.a));
    }

    public bool TryIndex(int id, out int index) => _index.TryGetValue(id, out index);

    public void Clear()
    {
        for (int i = 0; i < Colors.Length; i++) Colors[i] = new Color32(0, 0, 0, 0);
    }
}
