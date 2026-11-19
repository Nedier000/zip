using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using AssetStudio;

namespace HoKTool.Android;

/// <summary>
/// Conservative OBJ position importer. It changes only float32 position channels
/// in inline modern Unity vertex buffers; topology, UVs, normals, weights and rig
/// data remain byte-for-byte unchanged.
/// </summary>
public static class MeshPositionPatcher
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static string CreatePatchedCopy(Mesh mesh, string objPath, string outputDirectory)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (mesh.m_VertexCount <= 0) throw new InvalidDataException("Mesh không có vertex hợp lệ.");

        var vertexData = typeof(Mesh).GetField("m_VertexData", Fields)?.GetValue(mesh)
            ?? throw new NotSupportedException("Mesh dùng CompressedMesh hoặc layout cũ; importer này chỉ hỗ trợ vertex buffer inline hiện đại.");
        var streamData = typeof(Mesh).GetField("m_StreamData", Fields)?.GetValue(mesh);
        string externalPath = (string?)streamData?.GetType().GetField("path", Fields)?.GetValue(streamData) ?? "";
        if (!string.IsNullOrEmpty(externalPath))
            throw new NotSupportedException("Mesh dùng vertex data trong .resS ngoài bundle; chưa nhập external stream.");

        var rawData = (byte[]?)vertexData.GetType().GetField("m_DataSize", Fields)?.GetValue(vertexData)
            ?? throw new InvalidDataException("Không đọc được vertex buffer.");
        var channels = (IList?)vertexData.GetType().GetField("m_Channels", Fields)?.GetValue(vertexData)
            ?? throw new InvalidDataException("Không đọc được vertex channels.");
        var streams = (IList?)vertexData.GetType().GetField("m_Streams", Fields)?.GetValue(vertexData)
            ?? throw new InvalidDataException("Không đọc được vertex streams.");

        if (channels.Count == 0 || streams.Count == 0)
            throw new InvalidDataException("Mesh không có vertex channel/stream.");
        object? positionChannel = channels[0];
        if (positionChannel == null) throw new InvalidDataException("Position channel không hợp lệ.");
        byte format = Read<byte>(positionChannel, "format");
        byte dimension = Read<byte>(positionChannel, "dimension");
        byte streamIndex = Read<byte>(positionChannel, "stream");
        byte channelOffset = Read<byte>(positionChannel, "offset");
        if (format != 0 || dimension < 3)
            throw new NotSupportedException($"Position channel phải là float32 x3/x4; format={format}, dimension={dimension}.");

        if (streamIndex >= streams.Count) throw new InvalidDataException("Position channel trỏ tới stream ngoài phạm vi.");
        object stream = streams[streamIndex]!;
        uint streamOffset = Read<uint>(stream, "offset");
        uint stride = Read<uint>(stream, "stride");
        if (stride < (uint)(channelOffset + dimension * 4))
            throw new InvalidDataException("Vertex stride không đủ chứa position channel.");

        var positions = ReadObjPositions(objPath);
        if (positions.Count != mesh.m_VertexCount)
            throw new InvalidDataException($"OBJ có {positions.Count} vertex; Mesh gốc có {mesh.m_VertexCount}. Phải giữ nguyên thứ tự và số vertex.");
        if (rawData.Length == 0) throw new InvalidDataException("Vertex buffer rỗng.");

        byte[] patchedData = (byte[])rawData.Clone();
        for (int i = 0; i < positions.Count; i++)
        {
            long offset = (long)streamOffset + channelOffset + (long)stride * i;
            if (offset < 0 || offset + 12 > patchedData.Length)
                throw new InvalidDataException($"Vertex {i} nằm ngoài vertex buffer.");
            // OBJ exporter flips X for Unity-to-OBJ handedness conversion.
            WriteFloat(patchedData, (int)offset, -positions[i].X, mesh.reader.Endian);
            WriteFloat(patchedData, (int)offset + 4, positions[i].Y, mesh.reader.Endian);
            WriteFloat(patchedData, (int)offset + 8, positions[i].Z, mesh.reader.Endian);
        }

        byte[] rawObject = mesh.GetRawData();
        if (rawObject.Length != mesh.byteSize)
            throw new InvalidDataException("Raw Mesh object không khớp byteSize.");
        byte[] patchedObject = SerializedObjectPatcher.ReplaceUniqueByteArray(
            rawObject, rawData, patchedData, mesh.reader.Endian);
        return SerializedObjectPatcher.CreatePatchedCopy(mesh, rawObject, patchedObject, outputDirectory);
    }

    private static List<(float X, float Y, float Z)> ReadObjPositions(string path)
    {
        var result = new List<(float, float, float)>();
        foreach (string line in File.ReadLines(path))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("v ", StringComparison.Ordinal)) continue;
            string[] parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) throw new InvalidDataException("OBJ có dòng vertex không đủ 3 tọa độ.");
            float x = float.Parse(parts[1], CultureInfo.InvariantCulture);
            float y = float.Parse(parts[2], CultureInfo.InvariantCulture);
            float z = float.Parse(parts[3], CultureInfo.InvariantCulture);
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                throw new InvalidDataException("OBJ chứa tọa độ không hữu hạn.");
            result.Add((x, y, z));
        }
        if (result.Count == 0) throw new InvalidDataException("Không tìm thấy dòng vertex 'v' trong OBJ.");
        return result;
    }

    private static T Read<T>(object instance, string field)
    {
        object? value = instance.GetType().GetField(field, Fields)?.GetValue(instance);
        if (value is T typed) return typed;
        throw new InvalidDataException($"Không đọc được Mesh field {field}.");
    }

    private static void WriteFloat(byte[] data, int offset, float value, EndianType endian)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (endian == EndianType.BigEndian) Array.Reverse(bytes);
        Buffer.BlockCopy(bytes, 0, data, offset, 4);
    }
}
