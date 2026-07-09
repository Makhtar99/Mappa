using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Mappa;
using Mappa.Authoring.Core;

namespace Mappa.Authoring.App;

public sealed class WallView : Control
{
    private WriteableBitmap? _bmp;
    private EntityLayout? _layout;
    private byte[] _pixels = Array.Empty<byte>();
    private int _w, _h;

    public WallView()
    {
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    public void SetLayout(EntityLayout layout)
    {
        _layout = layout;
        _w = layout.Cols > 0 ? layout.Cols : 128;
        _h = layout.Rows > 0 ? layout.Rows : 128;
        _pixels = new byte[_w * _h * 4];
        _bmp = new WriteableBitmap(new PixelSize(_w, _h), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        InvalidateVisual();
    }

    public void UpdateFrom(State state)
    {
        if (_layout == null || _bmp == null) return;

        Array.Clear(_pixels, 0, _pixels.Length);
        bool grid = _layout.Cols > 0;

        foreach (int id in _layout.Ids)
        {
            if (!_layout.TryGet(id, out var p)) continue;
            int px, py;
            if (grid)
            {
                px = p.Col;
                py = _h - 1 - p.Row;
            }
            else
            {
                px = (int)(p.Nx * (_w - 1) + 0.5f);
                py = (int)((1f - p.Ny) * (_h - 1) + 0.5f);
            }
            if (px < 0 || px >= _w || py < 0 || py >= _h) continue;

            var c = state.Get(id);
            int i = (py * _w + px) * 4;
            _pixels[i] = c.B;
            _pixels[i + 1] = c.G;
            _pixels[i + 2] = c.R;
            _pixels[i + 3] = 255;
        }

        using (var fb = _bmp.Lock())
        {
            int rowBytes = _w * 4;
            for (int y = 0; y < _h; y++)
            {
                var dst = new IntPtr(fb.Address.ToInt64() + (long)y * fb.RowBytes);
                Marshal.Copy(_pixels, y * rowBytes, dst, rowBytes);
            }
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        context.FillRectangle(Brushes.Black, new Rect(bounds.Size));
        if (_bmp == null || _w == 0 || _h == 0) return;

        double scale = Math.Min(bounds.Width / _w, bounds.Height / _h);
        if (scale <= 0) return;
        double dw = _w * scale, dh = _h * scale;
        double ox = (bounds.Width - dw) / 2, oy = (bounds.Height - dh) / 2;

        context.DrawImage(_bmp, new Rect(0, 0, _w, _h), new Rect(ox, oy, dw, dh));
    }
}
