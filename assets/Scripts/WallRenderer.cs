using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[DefaultExecutionOrder(9000)]
public sealed class WallRenderer : MonoBehaviour
{
    [Tooltip("Taille d'une LED (monde). ~1x l'espacement (0.0156) = LED jointives comme un vrai panneau.")]
    public float ledSize = 0.016f;
    [Tooltip("Nettete du disque : plus haut = bord plus net.")]
    public float edgeSharpness = 1.4f;
    [Tooltip("Rond (LED) si vrai, carre plein sinon.")]
    public bool roundLeds = true;

    private Mesh _mesh;
    private Color32[] _cols;
    private int _n;

    private void Start()
    {
        var f = EntityField.Instance;
        if (f == null) return;

        _n = f.World.Length;
        var verts = new Vector3[_n * 4];
        var uvs = new Vector2[_n * 4];
        var tris = new int[_n * 6];
        float h = ledSize * 0.5f;

        for (int i = 0; i < _n; i++)
        {
            Vector3 c = f.World[i];
            int v = i * 4;
            verts[v] = c + new Vector3(-h, -h, 0);
            verts[v + 1] = c + new Vector3(h, -h, 0);
            verts[v + 2] = c + new Vector3(h, h, 0);
            verts[v + 3] = c + new Vector3(-h, h, 0);
            uvs[v] = new Vector2(0, 0);
            uvs[v + 1] = new Vector2(1, 0);
            uvs[v + 2] = new Vector2(1, 1);
            uvs[v + 3] = new Vector2(0, 1);
            int t = i * 6;
            tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
            tris[t + 3] = v; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
        }

        _mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        _mesh.vertices = verts;
        _mesh.uv = uvs;
        _mesh.triangles = tris;
        _cols = new Color32[_n * 4];
        _mesh.colors32 = _cols;

        GetComponent<MeshFilter>().mesh = _mesh;

        var mat = new Material(Shader.Find("Sprites/Default")) { name = "WallLED" };
        mat.mainTexture = roundLeds ? BuildDotTexture(64) : Texture2D.whiteTexture;
        GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    private Texture2D BuildDotTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        float c = (size - 1) * 0.5f;
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / c;
                float dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((1f - d) * (size * 0.5f) * 0.15f * edgeSharpness);
                a = Mathf.Clamp01(a);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false);
        return tex;
    }

    private void LateUpdate()
    {
        var f = EntityField.Instance;
        if (f == null || _mesh == null) return;

        for (int i = 0; i < _n; i++)
        {
            Color32 c = f.Colors[i];
            c.a = 255;
            int v = i * 4;
            _cols[v] = c; _cols[v + 1] = c; _cols[v + 2] = c; _cols[v + 3] = c;
        }
        _mesh.colors32 = _cols;
    }
}
