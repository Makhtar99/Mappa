using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

[DefaultExecutionOrder(10000)]
public sealed class EhubEmitter : MonoBehaviour
{
    public string ip = "127.0.0.1";
    public int port = 8765;
    public byte universe = 0;
    public float fps = 40f;
    public int maxPerMessage = 1400;

    private UdpClient _udp;
    private IPEndPoint _ep;
    private float _acc;

    private void Start()
    {
        _udp = new UdpClient();
        _ep = new IPEndPoint(IPAddress.Parse(ip), port);
    }

    private void Update()
    {
        _acc += Time.deltaTime;
        float period = 1f / Mathf.Max(1f, fps);
        if (_acc < period) return;
        _acc = 0f;

        var f = EntityField.Instance;
        if (f == null || _udp == null) return;

        int n = f.Ids.Length;
        for (int start = 0; start < n; start += maxPerMessage)
        {
            int count = Math.Min(maxPerMessage, n - start);
            var payload = new byte[count * 6];
            int p = 0;
            for (int i = 0; i < count; i++)
            {
                int id = f.Ids[start + i];
                Color32 c = f.Colors[start + i];
                payload[p] = (byte)(id & 0xFF);
                payload[p + 1] = (byte)((id >> 8) & 0xFF);
                payload[p + 2] = c.r;
                payload[p + 3] = c.g;
                payload[p + 4] = c.b;
                payload[p + 5] = c.a;
                p += 6;
            }

            byte[] gz = Gzip(payload);
            var msg = new byte[10 + gz.Length];
            msg[0] = (byte)'e'; msg[1] = (byte)'H'; msg[2] = (byte)'u'; msg[3] = (byte)'B';
            msg[4] = 2;
            msg[5] = universe;
            msg[6] = (byte)(count & 0xFF); msg[7] = (byte)((count >> 8) & 0xFF);
            msg[8] = (byte)(gz.Length & 0xFF); msg[9] = (byte)((gz.Length >> 8) & 0xFF);
            Array.Copy(gz, 0, msg, 10, gz.Length);

            try { _udp.Send(msg, msg.Length, _ep); }
            catch (SocketException e) { Debug.LogWarning(e.Message); }
        }
    }

    private static byte[] Gzip(byte[] data)
    {
        using (var m = new MemoryStream())
        {
            using (var g = new GZipStream(m, CompressionMode.Compress, true))
                g.Write(data, 0, data.Length);
            return m.ToArray();
        }
    }

    private void OnDestroy() => _udp?.Close();
}
