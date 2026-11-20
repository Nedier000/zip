using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using AssetStudio;

namespace HoKTool.Android;

/// <summary>
/// Managed glTF 2.0 binary exporter. It consumes Studio-HoK's IImported model,
/// so geometry, node transforms, skin weights, inverse bind matrices and
/// converted animation curves come from the same ModelConverter as desktop FBX.
/// It deliberately does not call the Windows FBX native wrapper.
/// </summary>
public sealed class StudioGlbExporter
{
    private readonly MemoryStream binary = new();
    private readonly BinaryWriter writer;
    private readonly List<object> bufferViews = new();
    private readonly List<object> accessors = new();
    private readonly List<object> nodes = new();
    private readonly List<object> meshes = new();
    private readonly List<object> materials = new();
    private readonly List<object> skins = new();
    private readonly List<object> animations = new();
    private readonly List<object> images = new();
    private readonly List<object> textures = new();
    private readonly Dictionary<string, int> materialIndices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> textureIndices = new(StringComparer.Ordinal);
    private readonly Dictionary<ImportedFrame, int> frameIndices = new();
    private readonly Dictionary<string, int> framePathIndices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> textureBytes = new(StringComparer.Ordinal);

    public StudioGlbExporter()
    {
        writer = new BinaryWriter(binary, Encoding.UTF8, leaveOpen: true);
    }

    public void Export(string outputPath, IImported imported, bool exportSkins = true, bool exportAnimations = true, bool exportMaterials = true)
    {
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path is empty.", nameof(outputPath));
        if (imported == null || imported.RootFrame == null) throw new ArgumentException("Imported scene is empty.", nameof(imported));
        if (imported.MeshList == null || imported.MeshList.Count == 0) throw new InvalidDataException("The selected GameObject has no mesh.");

        foreach (var texture in imported.TextureList ?? new List<ImportedTexture>())
            if (!string.IsNullOrWhiteSpace(texture.Name) && texture.Data?.Length > 0)
                textureBytes[texture.Name] = texture.Data;

        var rootNodeIndices = new List<int>();
        AddFrame(imported.RootFrame, rootNodeIndices);

        foreach (var sourceMesh in imported.MeshList)
        {
            if (sourceMesh?.VertexList == null || sourceMesh.VertexList.Count == 0) continue;
            AddImportedMesh(imported, sourceMesh, exportSkins, exportMaterials);
        }

        if (meshes.Count == 0) throw new InvalidDataException("No valid mesh primitives could be exported.");
        if (exportAnimations) AddAnimations(imported);

        var gltf = new Dictionary<string, object?>
        {
            ["asset"] = new Dictionary<string, object>
            {
                ["version"] = "2.0",
                ["generator"] = "Studio-HoK Android managed GLB exporter"
            },
            ["scene"] = 0,
            ["scenes"] = new object[]
            {
                new Dictionary<string, object> { ["nodes"] = rootNodeIndices }
            },
            ["nodes"] = nodes,
            ["meshes"] = meshes,
            ["bufferViews"] = bufferViews,
            ["accessors"] = accessors,
            ["buffers"] = new object[]
            {
                new Dictionary<string, object> { ["byteLength"] = Align4((int)binary.Length) }
            }
        };

        if (materials.Count > 0) gltf["materials"] = materials;
        if (skins.Count > 0) gltf["skins"] = skins;
        if (animations.Count > 0) gltf["animations"] = animations;
        if (images.Count > 0)
        {
            gltf["images"] = images;
            gltf["textures"] = textures;
            gltf["samplers"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["magFilter"] = 9729, ["minFilter"] = 9987,
                    ["wrapS"] = 10497, ["wrapT"] = 10497
                }
            };
        }

