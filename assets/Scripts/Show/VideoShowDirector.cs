using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(9000)]
public sealed class VideoShowDirector : MonoBehaviour
{
    public int resolution = 128;
    public float gain = 255f;
    public float bandGain = 320f;
    public int seed = -1;
    public float spinScale = 1f;

    private WallCanvas _c;
    private int _res;
    private float _wS;

    private readonly float[] _spec = new float[1024];
    private float _bass, _lowMid, _mid, _treble;
    private float _smBass, _smLowMid, _smMid, _smTreble;
    private float _bassAvg, _bassSlow, _beatCD;

    private float _t, _rot, _rotDir = 1f, _pump, _flash, _shake, _sparkle, _zoom = 1f;

    private int _scene = -1, _reqScene = -1, _lastScene = -1;
    private float _reqTime = -99f, _reqWeight = 1f;

    private float _ar, _ag, _ab, _br, _bg, _bb;
    private static readonly float[][] Palettes =
    {
        new[] { 1f, 0.176f, 0.118f, 1f, 1f, 1f },
        new[] { 1f, 0.118f, 0.353f, 1f, 0.863f, 0.353f },
        new[] { 1f, 1f, 1f, 1f, 0.118f, 0.078f },
        new[] { 1f, 0.471f, 0.078f, 1f, 0.157f, 0.706f },
        new[] { 1f, 0.078f, 0.235f, 0.471f, 0.784f, 1f },
    };

    private struct Particle { public float x, y, vx, vy, life; public float r, g, b; }
    private struct Shock { public float r, life; }
    private readonly List<Particle> _parts = new List<Particle>();
    private readonly List<Shock> _shocks = new List<Shock>();

    private bool _snakeReady;
    private float _snakeX, _snakeY, _snakeAng;
    private readonly List<Vector2> _snakeTrail = new List<Vector2>();

    private void Awake()
    {
        foreach (var r in FindObjectsByType<AudioReactive>(FindObjectsSortMode.None)) r.enabled = false;
        foreach (var r in FindObjectsByType<IlluminatorRig>(FindObjectsSortMode.None)) r.enabled = false;
    }

    private void Start()
    {
        _res = resolution;
        _wS = _res / 900f;
        _c = new WallCanvas(_res);
        if (seed >= 0) Random.InitState(seed);
        PickPalette();
    }

    private void Update()
    {
        var f = EntityField.Instance;
        if (f == null || _c == null) return;

        float dt = Mathf.Min(0.05f, Time.deltaTime);
        _t += dt;

        Analyse();
        DetectKick(dt);
        UpdateGlobals(dt);

        bool on = Time.time - _reqTime < 0.1f;
        int active = on ? _reqScene : -1;
        float bright = on ? Mathf.Clamp01(_reqWeight) : 0f;
        if (active != _lastScene) { if (active >= 0) Cut(); _lastScene = active; }
        _scene = active;

        bool longTrail = _scene == 1 || _scene == 3 || _scene == 4 || _scene == 8 || _scene == 10 || _scene == 13;
        _c.Fade(1f - (longTrail ? 0.16f : 0.32f));

        BgRays();
        if (_scene >= 0) DrawScene(dt);
        UpdateShocks(dt);
        UpdateParticles(dt);

        _c.BlitToField(f, gain * bright, _rot, _zoom, ShakeX(), ShakeY());
    }

    private float ShakeX() => (Mathf.PerlinNoise(_t * 40f, 0f) - 0.5f) * 2f * _shake * _wS;
    private float ShakeY() => (Mathf.PerlinNoise(0f, _t * 40f) - 0.5f) * 2f * _shake * _wS;

    private void Analyse()
    {
        AudioListener.GetSpectrumData(_spec, 0, FFTWindow.Blackman);
        _bass = Band(1, 14);
        _lowMid = Band(14, 44);
        _mid = Band(44, 110);
        _treble = Band(110, 300);
        _smBass += (_bass - _smBass) * 0.35f;
        _smLowMid += (_lowMid - _smLowMid) * 0.3f;
        _smMid += (_mid - _smMid) * 0.3f;
        _smTreble += (_treble - _smTreble) * 0.3f;
    }

    private float Band(int a, int b)
    {
        float s = 0f;
        for (int i = a; i < b && i < _spec.Length; i++) s += _spec[i];
        return Mathf.Clamp01(s / (b - a) * bandGain);
    }

    private float Freq(int bin)
    {
        if (bin < 0) bin = 0; if (bin >= _spec.Length) bin = _spec.Length - 1;
        return Mathf.Clamp01(_spec[bin] * bandGain);
    }

