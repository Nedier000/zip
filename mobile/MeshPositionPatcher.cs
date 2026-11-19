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
/// Conservative OBJ importer. It changes float32 position, normal and UV0 channels
/// in inline modern Unity vertex buffers; topology, weights and rig data remain unchanged.
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
        if (rawData.Length == 0) throw new InvalidDataException("Vertex buffer rỗng.");

        var positions = ReadObjPositions(objPath, "v", 3);
        if (positions.Count != mesh.m_VertexCount)
            throw new InvalidDataException($"OBJ có {positions.Count} vertex; Mesh gốc có {mesh.m_VertexCount}. Phải giữ nguyên thứ tự và số vertex.");

        byte[] patchedData = (byte[])rawData.Clone();
        PatchChannel(patchedData, channels, streams, 0, positions, 3, flipX: true, mesh.reader.Endian);

        var normals = ReadObjPositions(objPath, "vn", 3);
        if (normals.Count > 0)
        {
            if (normals.Count != mesh.m_VertexCount)
                throw new InvalidDataException($"OBJ có {normals.Count} normal; Mesh gốc có {mesh.m_VertexCount} vertex.");
            if (mesh.m_Normals == null || mesh.m_Normals.Length < mesh.m_VertexCount * 3)
                throw new NotSupportedException("OBJ có vn nhưng Mesh không có normal channel tương ứng.");
            PatchChannel(patchedData, channels, streams, 1, normals, 3, flipX: true, mesh.reader.Endian);
        }

        var uvs = ReadObjPositions(objPath, "vt", 2);
        if (uvs.Count > 0)
        {
            if (uvs.Count != mesh.m_VertexCount)
                throw new InvalidDataException($"OBJ có {uvs.Count} UV; Mesh gốc có {mesh.m_VertexCount} vertex.");
            if (mesh.m_UV0 == null || mesh.m_UV0.Length < mesh.m_VertexCount * 2)
                throw new NotSupportedException("OBJ có vt nhưng Mesh không có UV0 channel tương ứng.");
            int uvChannel = mesh.version[0] >= 2018 ? 4 : 3;
            PatchChannel(patchedData, channels, streams, uvChannel, uvs, 2, flipX: false, mesh.reader.Endian);
        }

        byte[] rawObject = mesh.GetRawData();
        if (rawObject.Length != mesh.byteSize)
            throw new InvalidDataException("Raw Mesh object không khớp byteSize.");
        byte[] patchedObject = SerializedObjectPatcher.ReplaceUniqueByteArray(
            rawObject, rawData, patchedData, mesh.reader.Endian);
        return SerializedObjectPatcher.CreatePatchedCopy(mesh, rawObject, patchedObject, outputDirectory);
    }

    private static List<(float X, float Y, float Z)> ReadObjPositions(string path, string tag, int components)
    {
        var result = new List<(float, float, float)>();
        string prefix = tag + " ";
        foreach (string line in File.ReadLines(path))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string[] parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < components + 1)
                throw new InvalidDataException($"OBJ có dòng {tag} không đủ tọa độ.");
            float x = float.Parse(parts[1], CultureInfo.InvariantCulture);
            float y = float.Parse(parts[2], CultureInfo.InvariantCulture);
            float z = components >= 3 ? float.Parse(parts[3], CultureInfo.InvariantCulture) : 0f;
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                throw new InvalidDataException($"OBJ chứa tọa độ {tag} không hữu hạn.");
            result.Add((x, y, z));
        }
        if (tag == "v" && result.Count == 0)
            throw new InvalidDataException("Không tìm thấy dòng vertex 'v' trong OBJ.");
        return result;
    }

    private static void PatchChannel(
        byte[] data, IList channels, IList streams, int channelIndex,
        List<(float X, float Y, float Z)> values, int components, bool flipX, EndianType endian)
    {
        if (channelIndex < 0 || channelIndex >= channels.Count || channels[channelIndex] == null)
            throw new NotSupportedException($"Mesh không có channel {channelIndex} cần thiết cho OBJ.");
        object channel = channels[channelIndex]!;
        byte format = Read<byte>(channel, "format");
        byte dimension = Read<byte>(channel, "dimension");
        byte streamIndex = Read<byte>(channel, "stream");
        byte channelOffset = Read<byte>(channel, "offset");
        if (format != 0 || dimension < components)
            throw new NotSupportedException($"Channel {channelIndex} phải là float32 x{components} trở lên; format={format}, dimension={dimension}.");
        if (streamIndex >= streams.Count) throw new InvalidDataException($"Channel {channelIndex} trỏ tới stream ngoài phạm vi.");

        object stream = streams[streamIndex]!;
        uint streamOffset = Read<uint>(stream, "offset");
        uint stride = Read<uint>(stream, "stride");
        if (stride < (uint)(channelOffset + dimension * 4))
            throw new InvalidDataException($"Stride của channel {channelIndex} không hợp lệ.");

        for (int i = 0; i < values.Count; i++)
        {
            long offset = (long)streamOffset + channelOffset + (long)stride * i;
            if (offset < 0 || offset + components * 4 > data.Length)
                throw new InvalidDataException($"Vertex {i} của channel {channelIndex} nằm ngoài vertex buffer.");
            float x = flipX ? -values[i].X : values[i].X;
            WriteFloat(data, (int)offset, x, endian);
            WriteFloat(data, (int)offset + 4, values[i].Y, endian);
            if (components >= 3) WriteFloat(data, (int)offset + 8, values[i].Z, endian);
        }
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
