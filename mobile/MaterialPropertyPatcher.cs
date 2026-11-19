using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using AssetStudio;

namespace HoKTool.Android;

public static class MaterialPropertyPatcher
{
    public static string CreatePatchedCopy(Material material, string editJson, string outputDirectory)
    {
        if (material == null) throw new ArgumentNullException(nameof(material));
        var root = JObject.Parse(editJson ?? "{}");
        byte[] raw = material.GetRawData();
        if (raw.Length == 0 || raw.Length != material.byteSize)
            throw new InvalidDataException("Raw Material object không khớp byteSize.");
        byte[] patched = (byte[])raw.Clone();
        int changes = 0;

        if (root["ints"] is JObject ints)
        {
            foreach (var property in ints.Properties())
            {
                var matches = material.m_SavedProperties.m_Ints?.Where(x => x.Key == property.Name).ToList()
                    ?? new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>>();
                if (matches.Count != 1) throw new InvalidDataException($"Int property '{property.Name}' không tồn tại hoặc bị trùng.");
                int value = property.Value?.Value<int>() ?? throw new InvalidDataException($"Giá trị int '{property.Name}' không hợp lệ.");
                byte[] oldBytes = EncodeInt(matches[0].Value, material.reader.Endian);
                byte[] newBytes = EncodeInt(value, material.reader.Endian);
                PatchAlignedProperty(patched, property.Name, oldBytes, newBytes, material.reader.Endian);
                changes++;
            }
        }

        if (root["floats"] is JObject floats)
        {
            foreach (var property in floats.Properties())
            {
                var matches = material.m_SavedProperties.m_Floats.Where(x => x.Key == property.Name).ToList();
                if (matches.Count != 1) throw new InvalidDataException($"Float property '{property.Name}' không tồn tại hoặc bị trùng.");
                float value = property.Value?.Value<float>() ?? throw new InvalidDataException($"Giá trị float '{property.Name}' không hợp lệ.");
                if (!float.IsFinite(value)) throw new InvalidDataException($"Float '{property.Name}' phải hữu hạn.");
                byte[] oldBytes = EncodeFloat(matches[0].Value, material.reader.Endian);
                byte[] newBytes = EncodeFloat(value, material.reader.Endian);
                PatchAlignedProperty(patched, property.Name, oldBytes, newBytes, material.reader.Endian);
                changes++;
            }
        }

        if (root["colors"] is JObject colors)
        {
            foreach (var property in colors.Properties())
            {
                var matches = material.m_SavedProperties.m_Colors.Where(x => x.Key == property.Name).ToList();
                if (matches.Count != 1) throw new InvalidDataException($"Color property '{property.Name}' không tồn tại hoặc bị trùng.");
                if (property.Value is not JArray values || values.Count != 4)
                    throw new InvalidDataException($"Color '{property.Name}' phải có đúng 4 số [R,G,B,A].");
                float[] rgba = values.Select(v => v.Value<float>()).ToArray();
                if (rgba.Any(v => !float.IsFinite(v))) throw new InvalidDataException($"Color '{property.Name}' chứa giá trị không hữu hạn.");
                var old = matches[0].Value;
                byte[] oldBytes = EncodeColor(old.R, old.G, old.B, old.A, material.reader.Endian);
                byte[] newBytes = EncodeColor(rgba[0], rgba[1], rgba[2], rgba[3], material.reader.Endian);
                PatchAlignedProperty(patched, property.Name, oldBytes, newBytes, material.reader.Endian);
                changes++;
            }
        }

        if (changes == 0) throw new InvalidOperationException("Không có float/color property nào được thay đổi.");
        return SerializedObjectPatcher.CreatePatchedCopy(material, raw, patched, outputDirectory);
    }

    private static void PatchAlignedProperty(byte[] raw, string name, byte[] expected, byte[] replacement, EndianType endian)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        byte[] length = BitConverter.GetBytes(nameBytes.Length);
        if (endian == EndianType.BigEndian) Array.Reverse(length);
        int found = -1;
        for (int i = 0; i <= raw.Length - 4 - nameBytes.Length; i++)
        {
            if (!raw.AsSpan(i, 4).SequenceEqual(length)) continue;
            if (!raw.AsSpan(i + 4, nameBytes.Length).SequenceEqual(nameBytes)) continue;
            int valueOffset = checked((i + 4 + nameBytes.Length + 3) & ~3);
            if (valueOffset > raw.Length - expected.Length) continue;
            if (!raw.AsSpan(valueOffset, expected.Length).SequenceEqual(expected)) continue;
            if (found >= 0) throw new InvalidDataException($"Property '{name}' có nhiều vị trí nhị phân khớp; từ chối patch mơ hồ.");
            found = valueOffset;
        }
        if (found < 0) throw new InvalidDataException($"Không xác thực được vị trí nhị phân của property '{name}'.");
        Buffer.BlockCopy(replacement, 0, raw, found, replacement.Length);
    }

    private static byte[] EncodeInt(int value, EndianType endian)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (endian == EndianType.BigEndian) Array.Reverse(bytes);
        return bytes;
    }

    private static byte[] EncodeFloat(float value, EndianType endian)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (endian == EndianType.BigEndian) Array.Reverse(bytes);
        return bytes;
    }

    private static byte[] EncodeColor(float r, float g, float b, float a, EndianType endian)
    {
        byte[] bytes = new byte[16];
        Buffer.BlockCopy(EncodeFloat(r, endian), 0, bytes, 0, 4);
        Buffer.BlockCopy(EncodeFloat(g, endian), 0, bytes, 4, 4);
        Buffer.BlockCopy(EncodeFloat(b, endian), 0, bytes, 8, 4);
        Buffer.BlockCopy(EncodeFloat(a, endian), 0, bytes, 12, 4);
        return bytes;
    }
}
