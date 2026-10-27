using System.IO;

namespace OpenPredator.Core.Backends.Linux;

/// <summary>
/// Reads DMI/SMBIOS system information from Linux sysfs (/sys/class/dmi/id/).
/// </summary>
public static class LinuxDmiReader
{
    public static SmbiosReader.SmbiosInfo GetSystemInfo()
    {
        string mfr = "Acer";
        string product = "Acer Gaming Laptop";
        string version = "";
        string serial = "";
        string family = "";
        bool hasRgbKb = false;

        try
        {
            if (File.Exists("/sys/class/dmi/id/sys_vendor"))
                mfr = File.ReadAllText("/sys/class/dmi/id/sys_vendor").Trim();

            if (File.Exists("/sys/class/dmi/id/product_name"))
            {
                string p = File.ReadAllText("/sys/class/dmi/id/product_name").Trim();
                if (!string.IsNullOrWhiteSpace(p))
                    product = p;
            }

            if (File.Exists("/sys/class/dmi/id/product_version"))
                version = File.ReadAllText("/sys/class/dmi/id/product_version").Trim();

            if (File.Exists("/sys/class/dmi/id/product_serial"))
                serial = File.ReadAllText("/sys/class/dmi/id/product_serial").Trim();

            if (File.Exists("/sys/class/dmi/id/product_family"))
                family = File.ReadAllText("/sys/class/dmi/id/product_family").Trim();

            if (File.Exists("/sys/firmware/dmi/tables/DMI"))
            {
                byte[] dmiBuf = File.ReadAllBytes("/sys/firmware/dmi/tables/DMI");
                hasRgbKb = ParseRgbKeyboardFromDmi(dmiBuf);
            }
        }
        catch { }

        return new SmbiosReader.SmbiosInfo(mfr, product, version, serial, family, hasRgbKb);
    }

    private static bool ParseRgbKeyboardFromDmi(byte[] buffer)
    {
        if (buffer == null || buffer.Length < 4) return false;
        int offset = 0;
        int totalLength = buffer.Length;

        while (offset + 4 <= totalLength)
        {
            byte type = buffer[offset];
            byte length = buffer[offset + 1];
            if (length < 4 || offset + length > totalLength) break;

            // Type 171 (0xAB): Acer OEM Feature Table. Feature 19 (0x13) = RGB Keyboard controller Enabled.
            if (type == 171 && length >= 4)
            {
                for (int j = 4; j + 2 < length; j += 5)
                {
                    byte featureId = buffer[offset + j];
                    byte status = buffer[offset + j + 2];
                    if (featureId == 19 && status == 1)
                    {
                        return true;
                    }
                }
            }

            if (type == 127) break;

            int ptr = offset + length;
            while (ptr < totalLength)
            {
                if (buffer[ptr] == 0)
                {
                    if (ptr + 1 < totalLength && buffer[ptr + 1] == 0)
                    {
                        ptr += 2;
                        break;
                    }
                }
                ptr++;
            }
            offset = ptr;
        }

        return false;
    }
}
