using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenPredator.Core.Enums;

namespace OpenPredator.Core.Protocol;

public static class PacketSerializer
{
    public static byte[] SerializeRequest(ServiceCommand command, params object[] args)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write((short)command);
        writer.Write((byte)args.Length);

        foreach (var arg in args)
        {
            switch (arg)
            {
                case byte b:
                    writer.Write(1);
                    writer.Write(b);
                    break;
                case short s:
                    writer.Write(2);
                    writer.Write(s);
                    break;
                case int i:
                    writer.Write(4);
                    writer.Write(i);
                    break;
                case uint u:
                    writer.Write(4);
                    writer.Write(u);
                    break;
                case long l:
                    writer.Write(8);
                    writer.Write(l);
                    break;
                case ulong ul:
                    writer.Write(8);
                    writer.Write(ul);
                    break;
                case string str:
                    byte[] strBytes = Encoding.Unicode.GetBytes(str + "\0");
                    writer.Write(strBytes.Length);
                    writer.Write(strBytes);
                    break;
                case byte[] raw:
                    writer.Write(raw.Length);
                    writer.Write(raw);
                    break;
                default:
                    throw new ArgumentException($"Unsupported argument type: {arg.GetType()}");
            }
        }

        return ms.ToArray();
    }

    public static bool TryDeserializeRequest(
        ReadOnlySpan<byte> buffer,
        out ServiceCommand command,
        out List<byte[]> args)
    {
        command = 0;
        args = new List<byte[]>();

        if (buffer.Length < 3)
            return false;

        command = (ServiceCommand)BinaryPrimitives.ReadInt16LittleEndian(buffer[..2]);
        byte argCount = buffer[2];
        int offset = 3;

        for (int i = 0; i < argCount; i++)
        {
            if (offset + 4 > buffer.Length)
                return false;

            int length = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset, 4));
            offset += 4;

            if (offset + length > buffer.Length)
                return false;

            byte[] argData = buffer.Slice(offset, length).ToArray();
            args.Add(argData);
            offset += length;
        }

        return true;
    }

    public static byte[] SerializeResponse(params object[] fields)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write((byte)fields.Length);

        foreach (var field in fields)
        {
            switch (field)
            {
                case byte b:
                    writer.Write(1);
                    writer.Write(b);
                    break;
                case int i:
                    writer.Write(4);
                    writer.Write(i);
                    break;
                case uint u:
                    writer.Write(4);
                    writer.Write(u);
                    break;
                case long l:
                    writer.Write(8);
                    writer.Write(l);
                    break;
                case ulong ul:
                    writer.Write(8);
                    writer.Write(ul);
                    break;
                case string str:
                    byte[] strBytes = Encoding.Unicode.GetBytes(str + "\0");
                    writer.Write(strBytes.Length);
                    writer.Write(strBytes);
                    break;
                case byte[] raw:
                    writer.Write(raw.Length);
                    writer.Write(raw);
                    break;
                default:
                    throw new ArgumentException($"Unsupported response field type: {field.GetType()}");
            }
        }

        return ms.ToArray();
    }

    public static bool TryDeserializeResponse(ReadOnlySpan<byte> buffer, out List<byte[]> fields)
    {
        fields = new List<byte[]>();
        if (buffer.Length < 1)
            return false;

        byte count = buffer[0];
        int offset = 1;

        for (int i = 0; i < count; i++)
        {
            if (offset + 4 > buffer.Length)
                return false;

            int length = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset, 4));
            offset += 4;

            if (offset + length > buffer.Length)
                return false;

            fields.Add(buffer.Slice(offset, length).ToArray());
            offset += length;
        }

        return true;
    }
}
