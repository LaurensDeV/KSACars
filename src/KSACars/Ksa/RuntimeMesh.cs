using System.Reflection;
using System.Runtime.CompilerServices;
using Brutal;
using Brutal.Numerics;
using Brutal.Render.Mesh;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using RenderCore;

namespace KSACars;

/// <summary>
/// A mesh whose vertices are made while the game runs and written over in place, drawn by KSA's own
/// static renderer: a stretch of fixed size in the three buffers every static mesh shares, and a
/// <see cref="StaticMeshRenderable"/> over it for each material it is drawn in.
///
/// <para>KSA's mesh system only ever takes room and never gives it back, so room is taken once, as a
/// <see cref="Block"/>, cut into meshes, and those are reused. It keeps one vertex offset for the
/// buffer of positions and the buffer of normals and UVs alike, so a reservation that left the two
/// out of step would misplace every mesh loaded after it: the room is checked before anything is
/// taken. <c>docs/KSA-MODDING-NOTES.md</c> has the mechanism.</para>
/// </summary>
internal sealed class RuntimeMesh
{
    /// <summary>What is free in the three shared buffers, in bytes.</summary>
    public readonly record struct Room(long Attribs, long Positions, long Indices)
    {
        public override string ToString() => $"{Attribs} B of normals and UVs, {Positions} B of positions, {Indices} B of indices";
    }

    /// <summary>
    /// A mesh's vertices and its triangles, those of its first material first.
    /// </summary>
    /// <param name="PartIndices">How many of <paramref name="Indices"/> are drawn in each of the mesh's materials, in order.</param>
    public readonly record struct Content(float3[] Positions, float3[] Normals, float2[] Uvs, int[] Indices, int[] PartIndices, double RadiusM);

    /// <summary>Room taken in KSA's buffers, which is never given back: where it starts and how much.</summary>
    public sealed class Block
    {
        internal required MeshIndirectSystem<InterleavedVertex> Meshes { get; init; }

        internal required SuperMeshRenderSystem System { get; init; }

        public required string Name { get; init; }

        public required int VertexOffset { get; init; }

        public required int IndexOffset { get; init; }

        public required int Vertices { get; init; }

        public required int Indices { get; init; }

        public required Room FreeBefore { get; init; }

        public required Room FreeAfter { get; init; }
    }

    private static readonly int AttribBytes = Unsafe.SizeOf<InterleavedVertex>(), PositionBytes = Unsafe.SizeOf<float3>();
    private const int IndexBytes = sizeof(int);

    private static InterleavedVertex[] _attribs = [];

    private readonly Block _block;
    private readonly MeshIndirectRef[] _refs;
    private readonly int _vertexOffset, _indexOffset;

    /// <summary>What draws the mesh, one for each of its materials.</summary>
    public StaticMeshRenderable[] Parts { get; }

    public StaticMeshRenderable Renderable => Parts[0];

    public int VertexCapacity { get; }

    public int IndexCapacity { get; }

    public int VertexOffset => _vertexOffset;

    public int IndexOffset => _indexOffset;

    public int Vertices { get; private set; }

    public int Indices { get; private set; }

    public Room FreeBefore => _block.FreeBefore;

    public Room FreeAfter => _block.FreeAfter;

    private RuntimeMesh(Block block, MeshIndirectRef[] refs, StaticMeshRenderable[] parts, int vertexOffset, int vertexCapacity,
                        int indexOffset, int indexCapacity)
    {
        _block = block;
        _refs = refs;
        Parts = parts;
        _vertexOffset = vertexOffset;
        _indexOffset = indexOffset;
        VertexCapacity = vertexCapacity;
        IndexCapacity = indexCapacity;
    }

    /// <summary>How many indices the mesh has in one of its materials: nothing, and that one is not to be drawn.</summary>
    public int PartIndices(int part) => _refs[part].IndexCount;

    /// <summary>
    /// Has one of the mesh's materials draw another run of the indices that are written, from the next
    /// frame and with nothing uploaded; a run that is not all inside them, and it draws nothing.
    /// </summary>
    public void Draws(int part, int firstIndex, int count)
    {
        MeshIndirectRef to = _refs[part];
        bool inside = firstIndex >= 0 && count > 0 && firstIndex + count <= Indices;
        to.Data.IndexOffset = _indexOffset + (inside ? firstIndex : 0);
        to.Data.IndexCount = inside ? count : 0;
    }