    private void DetectKick(float dt)
    {
        _beatCD -= dt;
        _bassAvg = _bassAvg * 0.90f + _bass * 0.10f;
        _bassSlow = _bassSlow * 0.995f + _bass * 0.005f;
        float thresh = Mathf.Max(_bassAvg * 1.22f, _bassSlow * 1.15f, 0.16f);
        if (_bass > thresh && _beatCD <= 0f)
        {
            _beatCD = 0.14f;
            float power = Mathf.Min(1f, _bass / Mathf.Max(0.001f, thresh) - 1f + 0.3f);
            _pump = Mathf.Min(1.4f, _pump + 0.6f + power * 0.7f);
            _flash = Mathf.Max(_flash, 0.3f + power * 0.55f);
            _shake = Mathf.Max(_shake, 6f + power * 16f);
            Burst(14 + power * 30f);
        }
    }

    private void UpdateGlobals(float dt)
    {
        _rot += dt * (0.25f + _smMid * 1.8f + _pump * 2.2f) * _rotDir * spinScale;
        _pump *= Mathf.Pow(0.0025f, dt);
        _sparkle *= Mathf.Pow(0.02f, dt);
        _flash *= Mathf.Pow(0.8f, dt * 60f);
        _shake *= Mathf.Pow(0.86f, dt * 60f);
        _zoom = 1f + _pump * 0.09f;

        if (_flash > 0.01f)
        {
            float k = _flash * 0.5f * 255f;
            for (int y = 0; y < _res; y++)
                for (int x = 0; x < _res; x++)
                    _c.AddPixel(x, y, k / gain, k / gain, k / gain);
        }
    }

    public void Request(int scene, float weight)
    {
        _reqScene = scene;
        _reqTime = Time.time;
        _reqWeight = weight;
    }

    private void Cut()
    {
        _rotDir = Random.value < 0.5f ? -1f : 1f;
        _flash = Mathf.Max(_flash, 0.95f);
        _shake = Mathf.Max(_shake, 18f);
        PickPalette();
        _shocks.Add(new Shock { r = 0f, life = 1f });
        Burst(24f);
        _snakeReady = false;
    }

    private void PickPalette()
    {
        var p = Palettes[Random.Range(0, Palettes.Length)];
        _ar = p[0]; _ag = p[1]; _ab = p[2]; _br = p[3]; _bg = p[4]; _bb = p[5];
    }

    private void ColOf(float v, out float r, out float g, out float b)
    {
        if (v > 0.6f) { r = _br; g = _bg; b = _bb; }
        else { r = _ar; g = _ag; b = _ab; }
    }

    private void BgRays()
    {
        float c = _res * 0.5f;
        for (int i = 0; i < 16; i++)
        {
            float a = i / 16f * Mathf.PI * 2f + _rot * 0.4f;
            float alpha = 0.02f + _bass * 0.06f;
            _c.Line(c, c, c + Mathf.Cos(a) * _res, c + Mathf.Sin(a) * _res, 1.5f, _ar, _ag, _ab, alpha);
        }
    }

    private void DrawScene(float dt)
    {
        switch (_scene)
        {
            case 0: Beams(); break;
            case 1: Tunnel(); break;
            case 2: Lasers(); break;
            case 3: Starburst(); break;
            case 4: Kaleido(); break;
            case 5: Grid(); break;
            case 6: Wave(); break;
            case 7: Bars(); break;
            case 8: Spiral(); break;
            case 9: Orbit(); break;
            case 10: Vortex(); break;
            case 11: Strobe(); break;
            case 12: Rings(); break;
            default: Snake(dt); break;
        }
    }

    private float Eng() => Mathf.Clamp01(_smBass * 0.7f + _pump * 0.5f);

    private void Ring()
    {
        float c = _res * 0.5f;
        float R0 = _res * 0.15f * (1f + _bass * 0.25f);
        for (int i = 0; i < 100; i++)
        {
            float a = i / 100f * Mathf.PI * 2f + _t * 0.15f;
            float v = Freq(2 + i);
            float len = 8f * _wS + v * _res * 0.20f;
            float x0 = c + Mathf.Cos(a) * R0, y0 = c + Mathf.Sin(a) * R0;
            float x1 = c + Mathf.Cos(a) * (R0 + len), y1 = c + Mathf.Sin(a) * (R0 + len);
            ColOf(v, out float cr, out float cg, out float cb);
            _c.Line(x0, y0, x1, y1, Mathf.Max(1f, _res * 0.004f), cr, cg, cb, 0.7f);
        }
    }

