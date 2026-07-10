using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

public sealed class DeviceEmitter : MonoBehaviour
{
    public string ip = "127.0.0.1";
    public int port = 8765;
    public byte universe = 0;
    public float fps = 40f;

    private readonly Dictionary<int, uint> _ch = new Dictionary<int, uint>();
    private UdpClient _udp;
    private IPEndPoint _ep;
    private float _acc;

    private void Start()
    {
        _udp = new UdpClient();
        _ep = new IPEndPoint(IPAddress.Parse(ip), port);
    }

    public void Set(int entityId, byte r, byte g, byte b, byte w)
    {
        _ch[entityId] = (uint)((r << 24) | (g << 16) | (b << 8) | w);
    }

    private void Update()
    {
        _acc += Time.deltaTime;
        if (_acc < 1f / Mathf.Max(1f, fps)) return;
        _acc = 0f;
        if (_udp == null || _ch.Count == 0) return;

        var ids = new List<int>(_ch.Keys);
        ids.Sort();
        var payload = new byte[ids.Count * 6];
        int p = 0;
        foreach (int id in ids)
        {
            uint c = _ch[id];
            payload[p] = (byte)(id & 0xFF);
            payload[p + 1] = (byte)((id >> 8) & 0xFF);
            payload[p + 2] = (byte)(c >> 24);
            payload[p + 3] = (byte)(c >> 16);
            payload[p + 4] = (byte)(c >> 8);
            payload[p + 5] = (byte)c;
            p += 6;
        }

        byte[] gz = Gzip(payload);
        var msg = new byte[10 + gz.Length];
        msg[0] = (byte)'e'; msg[1] = (byte)'H'; msg[2] = (byte)'u'; msg[3] = (byte)'B';
        msg[4] = 2;
        msg[5] = universe;
        msg[6] = (byte)(ids.Count & 0xFF); msg[7] = (byte)((ids.Count >> 8) & 0xFF);
        msg[8] = (byte)(gz.Length & 0xFF); msg[9] = (byte)((gz.Length >> 8) & 0xFF);
        Array.Copy(gz, 0, msg, 10, gz.Length);

        try { _udp.Send(msg, msg.Length, _ep); }
        catch (SocketException e) { Debug.LogWarning(e.Message); }
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