        WriteGlb(outputPath, gltf);
    }

    private int AddFrame(ImportedFrame frame, List<int> roots)
    {
        int index = nodes.Count;
        frameIndices[frame] = index;
        if (!string.IsNullOrEmpty(frame.Path)) framePathIndices[frame.Path] = index;

        var node = new Dictionary<string, object?>
        {
            ["name"] = SafeName(frame.Name),
            ["translation"] = new[] { frame.LocalPosition.X, frame.LocalPosition.Y, frame.LocalPosition.Z },
            ["rotation"] = new[] { frame.LocalRotation.X, frame.LocalRotation.Y, frame.LocalRotation.Z, frame.LocalRotation.W },
            ["scale"] = new[] { frame.LocalScale.X, frame.LocalScale.Y, frame.LocalScale.Z }
        };
        nodes.Add(node);

        var children = new List<int>();
        for (int i = 0; i < frame.Count; i++) AddFrame(frame[i], children);
        if (children.Count > 0) node["children"] = children;
        if (frame.Parent == null) roots.Add(index);
        return index;
    }

    private void AddImportedMesh(IImported imported, ImportedMesh source, bool exportSkins, bool exportMaterials)
    {
        var vertices = source.VertexList;
        int vertexCount = vertices.Count;
        var positions = new float[vertexCount * 3];
        var normals = source.hasNormal ? new float[vertexCount * 3] : null;
        var tangents = source.hasTangent ? new float[vertexCount * 4] : null;
        var colors = source.hasColor ? new float[vertexCount * 4] : null;

        for (int i = 0; i < vertexCount; i++)
        {
            var v = vertices[i];
            positions[i * 3] = v.Vertex.X;
            positions[i * 3 + 1] = v.Vertex.Y;
            positions[i * 3 + 2] = v.Vertex.Z;
            if (normals != null)
            {
                normals[i * 3] = v.Normal.X;
                normals[i * 3 + 1] = v.Normal.Y;
                normals[i * 3 + 2] = v.Normal.Z;
            }
            if (tangents != null)
            {
                tangents[i * 4] = v.Tangent.X;
                tangents[i * 4 + 1] = v.Tangent.Y;
                tangents[i * 4 + 2] = v.Tangent.Z;
                tangents[i * 4 + 3] = v.Tangent.W;
            }
            if (colors != null)
            {
                colors[i * 4] = v.Color.R;
                colors[i * 4 + 1] = v.Color.G;
                colors[i * 4 + 2] = v.Color.B;
                colors[i * 4 + 3] = v.Color.A;
            }
        }

        var attributes = new Dictionary<string, object>
        {
            ["POSITION"] = AddFloatAccessor(positions, 3, "VEC3", 34962, includeBounds: true)
        };
        if (normals != null) attributes["NORMAL"] = AddFloatAccessor(normals, 3, "VEC3", 34962);
        if (tangents != null) attributes["TANGENT"] = AddFloatAccessor(tangents, 4, "VEC4", 34962);
        if (colors != null) attributes["COLOR_0"] = AddFloatAccessor(colors, 4, "VEC4", 34962);

        if (source.hasUV != null)
        {
            for (int uv = 0; uv < Math.Min(8, source.hasUV.Length); uv++)
            {
                if (!source.hasUV[uv]) continue;
                var values = new List<float>(vertexCount * 2);
                bool valid = true;
                foreach (var vertex in vertices)
                {
                    if (vertex.UV == null || vertex.UV.Length <= uv || vertex.UV[uv] == null || vertex.UV[uv].Length < 2)
                    {
                        valid = false;
                        break;
                    }
                    values.Add(vertex.UV[uv][0]);
                    values.Add(vertex.UV[uv][1]);
                }
                if (valid && values.Count == vertexCount * 2)
                    attributes["TEXCOORD_" + uv] = AddFloatAccessor(values.ToArray(), 2, "VEC2", 34962);
            }
        }

        int? skinIndex = null;
        if (exportSkins && source.BoneList?.Count > 0)
        {
            skinIndex = AddSkin(source, vertices, attributes);
        }

        var primitives = new List<object>();
        foreach (var submesh in source.SubmeshList ?? new List<ImportedSubmesh>())
        {
            var indices = new List<uint>();
            foreach (var face in submesh.FaceList ?? new List<ImportedFace>())
            {
                if (face.VertexIndices == null || face.VertexIndices.Length < 3) continue;
                for (int k = 0; k < 3; k++)
                {
                    long index = (long)face.VertexIndices[k] + submesh.BaseVertex;
                    if (index < 0 || index >= vertexCount) throw new InvalidDataException("A mesh face references a vertex outside its vertex buffer.");
                    indices.Add((uint)index);
                }
            }
            if (indices.Count < 3) continue;

            var primitive = new Dictionary<string, object>
            {
                ["attributes"] = attributes,
                ["indices"] = AddUIntAccessor(indices.ToArray(), "SCALAR", 34963),
                ["mode"] = 4
            };
            if (exportMaterials && !string.IsNullOrEmpty(submesh.Material))
            {
                var materialIndex = GetMaterialIndex(imported, submesh.Material);
                if (materialIndex >= 0) primitive["material"] = materialIndex;
            }
            primitives.Add(primitive);
        }
        if (primitives.Count == 0) return;

        int meshIndex = meshes.Count;
        meshes.Add(new Dictionary<string, object>
        {
            ["name"] = SafeName(source.Path),
            ["primitives"] = primitives
        });

        int frameIndex = FindFrameIndex(source.Path);
        if (frameIndex < 0) frameIndex = 0;
        var meshNode = new Dictionary<string, object>
        {
            ["name"] = SafeName(PathTail(source.Path)) + "_Mesh",
            ["mesh"] = meshIndex
        };
        if (skinIndex.HasValue) meshNode["skin"] = skinIndex.Value;

        int meshNodeIndex = nodes.Count;
        nodes.Add(meshNode);
        var frameNode = (Dictionary<string, object?>)nodes[frameIndex];
        if (frameNode.TryGetValue("children", out var existing) && existing is List<int> children)
            children.Add(meshNodeIndex);
        else
            frameNode["children"] = new List<int> { meshNodeIndex };
    }

    private int AddSkin(ImportedMesh mesh, List<ImportedVertex> vertices, Dictionary<string, object> attributes)
    {
        var jointNodes = new List<int>();
        var inverseBind = new List<float>();
        var boneRemap = new Dictionary<int, ushort>();

        for (int i = 0; i < mesh.BoneList.Count; i++)
        {
            var bone = mesh.BoneList[i];
            int nodeIndex = FindFrameIndex(bone.Path);
            if (nodeIndex < 0) continue;
            boneRemap[i] = checked((ushort)jointNodes.Count);
            jointNodes.Add(nodeIndex);
            for (int j = 0; j < 16; j++) inverseBind.Add(bone.Matrix[j]);
        }
        if (jointNodes.Count == 0) return -1;

        var joints = new ushort[vertices.Count * 4];
        var weights = new float[vertices.Count * 4];
        for (int v = 0; v < vertices.Count; v++)
        {
            float sum = 0;
            for (int k = 0; k < 4; k++)
            {
                int sourceIndex = vertices[v].BoneIndices != null && vertices[v].BoneIndices.Length > k
                    ? vertices[v].BoneIndices[k] : -1;
                float weight = vertices[v].Weights != null && vertices[v].Weights.Length > k
                    ? Math.Max(0, vertices[v].Weights[k]) : 0;
                if (weight <= 0 || !boneRemap.TryGetValue(sourceIndex, out var mapped)) continue;
                joints[v * 4 + k] = mapped;
                weights[v * 4 + k] = weight;
                sum += weight;
            }
            if (sum > 0)
            {
                for (int k = 0; k < 4; k++) weights[v * 4 + k] /= sum;
            }
            else
            {
                joints[v * 4] = 0;
                weights[v * 4] = 1;
            }
        }

        attributes["JOINTS_0"] = AddUShortAccessor(joints, 4, "VEC4", 34962);
        attributes["WEIGHTS_0"] = AddFloatAccessor(weights, 4, "VEC4", 34962);
        int matrixAccessor = AddFloatAccessor(inverseBind.ToArray(), 16, "MAT4", 34962);
        int skeletonRoot = jointNodes[0];
        var skin = new Dictionary<string, object>
        {
            ["name"] = SafeName(mesh.Path) + "_Skin",
            ["joints"] = jointNodes,
            ["inverseBindMatrices"] = matrixAccessor,
            ["skeleton"] = skeletonRoot
        };
        int result = skins.Count;
        skins.Add(skin);
        return result;
    }

    private int GetMaterialIndex(IImported imported, string name)
    {
        if (materialIndices.TryGetValue(name, out int known)) return known;
        var material = imported.MaterialList?.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));
        if (material == null) return -1;

        var pbr = new Dictionary<string, object>
        {
            ["baseColorFactor"] = new[] { Clamp01(material.Diffuse.R), Clamp01(material.Diffuse.G), Clamp01(material.Diffuse.B), Clamp01(material.Diffuse.A) },
            ["metallicFactor"] = 0.0f,
            ["roughnessFactor"] = Math.Clamp(1f - material.Shininess / 100f, 0.05f, 1f)
        };
        var materialJson = new Dictionary<string, object>
        {
            ["name"] = SafeName(material.Name),
            ["pbrMetallicRoughness"] = pbr,
            ["doubleSided"] = true
        };

        foreach (var texture in material.Textures ?? new List<ImportedMaterialTexture>())
        {
            if (!textureIndices.TryGetValue(texture.Name ?? string.Empty, out int textureIndex))
            {
                var data = textureBytes.TryGetValue(texture.Name ?? string.Empty, out var bytes) ? bytes : null;
                if (data == null || data.Length == 0) continue;
                string extension = Path.GetExtension(texture.Name ?? string.Empty).ToLowerInvariant();
                string mime = extension is ".jpg" or ".jpeg" ? "image/jpeg" : extension == ".png" ? "image/png" : string.Empty;
                if (mime.Length == 0) continue;
                int view = AddRawBufferView(data, null);
                int imageIndex = images.Count;
                images.Add(new Dictionary<string, object>
                {
                    ["name"] = SafeName(texture.Name),
                    ["bufferView"] = view,
                    ["mimeType"] = mime
                });
                textureIndex = textures.Count;
                textures.Add(new Dictionary<string, object>
                {
                    ["sampler"] = 0,
                    ["source"] = imageIndex
                });
                textureIndices[texture.Name ?? string.Empty] = textureIndex;
            }

            var info = new Dictionary<string, object> { ["index"] = textureIndex };
            if (texture.Dest == 0) pbr["baseColorTexture"] = info;
            else if (texture.Dest == 1 || texture.Dest == 3) materialJson["normalTexture"] = info;
            else if (texture.Dest == 2) materialJson["metallicRoughnessTexture"] = info;
        }

        if (material.Transparency > 0) materialJson["alphaMode"] = "BLEND";
        int index = materials.Count;
        materials.Add(materialJson);
        materialIndices[name] = index;
        return index;
    }

    private void AddAnimations(IImported imported)
    {
        foreach (var animation in imported.AnimationList ?? new List<ImportedKeyframedAnimation>())
        {
            var samplers = new List<object>();
            var channels = new List<object>();
            foreach (var track in animation.TrackList ?? new List<ImportedAnimationKeyframedTrack>())
            {
                int nodeIndex = FindFrameIndex(track.Path);
                if (nodeIndex < 0) continue;

                AddAnimationChannel(track.Translations, "translation", 3, nodeIndex, samplers, channels,
                    v => new[] { v.X, v.Y, v.Z });
                AddAnimationChannel(track.Rotations, "rotation", 4, nodeIndex, samplers, channels,
                    v => new[] { v.X, v.Y, v.Z, v.W });
                AddAnimationChannel(track.Scalings, "scale", 3, nodeIndex, samplers, channels,
                    v => new[] { v.X, v.Y, v.Z });
            }
            if (channels.Count == 0) continue;
            animations.Add(new Dictionary<string, object>
            {
                ["name"] = SafeName(animation.Name),
                ["samplers"] = samplers,
                ["channels"] = channels
            });
        }
    }

    private void AddAnimationChannel<T>(
        List<ImportedKeyframe<T>>? keys,
        string targetPath,
        int componentCount,
        int nodeIndex,
        List<object> samplers,
        List<object> channels,
        Func<T, float[]> values)
    {
        if (keys == null || keys.Count == 0) return;
        var sorted = keys.Where(x => float.IsFinite(x.time))
            .OrderBy(x => x.time)
            .GroupBy(x => x.time)
            .Select(g => g.Last())
            .ToArray();
        if (sorted.Length == 0) return;

        var times = sorted.Select(x => Math.Max(0, x.time)).ToArray();
        var output = sorted.SelectMany(x => values(x.value)).ToArray();
        int inputAccessor = AddFloatAccessor(times, 1, "SCALAR", 34962, includeBounds: true);
        int outputAccessor = AddFloatAccessor(output, componentCount, componentCount == 4 ? "VEC4" : "VEC3", 34962);
        int samplerIndex = samplers.Count;
        samplers.Add(new Dictionary<string, object>
        {
            ["input"] = inputAccessor,
            ["output"] = outputAccessor,
            ["interpolation"] = "LINEAR"
        });
        channels.Add(new Dictionary<string, object>
        {
            ["sampler"] = samplerIndex,
            ["target"] = new Dictionary<string, object>
            {
                ["node"] = nodeIndex,
                ["path"] = targetPath
            }
        });
    }

    private int AddFloatAccessor(float[] data, int components, string type, int? target = null, bool includeBounds = false)
    {
        if (data.Length == 0 || data.Length % components != 0) throw new InvalidDataException("Invalid float accessor data.");
        AlignBinary();
        int offset = checked((int)binary.Position);
        foreach (float value in data) writer.Write(float.IsFinite(value) ? value : 0f);
        int length = checked((int)binary.Position - offset);
        int view = AddView(offset, length, target);
        int accessorIndex = accessors.Count;
        var accessor = new Dictionary<string, object>
        {
            ["bufferView"] = view,
            ["componentType"] = 5126,
            ["count"] = data.Length / components,
            ["type"] = type
        };
        if (includeBounds)
        {
            var min = new float[components];
            var max = new float[components];
            Array.Fill(min, float.PositiveInfinity);
            Array.Fill(max, float.NegativeInfinity);
            for (int i = 0; i < data.Length; i++)
            {
                int c = i % components;
                float v = float.IsFinite(data[i]) ? data[i] : 0f;
                if (v < min[c]) min[c] = v;
                if (v > max[c]) max[c] = v;
            }
            accessor["min"] = min;
            accessor["max"] = max;
        }
        accessors.Add(accessor);
        return accessorIndex;
    }

    private int AddUIntAccessor(uint[] data, string type, int? target)
    {
        AlignBinary();
        int offset = checked((int)binary.Position);
        foreach (uint value in data) writer.Write(value);
        int view = AddView(offset, checked((int)binary.Position - offset), target);
        int index = accessors.Count;
        accessors.Add(new Dictionary<string, object>
        {
            ["bufferView"] = view,
            ["componentType"] = 5125,
            ["count"] = data.Length,
            ["type"] = type
        });
        return index;
    }

    private int AddUShortAccessor(ushort[] data, int components, string type, int? target)
    {
        AlignBinary();
        int offset = checked((int)binary.Position);
        foreach (ushort value in data) writer.Write(value);
        int view = AddView(offset, checked((int)binary.Position - offset), target);
        int index = accessors.Count;
        accessors.Add(new Dictionary<string, object>
        {
            ["bufferView"] = view,
            ["componentType"] = 5123,
            ["count"] = data.Length / components,
            ["type"] = type
        });
        return index;
    }

    private int AddRawBufferView(byte[] data, int? target)
    {
        AlignBinary();
        int offset = checked((int)binary.Position);
        writer.Write(data);
        return AddView(offset, data.Length, target);
    }

    private int AddView(int offset, int length, int? target)
    {
        var view = new Dictionary<string, object>
        {
            ["buffer"] = 0,
            ["byteOffset"] = offset,
            ["byteLength"] = length
        };
        if (target.HasValue) view["target"] = target.Value;
        int index = bufferViews.Count;
        bufferViews.Add(view);
        return index;
    }

    private int FindFrameIndex(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return -1;
        if (framePathIndices.TryGetValue(path, out var index)) return index;
        var match = framePathIndices.FirstOrDefault(x => x.Key.EndsWith("/" + path, StringComparison.Ordinal) || path.EndsWith("/" + x.Key, StringComparison.Ordinal));
        return match.Equals(default(KeyValuePair<string, int>)) ? -1 : match.Value;
    }

    private void WriteGlb(string path, Dictionary<string, object?> gltf)
    {
        AlignBinary();
        gltf["buffers"] = new object[] { new Dictionary<string, object> { ["byteLength"] = checked((int)binary.Length) } };
        byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(gltf, Formatting.None));
        int jsonLength = Align4(jsonBytes.Length);
        int binLength = Align4(checked((int)binary.Length));
        int totalLength = checked(12 + 8 + jsonLength + 8 + binLength);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var output = File.Create(path);
        using var glb = new BinaryWriter(output, Encoding.UTF8, leaveOpen: false);
        glb.Write(0x46546C67u);
        glb.Write(2u);
        glb.Write((uint)totalLength);
        glb.Write((uint)jsonLength);
        glb.Write(0x4E4F534Au);
        glb.Write(jsonBytes);
        for (int i = jsonBytes.Length; i < jsonLength; i++) glb.Write((byte)0x20);
        glb.Write((uint)binLength);
        glb.Write(0x004E4942u);
        binary.Position = 0;
        binary.CopyTo(output);
        for (int i = checked((int)binary.Length); i < binLength; i++) glb.Write((byte)0);
    }

    private void AlignBinary()
    {
        while ((binary.Position & 3) != 0) writer.Write((byte)0);
    }

    private static int Align4(int value) => (value + 3) & ~3;
    private static float Clamp01(float value) => Math.Clamp(float.IsFinite(value) ? value : 0f, 0f, 1f);
    private static string PathTail(string? path) => string.IsNullOrWhiteSpace(path) ? "Model" : path.Split('/').Last();
    private static string SafeName(string? value) => string.IsNullOrWhiteSpace(value) ? "Unnamed" : value.Replace("/", "_").Replace("\\", "_");
}