    private void Beams()
    {
        float eng = Eng();
        for (int i = 0; i < 9; i++)
        {
            float ox = (0.06f + 0.88f * i / 8f) * _res;
            float oy = -0.03f * _res;
            float ang = (i - 4) * 0.14f + Mathf.Sin(_t * (0.6f + i * 0.13f) + i) * 0.30f + Mathf.Sin(_t * 0.27f + i * 2f) * 0.14f;
            float len = 1.15f * _res;
            float ex = ox + Mathf.Sin(ang) * len, ey = oy + Mathf.Cos(ang) * len;
            float w = (46f + eng * 150f) * _wS;
            bool white = i % 3 == 1;
            float cr = white ? _br : _ar, cg = white ? _bg : _ag, cb = white ? _bb : _ab;
            _c.Line(ox, oy, ex, ey, Mathf.Max(1f, w), cr, cg, cb, 0.25f);
        }
        Ring();
    }

    private void Tunnel()
    {
        float c = _res * 0.5f;
        float maxR = _res * 1.414f * 0.6f;
        float speed = (120f + _bass * 520f) * _wS;
        for (int y = 0; y < _res; y++)
        {
            for (int x = 0; x < _res; x++)
            {
                float dx = x - c, dy = y - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx) - _rot;
                float best = 0f;
                for (int k = 0; k < 16; k++)
                {
                    float ringR = Mathf.Repeat(_t * speed * 0.6f + k * maxR / 16f, maxR);
                    float sector = Mathf.PI / 3f;
                    float aa = Mathf.Repeat(ang + sector * 0.5f, sector) - sector * 0.5f;
                    float polyR = ringR * Mathf.Cos(Mathf.PI / 6f) / Mathf.Cos(aa);
                    float a = 1f - ringR / maxR;
                    float w = 2f + a * a * 10f * (0.5f + _bass);
                    float e = 1f - Mathf.Clamp01(Mathf.Abs(r - polyR) / w);
                    if (e > best) best = e;
                }
                if (best <= 0.02f) continue;
                ColOf(best, out float cr, out float cg, out float cb);
                _c.AddPixel(x, y, cr * best * 0.8f, cg * best * 0.8f, cb * best * 0.8f);
            }
        }
    }

    private void Lasers()
    {
        float c = _res * 0.5f;
        float eng = Eng();
        Vector2[] em = { new(0, 0), new(_res, 0), new(0, _res), new(_res, _res), new(c, -20f * _wS) };
        for (int e = 0; e < em.Length; e++)
        {
            float baseAng = Mathf.Atan2(c - em[e].y, c - em[e].x);
            for (int i = 0; i < 6; i++)
            {
                float ang = baseAng + Mathf.Sin(_t * (1.1f + e * 0.3f) + i * 0.7f) * 0.9f;
                float len = _res * 1.414f;
                float ex = em[e].x + Mathf.Cos(ang) * len, ey = em[e].y + Mathf.Sin(ang) * len;
                _c.Line(em[e].x, em[e].y, ex, ey, 1.5f + eng * 3.5f, _ar, _ag, _ab, 0.35f);
            }
        }
    }

    private void Starburst()
    {
        float c = _res * 0.5f;
        for (int i = 0; i < 120; i++)
        {
            float a = i / 120f * Mathf.PI * 2f + _rot * 1.6f;
            float v = Freq(2 + i % 250);
            float len = 20f * _wS + v * _res * 0.46f;
            ColOf(v, out float cr, out float cg, out float cb);
            _c.Line(c + Mathf.Cos(a) * 18f * _wS, c + Mathf.Sin(a) * 18f * _wS,
                    c + Mathf.Cos(a) * (18f * _wS + len), c + Mathf.Sin(a) * (18f * _wS + len),
                    1.5f, cr, cg, cb, 0.7f);
        }
        Ring();
    }

    private void Kaleido()
    {
        float c = _res * 0.5f;
        for (int seg = 0; seg < 8; seg++)
        {
            float mirror = seg % 2 == 0 ? 1f : -1f;
            for (int s = 0; s < 18; s++)
            {
                float v = Freq(2 + s * 6);
                float a = seg / 8f * Mathf.PI * 2f + _rot + s * 0.05f * mirror;
                float rr = 30f * _wS + v * _res * 0.5f;
                ColOf(v, out float cr, out float cg, out float cb);
                _c.Dot(c + Mathf.Cos(a) * rr, c + Mathf.Sin(a) * rr, 1.5f + v * 3f, cr, cg, cb, 0.6f);
            }
        }
    }

    private void Grid()
    {
        int cols = 14, rows = 8;
        float cw = (float)_res / cols, ch = (float)_res / rows;
        for (int gx = 0; gx < cols; gx++)
        {
            for (int gy = 0; gy < rows; gy++)
            {
                float v = Freq(2 + (gx + gy * cols) * 3);
                if (v < 0.14f) continue;
                float px = (gx + 0.5f) * cw, py = (gy + 0.5f) * ch;
                ColOf(v, out float cr, out float cg, out float cb);
                _c.Dot(px, py, cw * 0.5f * v, cr, cg, cb, 0.8f);
            }
        }
    }

    private void Wave()
    {
        float c = _res * 0.5f;
        for (int layer = 0; layer < 3; layer++)
        {
            float amp = _res * 0.28f * (0.4f + _bass) * (1f - layer * 0.2f);
            float prevX = 0, prevY = c;
            for (int x = 0; x <= _res; x += 2)
            {
                float ph = (float)x / _res * Mathf.PI * 6f + _t * 4f + layer;
                float y = c + Mathf.Sin(ph) * amp * Freq(2 + x);
                if (x > 0) _c.Line(prevX, prevY, x, y, (3 - layer) * 1.6f + 1f, _ar, _ag, _ab, 0.5f);
                prevX = x; prevY = y;
            }
        }
    }

    private void Bars()
    {
        int n = 48;
        float bw = (float)_res / n;
        for (int i = 0; i < n; i++)
        {
            int bin = 2 + (int)(Mathf.Pow(i / (float)n, 1.7f) * 220f);
            float v = Freq(bin);
            float h = v * _res * 0.62f;
            float x = (i + 0.5f) * bw;
            ColOf(v, out float cr, out float cg, out float cb);
            _c.Line(x, _res, x, _res - h, bw * 0.8f, cr, cg, cb, 0.7f);
            _c.Line(x, 0, x, h * 0.55f, bw * 0.8f, cr, cg, cb, 0.5f);
        }
    }

    private void Spiral()
    {
        float c = _res * 0.5f;
        for (int arm = 0; arm < 3; arm++)
        {
            for (int i = 0; i < 90; i++)
            {
                float ff = i / 90f;
                float a = ff * 7f * Mathf.PI + arm * 2f * Mathf.PI / 3f + _rot * 1.4f;
                float v = Freq(2 + i);
                float rr = ff * _res * 0.5f * (1f + _bass * 0.2f);
                ColOf(v, out float cr, out float cg, out float cb);
                _c.Dot(c + Mathf.Cos(a) * rr, c + Mathf.Sin(a) * rr, 1.5f + v * 8f * (1f - ff * 0.5f), cr, cg, cb, 0.6f);
            }
        }
    }

    private void Orbit()
    {
        float c = _res * 0.5f;
        for (int i = 0; i < 7; i++)
        {
            float rr = 60f * _wS + i * _res * 0.05f;
            float sign = i % 2 == 0 ? 1f : -1f;
            float a = _t * (0.3f + i * 0.15f) * sign * Mathf.PI;
            float sz = 6f * _wS + _smMid * 22f * _wS + _pump * 10f * _wS;
            _c.Dot(c + Mathf.Cos(a) * rr, c + Mathf.Sin(a) * rr, Mathf.Max(2f, sz), _ar, _ag, _ab, 0.8f);
        }
    }

    private void Vortex()
    {
        float c = _res * 0.5f;
        float maxR = _res * 1.414f * 0.5f;
        for (int i = 0; i < 64; i++)
        {
            float baseA = i / 64f * Mathf.PI * 2f;
            float px = c, py = c;
            for (int s = 1; s <= 10; s++)
            {
                float ff = s / 10f;
                float aa = baseA + ff * 1.6f * _rotDir + _rot;
                float rr = ff * maxR;
                float nx = c + Mathf.Cos(aa) * rr, ny = c + Mathf.Sin(aa) * rr;
                _c.Line(px, py, nx, ny, 1.5f, _ar, _ag, _ab, 0.4f);
                px = nx; py = ny;
            }
        }
    }

    private void Strobe()
    {
        float c = _res * 0.5f;
        bool on = ((int)(_t * 8f)) % 2 == 0;
        if (!on) return;
        float alpha = 0.10f + _bass * 0.22f;
        for (int y = 0; y < _res; y++)
        {
            for (int x = 0; x < _res; x++)
            {
                float ang = Mathf.Atan2(y - c, x - c) - _rot * 0.6f;
                int q = (int)(Mathf.Repeat(ang, Mathf.PI * 2f) / (Mathf.PI * 2f) * 12f);
                if (q % 2 != 0) continue;
                _c.AddPixel(x, y, _ar * alpha, _ag * alpha, _ab * alpha);
            }
        }
    }

    private void Rings()
    {
        float c = _res * 0.5f;
        float maxR = _res * 1.414f * 0.55f;
        float speed = (140f + _bass * 420f) * _wS;
        for (int y = 0; y < _res; y++)
        {
            for (int x = 0; x < _res; x++)
            {
                float dx = x - c, dy = y - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float best = 0f;
                for (int k = 0; k < 14; k++)
                {
                    float ringR = Mathf.Repeat(_t * speed + k * maxR / 14f, maxR);
                    float a = 1f - ringR / maxR;
                    float w = 2f + a * a * 14f * (0.4f + _bass);
                    float e = 1f - Mathf.Clamp01(Mathf.Abs(r - ringR) / w);
                    if (e > best) best = e;
                }
                if (best <= 0.02f) continue;
                ColOf(best, out float cr, out float cg, out float cb);
                _c.AddPixel(x, y, cr * best * 0.8f, cg * best * 0.8f, cb * best * 0.8f);
            }
        }
    }

    private void Snake(float dt)
    {
        float c = _res * 0.5f;
        if (!_snakeReady)
        {
            _snakeReady = true;
            _snakeX = c; _snakeY = c; _snakeAng = Random.value * Mathf.PI * 2f;
            _snakeTrail.Clear();
        }
        float sp = (3f + _smMid * 10f + _pump * 6f) * 3f * _wS;
        _snakeAng += Mathf.Sin(_t * 2f) * 0.15f;
        _snakeX += Mathf.Cos(_snakeAng) * sp;
        _snakeY += Mathf.Sin(_snakeAng) * sp;
        float m = 8f * _wS;
        if (_snakeX < m || _snakeX > _res - m) { _snakeAng = Mathf.PI - _snakeAng; _snakeX = Mathf.Clamp(_snakeX, m, _res - m); }
        if (_snakeY < m || _snakeY > _res - m) { _snakeAng = -_snakeAng; _snakeY = Mathf.Clamp(_snakeY, m, _res - m); }

        _snakeTrail.Add(new Vector2(_snakeX, _snakeY));
        while (_snakeTrail.Count > 60) _snakeTrail.RemoveAt(0);
        for (int i = 0; i < _snakeTrail.Count; i++)
        {
            float ff = 1f - (float)i / _snakeTrail.Count;
            _c.Dot(_snakeTrail[i].x, _snakeTrail[i].y, (4f + _bass * 26f) * _wS * (1f - ff * 0.7f), _ar, _ag, _ab, 0.7f);
        }
    }

    private void Burst(float count)
    {
        float c = _res * 0.5f;
        int n = Mathf.Clamp((int)count, 1, 40);
        for (int i = 0; i < n; i++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float sp = Random.Range(0.4f, 2.2f) * _wS * 40f;
            _parts.Add(new Particle
            {
                x = c, y = c,
                vx = Mathf.Cos(a) * sp, vy = Mathf.Sin(a) * sp,
                life = 1f, r = _br, g = _bg, b = _bb
            });
        }
    }

    private void UpdateParticles(float dt)
    {
        for (int i = _parts.Count - 1; i >= 0; i--)
        {
            var p = _parts[i];
            p.vy += 0.06f * 60f * dt * _wS * 10f;
            p.vx *= 0.96f; p.vy *= 0.96f;
            p.x += p.vx * dt * 60f; p.y += p.vy * dt * 60f;
            p.life -= 0.018f * 60f * dt;
            if (p.life <= 0f || p.x < 0 || p.x >= _res || p.y < 0 || p.y >= _res) { _parts.RemoveAt(i); continue; }
            _parts[i] = p;
            _c.Dot(p.x, p.y, 1.5f, p.r, p.g, p.b, p.life);
        }
    }

    private void UpdateShocks(float dt)
    {
        float c = _res * 0.5f;
        for (int i = _shocks.Count - 1; i >= 0; i--)
        {
            var s = _shocks[i];
            s.r += dt * 900f * _wS;
            s.life -= dt * 1.1f;
            if (s.life <= 0f) { _shocks.RemoveAt(i); continue; }
            _shocks[i] = s;
            int seg = 90;
            for (int k = 0; k < seg; k++)
            {
                float a = k / (float)seg * Mathf.PI * 2f;
                _c.Dot(c + Mathf.Cos(a) * s.r, c + Mathf.Sin(a) * s.r, 1.5f, 1f, 1f, 1f, s.life * 0.7f);
            }
        }
    }
}
