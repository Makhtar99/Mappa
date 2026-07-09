using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Mappa
{
    public static class Ehub
    {
        public static readonly byte[] Magic = { (byte)'e', (byte)'H', (byte)'u', (byte)'B' };

        public const byte TypeConfig = 1;
        public const byte TypeUpdate = 2;

        public const int DefaultUdpPort = 8765;

        public const int SextuorSize = 6;
        public const int RangeSize = 8;

        public static bool LittleEndian = true;

        public readonly struct Range
        {
            public readonly ushort SextuorStart;
            public readonly ushort EntityStart;
            public readonly ushort SextuorEnd;
            public readonly ushort EntityEnd;

            public Range(ushort sextuorStart, ushort entityStart, ushort sextuorEnd, ushort entityEnd)
            {
                SextuorStart = sextuorStart;
                EntityStart = entityStart;
                SextuorEnd = sextuorEnd;
                EntityEnd = entityEnd;
            }
        }

        public static List<Range> ComputeRanges(IReadOnlyList<int> orderedIds)
        {
            var ranges = new List<Range>();
            if (orderedIds.Count == 0) return ranges;

            int runStartIndex = 0;
            int runStartId = orderedIds[0];
            int prevId = orderedIds[0];

            for (int i = 1; i < orderedIds.Count; i++)
            {
                int id = orderedIds[i];
                if (id != prevId + 1)
                {
                    ranges.Add(new Range(
                        (ushort)runStartIndex, (ushort)runStartId,
                        (ushort)(i - 1), (ushort)prevId));
                    runStartIndex = i;
                    runStartId = id;
                }
                prevId = id;
            }
            ranges.Add(new Range(
                (ushort)runStartIndex, (ushort)runStartId,
                (ushort)(orderedIds.Count - 1), (ushort)prevId));
            return ranges;
        }

        public static byte[] EncodeUpdate(byte ehubUniverse, IReadOnlyList<int> orderedIds, State state)
        {
            var payload = new byte[orderedIds.Count * SextuorSize];
            int p = 0;
            for (int i = 0; i < orderedIds.Count; i++)
            {
                int id = orderedIds[i];
                var c = state.Get(id);
                WriteU16(payload, p, (ushort)id);
                payload[p + 2] = c.R;
                payload[p + 3] = c.G;
                payload[p + 4] = c.B;
                payload[p + 5] = c.W;
                p += SextuorSize;
            }
            return BuildMessage(TypeUpdate, ehubUniverse, (ushort)orderedIds.Count, payload);
        }

        public static byte[] EncodeConfig(byte ehubUniverse, IReadOnlyList<Range> ranges)
        {
            var payload = new byte[ranges.Count * RangeSize];
            int p = 0;
            foreach (var r in ranges)
            {
                WriteU16(payload, p, r.SextuorStart);
                WriteU16(payload, p + 2, r.EntityStart);
                WriteU16(payload, p + 4, r.SextuorEnd);
                WriteU16(payload, p + 6, r.EntityEnd);
                p += RangeSize;
            }
            return BuildMessage(TypeConfig, ehubUniverse, (ushort)ranges.Count, payload);
        }

        private static byte[] BuildMessage(byte type, byte ehubUniverse, ushort count, byte[] payload)
        {
            byte[] compressed = Gzip(payload);
            var msg = new byte[10 + compressed.Length];
            msg[0] = Magic[0]; msg[1] = Magic[1]; msg[2] = Magic[2]; msg[3] = Magic[3];
            msg[4] = type;
            msg[5] = ehubUniverse;
            WriteU16(msg, 6, count);
            WriteU16(msg, 8, (ushort)compressed.Length);
            Array.Copy(compressed, 0, msg, 10, compressed.Length);
            return msg;
        }

        public sealed class Message
        {
            public byte Type;
            public byte Universe;
            public ushort Count;
            public byte[] Payload = Array.Empty<byte>();
        }

        public static Message Decode(byte[] message)
        {
            if (message.Length < 10)
                throw new ArgumentException("Message eHuB trop court.");
            for (int i = 0; i < 4; i++)
                if (message[i] != Magic[i])
                    throw new ArgumentException("En-tete eHuB invalide.");

            ushort compressedLen = ReadU16(message, 8);
            if (10 + compressedLen > message.Length)
                throw new ArgumentException("Taille de payload compresse incoherente.");

            var compressed = new byte[compressedLen];
            Array.Copy(message, 10, compressed, 0, compressedLen);

            return new Message
            {
                Type = message[4],
                Universe = message[5],
                Count = ReadU16(message, 6),
                Payload = Gunzip(compressed),
            };
        }

        public static List<Range> DecodeRanges(Message config)
        {
            var ranges = new List<Range>(config.Count);
            for (int i = 0; i < config.Count; i++)
            {
                int p = i * RangeSize;
                ranges.Add(new Range(
                    ReadU16(config.Payload, p),
                    ReadU16(config.Payload, p + 2),
                    ReadU16(config.Payload, p + 4),
                    ReadU16(config.Payload, p + 6)));
            }
            return ranges;
        }

        public static void WriteU16(byte[] buf, int offset, ushort value)
        {
            if (LittleEndian)
            {
                buf[offset] = (byte)(value & 0xFF);
                buf[offset + 1] = (byte)((value >> 8) & 0xFF);
            }
            else
            {
                buf[offset] = (byte)((value >> 8) & 0xFF);
                buf[offset + 1] = (byte)(value & 0xFF);
            }
        }

        public static ushort ReadU16(byte[] buf, int offset)
        {
            return LittleEndian
                ? (ushort)(buf[offset] | (buf[offset + 1] << 8))
                : (ushort)((buf[offset] << 8) | buf[offset + 1]);
        }

        private static byte[] Gzip(byte[] data)
        {
            using var outStream = new MemoryStream();
            using (var gz = new GZipStream(outStream, CompressionLevel.Fastest, leaveOpen: true))
            {
                gz.Write(data, 0, data.Length);
            }
            return outStream.ToArray();
        }

        private static byte[] Gunzip(byte[] data)
        {
            using var inStream = new MemoryStream(data);
            using var gz = new GZipStream(inStream, CompressionMode.Decompress);
            using var outStream = new MemoryStream();
            gz.CopyTo(outStream);
            return outStream.ToArray();
        }
    }
}
