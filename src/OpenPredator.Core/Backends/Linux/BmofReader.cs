using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenPredator.Core.Services;

namespace OpenPredator.Core.Backends.Linux;

/// <summary>
/// Discovers, decompresses, and parses Binary MOF (BMOF) from ACPI PNP0C14 devices on Linux.
/// Embedded pure C# DoubleSpace (DS-0-2) decompressor and BMF parser: zero external bmfdec dependency.
/// </summary>
public static class BmofReader
{
    private static readonly Dictionary<string, uint> _gamingMethodMap = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, uint> _genericMethodMap = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized = false;
    private static readonly object _lock = new();

    public static uint? GetGamingMethodId(string methodName)
    {
        EnsureInitialized();
        return _gamingMethodMap.TryGetValue(methodName, out uint id) ? id : null;
    }

    public static uint? GetGenericMethodId(string methodName)
    {
        EnsureInitialized();
        return _genericMethodMap.TryGetValue(methodName, out uint id) ? id : null;
    }

    public static void EnsureInitialized()
    {
        if (_initialized) return;

        lock (_lock)
        {
            if (_initialized) return;

            // 1. Try reading from cached JSON file first
            string cachePath = GetCacheFilePath();
            if (File.Exists(cachePath) && TryLoadFromCache(cachePath))
            {
                AppLogger.Info($"[BmofReader] Loaded WMI method map from cache ({cachePath})");
                _initialized = true;
                return;
            }

            // 2. Discover BMOF sysfs file in /sys/bus/wmi/devices/*/bmof
            string? bmofPath = FindBmofSysfsPath();
            if (bmofPath != null && TryDecompressAndParse(bmofPath))
            {
                SaveToCache(cachePath);
                AppLogger.Info($"[BmofReader] Successfully decompressed and parsed BMOF from {bmofPath} (cached to {cachePath})");
                _initialized = true;
                return;
            }

            AppLogger.Error("[BmofReader] Failed to discover WMI methods: neither cached wmi_methods.json nor bmof decompression succeeded. WMI calls disabled.");
            _initialized = true;
        }
    }

    private static string? FindBmofSysfsPath()
    {
        const string wmiDevicesPath = "/sys/bus/wmi/devices";
        if (!Directory.Exists(wmiDevicesPath)) return null;

        foreach (var dir in Directory.GetDirectories(wmiDevicesPath))
        {
            string bmofFile = Path.Combine(dir, "bmof");
            if (File.Exists(bmofFile))
            {
                return bmofFile;
            }
        }
        return null;
    }

