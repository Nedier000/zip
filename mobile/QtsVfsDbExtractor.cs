using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using K4os.Compression.LZ4;
using ZstdSharp;

namespace HoKTool.Android;

/// <summary>
/// Reader for the modern HoK QTS VFS .db format. Its first four bytes are
/// 01 00 00 02, which collides with the legacy QuickBMS index magic; use the
/// full eight-byte signature before routing to the legacy parser.
/// </summary>
public static class QtsVfsDbExtractor
{
    private static readonly byte[] Magic = { 0x01, 0x00, 0x00, 0x02, 0x01, 0x02, 0x03, 0x04 };
    private const int MaxTableRecords = 1_000_000;
    private const int MaxChunkBytes = 512 * 1024 * 1024;

    private readonly record struct Table(int A, int B, int C);
    private readonly record struct Chunk(long Offset, int CompressedSize, int UncompressedSize, int MainBlock, int SubBlock);
    private readonly record struct Block(int Offset, ushort EntryCount, ushort Type);

    public static bool IsQtsVfsFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < Magic.Length) return false;
            Span<byte> header = stackalloc byte[8];
            stream.ReadExactly(header);
            return header.SequenceEqual(Magic);
        }
        catch { return false; }
    }

    public static bool ContainsQtsVfsFile(string directory) =>
        Directory.Exists(directory) &&
        Directory.EnumerateFiles(directory, "*.db", SearchOption.TopDirectoryOnly).Any(IsQtsVfsFile);

    public static async Task<DbIndexExtractor.Result> ExtractAsync(
        string inputDirectory, string outputDirectory,
        IProgress<DbIndexExtractor.Progress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var files = Directory.EnumerateFiles(inputDirectory, "*.db", SearchOption.TopDirectoryOnly)
            .Where(IsQtsVfsFile)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
            throw new FileNotFoundException("Không tìm thấy QTS VFS .db có chữ ký 01 00 00 02 01 02 03 04.");

        Directory.CreateDirectory(outputDirectory);
        int entriesSeen = 0, filesWritten = 0, skipped = 0;
        long bytesWritten = 0;
        var warnings = new List<string>();
        var manifest = new StringBuilder();
        manifest.AppendLine("source_db,file_id,chunks,decoded_bytes,codecs,status");

        foreach (string path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dbName = Path.GetFileNameWithoutExtension(path);
            string dbOutput = Path.Combine(outputDirectory, SafeName(dbName));
            Directory.CreateDirectory(dbOutput);
            using var reader = new BinaryReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            var entries = ReadIndex(reader);
            entriesSeen += entries.Count;

            int current = 0;
            foreach (var pair in entries.OrderBy(x => x.Key))
            {
                cancellationToken.ThrowIfCancellationRequested();
                current++;
                string id = pair.Key.ToString("X16");
                string output = Path.Combine(dbOutput, id + ".bin");
                string temp = output + ".partial";
                long total = 0;
                var codecs = new List<string>();
                try
                {
                    using (var destination = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
                    {
                        foreach (var chunk in pair.Value.OrderBy(c => c.MainBlock).ThenBy(c => c.SubBlock).ThenBy(c => c.Offset))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            byte[] packed = ReadAt(reader, chunk.Offset, chunk.CompressedSize);
                            string codec;
                            byte[] decoded = DecodeChunk(pair.Key, chunk, packed, out codec);
                            if (total > long.MaxValue - decoded.LongLength)
                                throw new InvalidDataException("Tổng payload vượt giới hạn FileStream 64-bit.");
                            await destination.WriteAsync(decoded, cancellationToken);
                            total += decoded.LongLength;
                            codecs.Add(codec);
                        }
                        await destination.FlushAsync(cancellationToken);
                    }
                    File.Move(temp, output, overwrite: true);
                    filesWritten++;
                    bytesWritten = checked(bytesWritten + total);
                    manifest.AppendLine($"{Escape(dbName)},{id},{pair.Value.Count},{total},{Escape(string.Join("+", codecs.Distinct()))},OK");
                }
                catch (Exception ex)
                {
                    skipped++;
                    if (File.Exists(temp)) File.Delete(temp);
                    warnings.Add($"{dbName}/{id}: {ex.GetBaseException().Message}");
                    manifest.AppendLine($"{Escape(dbName)},{id},{pair.Value.Count},{total},{Escape(string.Join("+", codecs.Distinct()))},ERROR");
                }
                progress?.Report(new DbIndexExtractor.Progress(entriesSeen, filesWritten, Path.GetFileName(path),
                    $"QTS VFS {dbName}: {current}/{entries.Count} entry · {filesWritten} file · {FormatBytes(bytesWritten)}"));
            }
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "qts_manifest.csv"),
            manifest.ToString(), new UTF8Encoding(false), cancellationToken);
        return new DbIndexExtractor.Result(entriesSeen, filesWritten, bytesWritten, skipped, warnings);
    }

    private static Dictionary<ulong, List<Chunk>> ReadIndex(BinaryReader reader)
    {
        var stream = reader.BaseStream;
        if (stream.Length < 80) throw new InvalidDataException("QTS VFS header bị cắt cụt.");
        stream.Position = 0;
        byte[] signature = reader.ReadBytes(8);
        if (!signature.SequenceEqual(Magic)) throw new InvalidDataException("Sai chữ ký QTS VFS.");
        int size = reader.ReadInt32();
        int fullSize = reader.ReadInt32();
        // The fields are header metadata, not per-entry allocation sizes.
        if (size < 0 || fullSize < 0)
            throw new InvalidDataException($"QTS VFS header size không hợp lệ: size={size}, fullSize={fullSize}.");

        int[] pairs = new int[16];
        for (int i = 0; i < pairs.Length; i++) pairs[i] = reader.ReadInt32();
        // Layout after magic/size/fullSize:
        // Unknown0, Unknown1, Index, IndexData, Entries1, Entries2, Entries3, Unknown2.
        int entries2Offset = pairs[10];
        int entries3Offset = pairs[12];
        var tables = ReadTables(reader, entries2Offset).Concat(ReadTables(reader, entries3Offset)).ToArray();
        var blockOffsets = new HashSet<int>();
        foreach (var table in tables)
        {
            AddOffset(blockOffsets, table.A);
            AddOffset(blockOffsets, table.B);
            AddOffset(blockOffsets, table.C);
        }

        var blocks = new List<Block>();
        foreach (int offset in blockOffsets)
        {
            EnsureRange(stream, offset + 4080L, 16, "QTS VFS entry block header");
            stream.Position = offset + 4080L;
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
            ushort count = reader.ReadUInt16();
            ushort type = reader.ReadUInt16();
            if (count > 2040) throw new InvalidDataException($"Block {offset:X}: EntryCount bất thường {count}.");
            blocks.Add(new Block(offset, count, type));
        }

        var recordOffsets = new HashSet<int>();
        foreach (var block in blocks.Where(b => b.Type != 2))
        {
            EnsureRange(stream, block.Offset + 2040L, block.EntryCount * 4L, "QTS VFS entry offset table");
            stream.Position = block.Offset + 2040L;
            for (int i = 0; i < block.EntryCount; i++)
            {
                int recordOffset = reader.ReadInt32();
                if (recordOffset >= 0) recordOffsets.Add(recordOffset);
            }
        }

        var result = new Dictionary<ulong, List<Chunk>>();
        var pending = new Queue<int>(recordOffsets.OrderBy(x => x));
        var visited = new HashSet<int>();
        while (pending.Count > 0)
        {
            int offset = pending.Dequeue();
            if (!visited.Add(offset)) continue;
            EnsureRange(stream, offset, 32, "QTS VFS entry record");
            stream.Position = offset;
            int self = reader.ReadInt32();
            if (self != offset) throw new InvalidDataException($"QTS VFS record self-offset mismatch at 0x{offset:X}.");
            int next = reader.ReadInt32();
            if (next != -1 && next >= 0 && !visited.Contains(next)) pending.Enqueue(next);

            stream.Position = checked(offset + 24L);
            int compressedField = reader.ReadInt32();
            if (compressedField < 4) throw new InvalidDataException($"QTS record 0x{offset:X}: compressed-size field invalid {compressedField}.");
            int compressedSize = checked(compressedField - 4);
            int uncompressedSize = reader.ReadInt32();
            if (compressedSize <= 0 || compressedSize > MaxChunkBytes)
                throw new InvalidDataException($"QTS record 0x{offset:X}: compressed chunk size invalid {compressedSize}.");
            if (uncompressedSize < 0 || uncompressedSize > MaxChunkBytes)
                throw new InvalidDataException($"QTS record 0x{offset:X}: decoded chunk size invalid {uncompressedSize}.");
            long dataOffset = stream.Position;
            EnsureRange(stream, dataOffset, compressedSize, "QTS VFS compressed payload");
            long trailer = Align4(checked(dataOffset + compressedSize));
            EnsureRange(stream, trailer, 16, "QTS VFS payload identifier");
            stream.Position = trailer;
            ulong id = reader.ReadUInt64();
            int mainBlock = reader.ReadInt32();
            int subBlock = reader.ReadInt32();
            if (!result.TryGetValue(id, out var chunks)) result.Add(id, chunks = new List<Chunk>());
            chunks.Add(new Chunk(dataOffset, compressedSize, uncompressedSize, mainBlock, subBlock));
        }
        return result;
    }

    private static IEnumerable<Table> ReadTables(BinaryReader reader, int offset)
    {
        var stream = reader.BaseStream;
        if (offset == -1) return Array.Empty<Table>();
        EnsureRange(stream, offset, 24, "QTS VFS table header");
        stream.Position = offset;
        _ = reader.ReadInt32(); // capacity/max
        int count = reader.ReadInt32();
        stream.Position += 16;
        if (count < 0 || count > MaxTableRecords)
            throw new InvalidDataException($"QTS VFS table count không hợp lệ: {count}.");
        EnsureRange(stream, stream.Position, count * 24L, "QTS VFS table records");
        var tables = new List<Table>(count);
        for (int i = 0; i < count; i++)
        {
            int a = reader.ReadInt32();
            int b = reader.ReadInt32();
            int c = reader.ReadInt32();
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
            tables.Add(new Table(a, b, c));
        }
        return tables;
    }

    private static byte[] DecodeChunk(ulong fileId, Chunk chunk, byte[] packed, out string codec)
    {
        bool indexRecord = chunk.UncompressedSize <= 1 && packed.Length >= 40 &&
            (packed.Length - 40) % 12 == 0 &&
            ReadUInt64LE(packed, 4) == fileId &&
            ReadUInt32LE(packed, 36) == (uint)((packed.Length - 40) / 12);
        if (indexRecord)
        {
            codec = "StoredIndex";
            return packed;
        }
        if (packed.AsSpan().StartsWith(Encoding.ASCII.GetBytes("QTSF_PACKAGE")) || chunk.UncompressedSize == 0)
        {
            codec = "StoredMetadata";
            return packed;
        }
        if (chunk.UncompressedSize > MaxChunkBytes)
            throw new InvalidDataException($"Uncompressed chunk exceeds {MaxChunkBytes} byte safety limit.");
        byte[] result;
        if (packed.Length == chunk.UncompressedSize)
        {
            codec = "Stored";
            return packed;
        }
        result = new byte[chunk.UncompressedSize];
        int decoded;
        if (packed.Length >= 4 && packed[0] == 0x28 && packed[1] == 0xB5 && packed[2] == 0x2F && packed[3] == 0xFD)
        {
            codec = "Zstd";
            using var zstd = new Decompressor();
            int n = zstd.Unwrap(packed, 0, packed.Length, result, 0, result.Length);
            if (n != result.Length) throw new InvalidDataException($"Zstd decoded {n}, expected {result.Length}.");
            return result;
        }
        if (packed.Length >= 2 && (packed[0] == 0x8C || packed[0] == 0xCC) && packed[1] == 0x0C)
        {
            codec = "Oodle";
            int n = DbIndexExtractor.DecompressOodleChunk(packed, result);
            if (n != result.Length) throw new InvalidDataException($"Oodle decoded {n}, expected {result.Length}.");
            return result;
        }
        codec = "LZ4";
        int lz = LZ4Codec.Decode(packed, 0, packed.Length, result, 0, result.Length);
        if (lz != result.Length) throw new InvalidDataException($"LZ4 decoded {lz}, expected {result.Length}.");
        return result;
    }

    private static byte[] ReadAt(BinaryReader reader, long offset, int size)
    {
        EnsureRange(reader.BaseStream, offset, size, "QTS VFS chunk read");
        reader.BaseStream.Position = offset;
        byte[] data = reader.ReadBytes(size);
        if (data.Length != size) throw new EndOfStreamException("QTS VFS chunk payload bị cắt cụt.");
        return data;
    }

    private static void EnsureRange(Stream stream, long offset, long count, string description)
    {
        if (offset < 0 || count < 0 || offset > stream.Length - count)
            throw new InvalidDataException($"{description}: out of range ({offset}+{count}/{stream.Length}).");
    }
    private static long Align4(long value) => (value + 3L) & ~3L;
    private static void AddOffset(HashSet<int> values, int value) { if (value >= 0) values.Add(value); }
    private static ulong ReadUInt64LE(byte[] data, int offset) => BitConverter.ToUInt64(data, offset);
    private static uint ReadUInt32LE(byte[] data, int offset) => BitConverter.ToUInt32(data, offset);
    private static string SafeName(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    private static string Escape(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static string FormatBytes(long value) => value < 1024 ? value + " B" : value < 1024 * 1024 ? $"{value / 1024d:0.0} KB" : $"{value / 1024d / 1024:0.00} MB";
}