    /// <summary>What is free in KSA's static mesh buffers now, or null if its allocators are not where they were.</summary>
    public static Room? Free()
    {
        try
        {
            return Allocators(Program.Instance.SuperMeshRenderSystem.MeshIndirectSystem) is { } found ? RoomIn(found) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Takes room for <paramref name="vertices"/> vertices and <paramref name="indices"/> indices
    /// under <paramref name="name"/>. Null with the reason; never throws. That room is never given back.
    /// </summary>
    public static Block? Reserve(string name, int vertices, int indices, out string why)
    {
        try
        {
            SuperMeshRenderSystem system = Program.Instance.SuperMeshRenderSystem;
            MeshIndirectSystem<InterleavedVertex> meshes = system.MeshIndirectSystem;
            if (vertices < 3 || indices < 3)
            {
                why = "a mesh needs room for a triangle";
                return null;
            }
            if (Allocators(meshes) is not { } allocators)
            {
                why = "KSA's mesh allocators were not found";
                return null;
            }

            Room before = RoomIn(allocators);
            long usedAttribs = (long)(ulong)allocators.Attribs.CurrentOffset, usedPositions = (long)(ulong)allocators.Positions.CurrentOffset,
                 usedIndices = (long)(ulong)allocators.Indices.CurrentOffset;
            // KSA draws vertex n of a mesh from byte n times the stride of each buffer, off one offset.
            if (usedAttribs % AttribBytes != 0 || usedPositions % PositionBytes != 0 || usedIndices % IndexBytes != 0
                || usedAttribs / AttribBytes != usedPositions / PositionBytes)
            {
                why = $"KSA's vertex buffers are not in step ({usedAttribs} B of normals and UVs, {usedPositions} B of positions used)";
                return null;
            }

            // Half of what is free at most: KSA throws while loading a part if a buffer is full.
            Room need = new((long)vertices * AttribBytes, (long)vertices * PositionBytes, (long)indices * IndexBytes);
            if (2 * need.Attribs > before.Attribs || 2 * need.Positions > before.Positions || 2 * need.Indices > before.Indices)
            {
                why = $"not enough room in KSA's mesh buffers: {need} wanted and {before} free";
                return null;
            }

            AssetName id = name;
            if (meshes.IsLoaded(id))
            {
                why = $"KSA already has a mesh called '{name}'";
                return null;
            }

            using (MeshAsset blank = new())
            {
                blank.SetVerticesFromData<MeshAttribute, InterleavedVertex>(MeshAttribute.Interleaved, new InterleavedVertex[vertices]);
                blank.SetVerticesFromData<MeshAttribute, float3>(MeshAttribute.Position, new float3[vertices]);
                blank.SetIndicesFromData<int>(new int[indices]);
                blank.Update();
                if (!meshes.AddMesh(id, blank))
                {
                    why = "KSA would not take the mesh";
                    return null;
                }
            }

            // Nothing draws the room itself, only the meshes cut from it.
            MeshIndirectRef taken = meshes.GetOrLoad(id);
            taken.Data.IndexCount = 0;
            Room after = RoomIn(allocators);
            Log.Info($"runtime mesh '{name}': {vertices} vertices and {indices} indices reserved at vertex " +
                     $"{taken.VertexOffset}, index {taken.IndexOffset}; free before: {before}; after: {after}");
            if (taken.VertexOffset != usedAttribs / AttribBytes || taken.IndexOffset != usedIndices / IndexBytes
                || before.Positions - after.Positions != need.Positions)
            {
                why = "the room KSA gave is not the room that was free; something else was loading a mesh";
                return null;
            }

            why = string.Empty;
            return new Block
            {
                Meshes = meshes, System = system, Name = name, VertexOffset = taken.VertexOffset, IndexOffset = taken.IndexOffset,
                Vertices = vertices, Indices = indices, FreeBefore = before, FreeAfter = after,
            };
        }
        catch (Exception e)
        {
            why = $"reserving '{name}' threw: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}";
            return null;
        }
    }

    /// <summary>
    /// A mesh in part of <paramref name="block"/>, from its vertex <paramref name="vertexAt"/> and
    /// index <paramref name="indexAt"/>, drawn in each of <paramref name="materials"/> and drawing
    /// nothing until it is uploaded to. Null with the reason; never throws.
    /// </summary>
    /// <remarks>
    /// Each material draws a run of the mesh's indices of its own. KSA writes a draw from whatever
    /// <see cref="MeshIndirectRef"/> a renderable was built over, read anew each frame, and files draws
    /// by that object, so one made here over part of a block's room is drawn as any loaded mesh is.
    /// </remarks>
    public static RuntimeMesh? Over(Block block, string name, int vertexAt, int vertexCapacity, int indexAt, int indexCapacity,
                                    IReadOnlyList<string> materials, out string why)
    {
        try
        {
            if (vertexAt < 0 || indexAt < 0 || vertexCapacity < 3 || indexCapacity < 3 || materials.Count == 0
                || vertexAt + vertexCapacity > block.Vertices || indexAt + indexCapacity > block.Indices)
            {
                why = $"'{name}' does not fit the room of '{block.Name}'";
                return null;
            }

            SuperMeshRenderSystem system = block.System;
            MeshIndirectRef[] refs = new MeshIndirectRef[materials.Count];
            StaticMeshRenderable[] parts = new StaticMeshRenderable[materials.Count];
            for (int p = 0; p < materials.Count; p++)
            {
                AssetName id = materials.Count == 1 ? name : $"{name}_{p}";
                if (system.GltfSystem.IsLoaded(id))
                {
                    why = $"KSA already has a glTF called '{id}'";
                    return null;
                }

                MeshOffsetData data = default;
                data.VertexOffset = block.VertexOffset + vertexAt;
                data.IndexOffset = block.IndexOffset + indexAt;
                refs[p] = new MeshIndirectRef(id, data, -1, null);

                GpuObjectAssetRef material = system.MaterialSystem.GetOrLoad(materials[p]);
                GltfPbrAssetRef gltf = new(id) { Meshes = [refs[p]], MaterialIndices = [0], Materials = [material], Textures = [] };
                if (!system.GltfSystem.TryAdd(gltf))
                {
                    why = $"KSA would not take a glTF called '{id}'";
                    return null;
                }
                parts[p] = new StaticMeshRenderable(system.MeshRendererStaticPbr, id, system.MeshRendererStaticPrePass, true);
            }

            why = string.Empty;
            return new RuntimeMesh(block, refs, parts, block.VertexOffset + vertexAt, vertexCapacity, block.IndexOffset + indexAt, indexCapacity);
        }
        catch (Exception e)
        {
            why = $"making '{name}' threw: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}";
            return null;
        }
    }

    /// <summary>Has the mesh draw nothing, whatever is written in its room.</summary>
    public void Empty()
    {
        foreach (MeshIndirectRef part in _refs) part.Data.IndexCount = 0;
        Vertices = 0;
        Indices = 0;
    }

    public bool Upload(in Content content, out string why) => Upload([(this, content)], out why);

    /// <summary>
    /// Writes each mesh's content over what is in its room and has it drawn from the next frame, all
    /// of them in one submission. It waits for the graphics card once, so it is for the frame hook and
    /// not for KSA's render. False with the reason, and then none of them draws anything; never throws.
    /// </summary>
    public static bool Upload(IReadOnlyList<(RuntimeMesh Mesh, Content Content)> all, out string why)
    {
        why = string.Empty;
        if (all.Count == 0) return true;

        foreach ((RuntimeMesh mesh, Content content) in all)
        {
            mesh.Empty();
            if (!mesh.Takes(content, out why)) return false;
        }

        try
        {
            Block block = all[0].Mesh._block;

            // As MeshIndirectSystem.AddMesh uploads: disposing the pool submits the copies and waits for them.
            using (StagingPool pool = block.System.DeviceCtx.CreateStagingPool())
            {
                CommandBuffer commands = pool.NextCommandBuffer();
                commands.Begin();
                foreach ((RuntimeMesh mesh, Content content) in all)
                {
                    int count = content.Positions.Length;
                    if (content.Indices.Length == 0) continue;
                    if (_attribs.Length < count) _attribs = new InterleavedVertex[count];
                    for (int i = 0; i < count; i++) _attribs[i] = new InterleavedVertex { Normal = content.Normals[i], Uv0 = content.Uvs[i] };

                    MeshIndirectSystem<InterleavedVertex> meshes = mesh._block.Meshes;
                    VkUtils.StageAndUploadToBuffer(pool, meshes.VertexAttribBuffer, Bytes((long)mesh._vertexOffset * AttribBytes),
                                                   _attribs.AsSpan(0, count), commands);
                    VkUtils.StageAndUploadToBuffer(pool, meshes.VertexPosBuffer, Bytes((long)mesh._vertexOffset * PositionBytes),
                                                   content.Positions.AsSpan(), commands);
                    VkUtils.StageAndUploadToBuffer(pool, meshes.IndexBuffer, Bytes((long)mesh._indexOffset * IndexBytes),
                                                   content.Indices.AsSpan(), commands);
                }
                commands.End();
            }

            // KSA reads these when it writes each frame's draw, and the radius when it culls the shadow.
            foreach ((RuntimeMesh mesh, Content content) in all)
            {
                int at = mesh._indexOffset;
                for (int p = 0; p < mesh._refs.Length; p++)
                {
                    MeshIndirectRef part = mesh._refs[p];
                    part.Data.VertexCount = content.Positions.Length;
                    part.Data.IndexOffset = at;
                    part.Data.IndexCount = content.PartIndices[p];
                    part.BoundingRadius = (float)content.RadiusM;
                    at += content.PartIndices[p];
                }
                mesh.Vertices = content.Positions.Length;
                mesh.Indices = content.Indices.Length;
            }
            return true;
        }
        catch (Exception e)
        {
            foreach ((RuntimeMesh mesh, _) in all) mesh.Empty();
            why = $"uploading threw: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}";
            Log.Error("runtime meshes could not be uploaded and are not drawn", e);
            return false;
        }
    }

    private bool Takes(in Content content, out string why)
    {
        int vertices = content.Positions.Length, indices = content.Indices.Length;
        if (content.Normals.Length != vertices || content.Uvs.Length != vertices)
        {
            why = "a mesh's positions, normals and UVs are not the same number";
            return false;
        }
        if (vertices > VertexCapacity || indices > IndexCapacity)
        {
            why = $"{vertices} vertices and {indices} indices do not fit the {VertexCapacity} and {IndexCapacity} reserved";
            return false;
        }
        if (content.PartIndices.Length != _refs.Length || content.PartIndices.Sum() != indices || content.PartIndices.Any(n => n < 0 || n % 3 != 0))
        {
            why = $"a mesh's {indices} indices are not shared out among its {_refs.Length} material(s) in whole triangles";
            return false;
        }
        foreach (int index in content.Indices)
        {
            if ((uint)index < (uint)vertices) continue;
            why = $"index {index} is past the mesh's {vertices} vertices";
            return false;
        }
        why = string.Empty;
        return true;
    }

    private static ByteSize Bytes(long bytes) => new((nuint)bytes);

    private static Room RoomIn((LinearBufferPartitioner Attribs, LinearBufferPartitioner Positions, LinearBufferPartitioner Indices) a) =>
        new((long)(ulong)a.Attribs.FreeSpace, (long)(ulong)a.Positions.FreeSpace, (long)(ulong)a.Indices.FreeSpace);

    private static (LinearBufferPartitioner Attribs, LinearBufferPartitioner Positions, LinearBufferPartitioner Indices)? Allocators(
        MeshIndirectSystem<InterleavedVertex> meshes)
    {
        static LinearBufferPartitioner? Field(object on, string name) =>
            on.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(on) as LinearBufferPartitioner;

        return Field(meshes, "_vertexAllocator") is { } attribs && Field(meshes, "_vertexPosAllocator") is { } positions
               && Field(meshes, "_indexAllocator") is { } indices
            ? (attribs, positions, indices)
            : null;
    }
}