    private static bool TryDecompressAndParse(string bmofPath)
    {
        try
        {
            byte[] raw = File.ReadAllBytes(bmofPath);
            if (raw.Length <= 16) return false;

            // BMOF Header:
            // 0..3:   0x424D4F46 ("BMOF" / "FOMB")
            // 4..7:   0x00000001 (Version 1)
            // 8..11:  Compressed size (raw.Length - 16)
            // 12..15: Decompressed size
            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0, 4));
            uint version = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(4, 4));
            if (magic != 0x424D4F46 || version != 0x00000001)
            {
                AppLogger.Warn($"[BmofReader] Invalid BMOF magic (0x{magic:X8}) or version (0x{version:X8}) in {bmofPath}");
                return false;
            }

            int decompressedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(12, 4));
            if (decompressedSize <= 0 || decompressedSize > 0x2000000)
            {
                AppLogger.Warn($"[BmofReader] Invalid decompressed size: {decompressedSize}");
                return false;
            }

            ReadOnlySpan<byte> compressedPayload = raw.AsSpan(16);
            byte[]? bmfData = DoubleSpaceDecoder.Decompress(compressedPayload, decompressedSize);
            if (bmfData == null)
            {
                AppLogger.Warn($"[BmofReader] DoubleSpace decompression failed for {bmofPath}");
                return false;
            }

            return BmfParser.Parse(bmfData, _gamingMethodMap, _genericMethodMap);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[BmofReader] Error reading {bmofPath}: {ex.Message}");
            return false;
        }
    }

    private static string GetCacheFilePath()
    {
        string? home = Environment.GetEnvironmentVariable("HOME");
        string dir = !string.IsNullOrEmpty(home)
            ? Path.Combine(home, ".config", "openpredator")
            : "/etc/openpredator";

        try { Directory.CreateDirectory(dir); } catch { }
        return Path.Combine(dir, "wmi_methods.json");
    }

    private static bool TryLoadFromCache(string cachePath)
    {
        try
        {
            string json = File.ReadAllText(cachePath);
            var cache = JsonSerializer.Deserialize(json, BmofJsonContext.Default.BmofCache);
            if (cache != null)
            {
                if (cache.AcerGamingFunction != null)
                {
                    foreach (var kvp in cache.AcerGamingFunction) _gamingMethodMap[kvp.Key] = kvp.Value;
                }
                if (cache.APGeAction != null)
                {
                    foreach (var kvp in cache.APGeAction) _genericMethodMap[kvp.Key] = kvp.Value;
                }
            }

            return _gamingMethodMap.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveToCache(string cachePath)
    {
        try
        {
            var cache = new BmofCache(_gamingMethodMap, _genericMethodMap);
            string json = JsonSerializer.Serialize(cache, BmofJsonContext.Default.BmofCache);
            File.WriteAllText(cachePath, json);
        }
        catch { }
    }


    #region Pure C# DoubleSpace (DS-0-2) Decompressor

    private static class DoubleSpaceDecoder
    {
        private ref struct BitStream
        {
            private readonly ReadOnlySpan<byte> _input;
            private int _inputPos;
            public uint Buf;
            public int Pb;

            public BitStream(ReadOnlySpan<byte> input)
            {
                _input = input;
                _inputPos = 0;
                Buf = 0;
                Pb = 32;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void Get16()
            {
                Buf >>= 16;
                Pb -= 16;
                if (_inputPos + 1 < _input.Length)
                {
                    ushort val = unchecked((ushort)(_input[_inputPos] | (_input[_inputPos + 1] << 8)));
                    _inputPos += 2;
                    Buf |= (uint)val << 16;
                }
                else if (_inputPos < _input.Length)
                {
                    ushort val = _input[_inputPos];
                    _inputPos++;
                    Buf |= (uint)val << 16;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public uint Prepare()
            {
                if (Pb >= 16) Get16();
                return Buf >> Pb;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public uint ReadBits(int n)
            {
                uint u = Prepare();
                Pb += n;
                return u & ((1u << n) - 1u);
            }

            public bool HasMoreBits => _inputPos < _input.Length || Pb < 16;
        }

        private static int ReadLength(ref BitStream stream)
        {
            uint u = stream.Prepare();
            switch (u & 15)
            {
                case 1: case 3: case 5: case 7:
                case 9: case 11: case 13: case 15:
                    stream.Pb++;
                    return 3;
                case 2: case 6:
                case 10: case 14:
                    stream.Pb += 3;
                    return (int)((1 & (u >> 2)) + 4);
                case 4: case 12:
                    stream.Pb += 5;
                    return (int)((3 & (u >> 3)) + 6);
                case 8:
                    stream.Pb += 7;
                    return (int)((7 & (u >> 4)) + 10);
                case 0:
                    break;
            }

            switch ((u >> 4) & 15)
            {
                case 1: case 3: case 5: case 7:
                case 9: case 11: case 13: case 15:
                    stream.Pb += 9;
                    return (int)((15 & (u >> 5)) + 18);
                case 2: case 6:
                case 10: case 14:
                    stream.Pb += 11;
                    return (int)((31 & (u >> 6)) + 34);
                case 4: case 12:
                    stream.Pb += 13;
                    return (int)((63 & (u >> 7)) + 66);
                case 8:
                    stream.Pb += 15;
                    return (int)((127 & (u >> 8)) + 130);
                case 0:
                    break;
            }

            stream.Pb += 9;
            if ((u & 256) != 0)
            {
                return (int)(stream.ReadBits(8) + 258);
            }
            return -1;
        }

        private static bool Decrep(ref BitStream stream, byte[] pout, ref int outPos, int repOffs, int k, int flg)
        {
            if (repOffs == 0) return false;
            if (repOffs == 0x113F)
            {
                if ((outPos % 512 != 0) && (flg & 0x4000) == 0) return false;
                return true;
            }

            int repLen = ReadLength(ref stream) + k;
            if (repLen <= 0 || repOffs > outPos || outPos + repLen > pout.Length)
                return false;

            int src = outPos - repOffs;
            for (int i = 0; i < repLen; i++)
            {
                pout[outPos++] = pout[src++];
            }
            return true;
        }

        public static byte[]? Decompress(ReadOnlySpan<byte> compressed, int decompressedSize)
        {
            if (decompressedSize <= 0 || decompressedSize > 0x2000000) return null;
            byte[] pout = new byte[decompressedSize];
            int outPos = 0;

            var stream = new BitStream(compressed);
            if (stream.ReadBits(16) != 0x5344) return null; // "DS" signature

            uint ver = stream.ReadBits(16);

            bool ok = true;
            while (ok && outPos < pout.Length && stream.HasMoreBits)
            {
                uint u = stream.Prepare();
                switch (u & 3)
                {
                    case 0:
                        stream.Pb += 8;
                        int repOffs0 = (int)((u >> 2) & 63);
                        ok = Decrep(ref stream, pout, ref outPos, repOffs0, -1, 0);
                        break;
                    case 1:
                        stream.Pb += 9;
                        if (outPos < pout.Length) pout[outPos++] = (byte)(((u >> 2) & 0x7F) | 128);
                        else ok = false;
                        break;
                    case 2:
                        stream.Pb += 9;
                        if (outPos < pout.Length) pout[outPos++] = (byte)((u >> 2) & 127);
                        else ok = false;
                        break;
                    case 3:
                        int repOffs3;
                        if ((u & 4) != 0)
                        {
                            stream.Pb += 15;
                            repOffs3 = (int)(((u >> 3) & 4095) + 320);
                        }
                        else
                        {
                            stream.Pb += 11;
                            repOffs3 = (int)(((u >> 3) & 255) + 64);
                        }
                        ok = Decrep(ref stream, pout, ref outPos, repOffs3, -1, 0);
                        break;
                }
            }

            return ok && outPos == decompressedSize ? pout : (outPos > 0 ? pout : null);
        }
    }

    #endregion

    #region Pure C# Binary MOF (BMF) Parser

    private static class BmfParser
    {
        public static bool Parse(
            byte[] bmf,
            Dictionary<string, uint> gamingMap,
            Dictionary<string, uint> genericMap)
        {
            if (bmf.Length < 20) return false;

            // Decompressed BMF starts with 0x424D4F46 ("BMOF") and length of first part
            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bmf.AsSpan(0, 4));
            if (magic != 0x424D4F46) return false;

            uint firstPartLen = BinaryPrimitives.ReadUInt32LittleEndian(bmf.AsSpan(4, 4));
            if (firstPartLen > bmf.Length || firstPartLen < 20) return false;

            // Root structure at offset 8:
            // offset 8:  unknown1 (4 bytes)
            // offset 12: unknown2 (4 bytes)
            // offset 16: classCount (4 bytes)
            uint classCount = BinaryPrimitives.ReadUInt32LittleEndian(bmf.AsSpan(16, 4));
            int offset = 20;

            for (uint c = 0; c < classCount && offset + 20 <= firstPartLen; c++)
            {
                uint classLen = BinaryPrimitives.ReadUInt32LittleEndian(bmf.AsSpan(offset, 4));
                if (classLen < 20 || offset + classLen > firstPartLen) break;

                ParseClass(bmf.AsSpan(offset, (int)classLen), gamingMap, genericMap);
                offset += (int)classLen;
            }

            return gamingMap.Count > 0 || genericMap.Count > 0;
        }

        private static void ParseClass(
            ReadOnlySpan<byte> classSpan,
            Dictionary<string, uint> gamingMap,
            Dictionary<string, uint> genericMap)
        {
            if (classSpan.Length < 20) return;

            uint qualifiersLen = BinaryPrimitives.ReadUInt32LittleEndian(classSpan.Slice(8, 4));
            uint dataLen = BinaryPrimitives.ReadUInt32LittleEndian(classSpan.Slice(12, 4));

            if (20 + dataLen > classSpan.Length) return;

            // Parse class name from __CLASS property inside class data
            string? className = GetClassName(classSpan.Slice(20, (int)dataLen));
            if (string.IsNullOrEmpty(className)) return;

            Dictionary<string, uint>? targetMap = null;
            if (className.Equals("AcerGamingFunction", StringComparison.OrdinalIgnoreCase))
                targetMap = gamingMap;
            else if (className.Equals("APGeAction", StringComparison.OrdinalIgnoreCase))
                targetMap = genericMap;

            if (targetMap == null) return;

            // Methods array follows immediately after class data
            int methodOffset = 20 + (int)dataLen;
            if (methodOffset + 8 > classSpan.Length) return;

            uint methodsTotalLen = BinaryPrimitives.ReadUInt32LittleEndian(classSpan.Slice(methodOffset, 4));
            uint methodsCount = BinaryPrimitives.ReadUInt32LittleEndian(classSpan.Slice(methodOffset + 4, 4));
            methodOffset += 8;

            for (uint m = 0; m < methodsCount && methodOffset + 20 <= classSpan.Length; m++)
            {
                uint methodLen = BinaryPrimitives.ReadUInt32LittleEndian(classSpan.Slice(methodOffset, 4));
                if (methodLen < 20 || methodOffset + methodLen > classSpan.Length) break;

                ParseMethod(classSpan.Slice(methodOffset, (int)methodLen), targetMap);
                methodOffset += (int)methodLen;
            }
        }

        private static void ParseMethod(ReadOnlySpan<byte> methodSpan, Dictionary<string, uint> targetMap)
        {
            if (methodSpan.Length < 20) return;

            uint nameLen = BinaryPrimitives.ReadUInt32LittleEndian(methodSpan.Slice(12, 4));
            uint paramLen = BinaryPrimitives.ReadUInt32LittleEndian(methodSpan.Slice(16, 4));

            if (nameLen == 0xFFFFFFFF) nameLen = paramLen;
            if (20 + nameLen > methodSpan.Length) return;

            string methodName = ParseString(methodSpan.Slice(20, (int)nameLen));
            if (string.IsNullOrWhiteSpace(methodName)) return;

            // Qualifiers block begins at 20 + paramLen
            int qualOffset = 20 + (int)paramLen;
            if (qualOffset + 8 > methodSpan.Length) return;

            uint qualTotalLen = BinaryPrimitives.ReadUInt32LittleEndian(methodSpan.Slice(qualOffset, 4));
            uint qualCount = BinaryPrimitives.ReadUInt32LittleEndian(methodSpan.Slice(qualOffset + 4, 4));
            qualOffset += 8;

            for (uint q = 0; q < qualCount && qualOffset + 16 <= methodSpan.Length; q++)
            {
                uint qLen = BinaryPrimitives.ReadUInt32LittleEndian(methodSpan.Slice(qualOffset, 4));
                if (qLen < 16 || qualOffset + qLen > methodSpan.Length) break;

                uint qType = BinaryPrimitives.ReadUInt32LittleEndian(methodSpan.Slice(qualOffset + 4, 4));
                uint qNameLen = BinaryPrimitives.ReadUInt32LittleEndian(methodSpan.Slice(qualOffset + 12, 4));

                if (qualOffset + 16 + qNameLen <= methodSpan.Length)
                {
                    string qName = ParseString(methodSpan.Slice(qualOffset + 16, (int)qNameLen));
                    if (qName.Equals("WmiMethodId", StringComparison.OrdinalIgnoreCase) && qType == 0x03)
                    {
                        if (qualOffset + 16 + qNameLen + 4 <= methodSpan.Length)
                        {
                            uint methodId = BinaryPrimitives.ReadUInt32LittleEndian(methodSpan.Slice(qualOffset + 16 + (int)qNameLen, 4));
                            targetMap[methodName] = methodId;
                        }
                    }
                }

                qualOffset += (int)qLen;
            }
        }

        private static string? GetClassName(ReadOnlySpan<byte> classData)
        {
            if (classData.Length < 16) return null;
            int offset = 0;
            uint len1 = BinaryPrimitives.ReadUInt32LittleEndian(classData.Slice(offset, 4));
            offset += 8 + (int)len1; // skip qualifiers

            if (offset + 8 > classData.Length) return null;
            uint len2 = BinaryPrimitives.ReadUInt32LittleEndian(classData.Slice(offset, 4));
            uint count2 = BinaryPrimitives.ReadUInt32LittleEndian(classData.Slice(offset + 4, 4));
            offset += 8;

            for (int i = 0; i < count2 && offset + 20 <= classData.Length; i++)
            {
                uint propLen = BinaryPrimitives.ReadUInt32LittleEndian(classData.Slice(offset, 4));
                if (propLen == 0 || offset + propLen > classData.Length) break;

                uint propType = BinaryPrimitives.ReadUInt32LittleEndian(classData.Slice(offset + 4, 4));
                uint isClassProp = BinaryPrimitives.ReadUInt32LittleEndian(classData.Slice(offset + 16, 4));

                if (isClassProp == 0xFFFFFFFF && propType == 0x08)
                {
                    uint slen = BinaryPrimitives.ReadUInt32LittleEndian(classData.Slice(offset + 12, 4));
                    if (offset + 20 + slen <= classData.Length)
                    {
                        string name = ParseString(classData.Slice(offset + 20, (int)slen));
                        if (string.Equals(name, "__CLASS", StringComparison.OrdinalIgnoreCase))
                        {
                            int valLen = (int)(propLen - slen - 20);
                            if (valLen > 0 && offset + 20 + slen + valLen <= classData.Length)
                            {
                                return ParseString(classData.Slice(offset + 20 + (int)slen, valLen));
                            }
                        }
                    }
                }
                offset += (int)propLen;
            }
            return null;
        }

        private static string ParseString(ReadOnlySpan<byte> buf)
        {
            int len = 0;
            while (len + 1 < buf.Length)
            {
                if (buf[len] == 0 && buf[len + 1] == 0) break;
                len += 2;
            }
            return Encoding.Unicode.GetString(buf.Slice(0, len));
        }
    }

    #endregion
}

internal sealed record BmofCache(
    Dictionary<string, uint> AcerGamingFunction,
    Dictionary<string, uint> APGeAction
);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(BmofCache))]
internal partial class BmofJsonContext : JsonSerializerContext
{
}
