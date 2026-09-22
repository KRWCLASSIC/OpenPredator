using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenPredator.Core.Backends;

public static class SmbiosReader
{
    private const uint RSMB = 0x52534D42; // 'RSMB' in ASCII

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetSystemFirmwareTable(uint firmwareTableProviderSignature, uint firmwareTableId, IntPtr pFirmwareTableBuffer, uint bufferSize);

    public record SmbiosInfo(
        string Manufacturer,
        string ProductName,
        string Version,
        string SerialNumber,
        string Family,
        bool HasRgbKeyboard
    );

    public static SmbiosInfo GetSystemInfo()
    {
        string mfr = "Acer";
        string product = "Acer Gaming Laptop";
        string version = "";
        string serial = "";
        string family = "";
        bool hasRgbKb = false;

        if (!OperatingSystem.IsWindows())
        {
            return new SmbiosInfo(mfr, product, version, serial, family, hasRgbKb);
        }

        try
        {
            uint size = GetSystemFirmwareTable(RSMB, 0, IntPtr.Zero, 0);
            if (size <= 8)
            {
                return new SmbiosInfo(mfr, product, version, serial, family, hasRgbKb);
            }

            byte[] buffer = new byte[size];
            unsafe
            {
                fixed (byte* pBuf = buffer)
                {
                    uint bytesRead = GetSystemFirmwareTable(RSMB, 0, (IntPtr)pBuf, size);
                    if (bytesRead <= 8)
                    {
                        return new SmbiosInfo(mfr, product, version, serial, family, hasRgbKb);
                    }
                }
            }

            int offset = 8;
            int totalLength = buffer.Length;

            while (offset + 4 <= totalLength)
            {
                byte type = buffer[offset];
                byte length = buffer[offset + 1];
                if (length < 4 || offset + length > totalLength)
                {
                    break;
                }

                // Collect strings in the string section
                var strings = new List<string>();
                int strStart = offset + length;
                int ptr = strStart;

                while (ptr < totalLength)
                {
                    if (buffer[ptr] == 0)
                    {
                        int strLen = ptr - strStart;
                        if (strLen > 0)
                        {
                            strings.Add(Encoding.ASCII.GetString(buffer, strStart, strLen).Trim());
                        }
                        strStart = ptr + 1;

                        // Double null terminator (\0\0) signals end of SMBIOS table record
                        if (ptr + 1 < totalLength && buffer[ptr + 1] == 0)
                        {
                            ptr += 2;
                            break;
                        }
                    }
                    ptr++;
                }

                string GetString(int stringIndex)
                {
                    if (stringIndex > 0 && stringIndex <= strings.Count)
                    {
                        return strings[stringIndex - 1];
                    }
                    return string.Empty;
                }

                // Type 1: System Information
                if (type == 1 && length >= 8)
                {
                    mfr = GetString(buffer[offset + 4]);
                    string prod = GetString(buffer[offset + 5]);
                    if (!string.IsNullOrWhiteSpace(prod))
                    {
                        product = prod;
                    }
                    version = GetString(buffer[offset + 6]);
                    serial = GetString(buffer[offset + 7]);

                    if (length >= 0x1B)
                    {
                        family = GetString(buffer[offset + 0x1A]);
                    }
                }

                // Type 171 (0xAB): Acer Proprietary OEM Feature Table
                // 5-byte entry format: (FeatureID, Category, Status, Param1, Param2)
                // Feature 19 (0x13) = 4-Zone RGB Keyboard. Status byte (offset + j + 2) must be == 1 (Supported).
                if (type == 171 && length >= 4)
                {
                    for (int j = 4; j + 2 < length; j += 5)
                    {
                        byte featureId = buffer[offset + j];
                        byte status = buffer[offset + j + 2];
                        if (featureId == 19 && status == 1) // 0x13 = RGB Keyboard controller Enabled
                        {
                            hasRgbKb = true;
                        }
                    }
                }

                // End of tables marker
                if (type == 127)
                {
                    break;
                }

                offset = ptr;
            }
        }
        catch { }

        return new SmbiosInfo(mfr, product, version, serial, family, hasRgbKb);
    }
}
