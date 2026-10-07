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
/// static renderer: a stretch of fixed size in the three buffers every static mesh shares, taken once,
/// and a <see cref="StaticMeshRenderable"/> over it. <b>An experiment, not yet seen in game.</b>
///
/// <para>KSA's mesh system only ever takes room and never gives it back, so a slot is reserved once
/// and reused. It keeps one vertex offset for the buffer of positions and the buffer of normals and
/// UVs alike, so a reservation that left the two out of step would misplace every mesh loaded after
/// it: the room is checked before anything is taken. <c>docs/KSA-MODDING-NOTES.md</c> has the
/// mechanism.</para>
/// </summary>
internal sealed class RuntimeMesh
{
    private const string Material = "KSACars_Road_Material";

    /// <summary>What is free in the three shared buffers, in bytes.</summary>
    public readonly record struct Room(long Attribs, long Positions, long Indices)
    {
        public override string ToString() => $"{Attribs} B of normals and UVs, {Positions} B of positions, {Indices} B of indices";
    }

    private static readonly int AttribBytes = Unsafe.SizeOf<InterleavedVertex>(), PositionBytes = Unsafe.SizeOf<float3>();
    private const int IndexBytes = sizeof(int);

    private readonly MeshIndirectSystem<InterleavedVertex> _meshes;
    private readonly IVulkanContext _device;
    private readonly MeshIndirectRef _slot;
    private readonly InterleavedVertex[] _attribs;
    private bool _broken;

    public StaticMeshRenderable Renderable { get; }

    public int VertexCapacity { get; }

    public int IndexCapacity { get; }

    public int VertexOffset => _slot.VertexOffset;

    public int IndexOffset => _slot.IndexOffset;

    public int Vertices { get; private set; }

    public int Indices { get; private set; }

    public Room FreeBefore { get; }

    public Room FreeAfter { get; }

    private RuntimeMesh(MeshIndirectSystem<InterleavedVertex> meshes, IVulkanContext device, MeshIndirectRef slot,
                        StaticMeshRenderable renderable, int vertexCapacity, int indexCapacity, Room before, Room after)
    {
        _meshes = meshes;
        _device = device;
        _slot = slot;
        _attribs = new InterleavedVertex[vertexCapacity];
        Renderable = renderable;
        VertexCapacity = vertexCapacity;
        IndexCapacity = indexCapacity;
        FreeBefore = before;
        FreeAfter = after;
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
    /// Takes room for a mesh of up to <paramref name="vertexCapacity"/> vertices and
    /// <paramref name="indexCapacity"/> indices under <paramref name="name"/>, drawing nothing until
    /// <see cref="Upload"/>. Null with the reason; never throws. That room is never given back.
    /// </summary>
    public static RuntimeMesh? Reserve(string name, int vertexCapacity, int indexCapacity, out string why)
    {
        try
        {
            SuperMeshRenderSystem system = Program.Instance.SuperMeshRenderSystem;
            MeshIndirectSystem<InterleavedVertex> meshes = system.MeshIndirectSystem;
            if (vertexCapacity < 3 || indexCapacity < 3)
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
            Room need = new((long)vertexCapacity * AttribBytes, (long)vertexCapacity * PositionBytes, (long)indexCapacity * IndexBytes);
            if (2 * need.Attribs > before.Attribs || 2 * need.Positions > before.Positions || 2 * need.Indices > before.Indices)
            {
                why = $"not enough room in KSA's mesh buffers: {need} wanted and {before} free";
                return null;
            }

            AssetName id = name;
            if (meshes.IsLoaded(id) || system.GltfSystem.IsLoaded(id))
            {
                why = $"KSA already has a mesh called '{name}'";
                return null;
            }
            GpuObjectAssetRef material = system.MaterialSystem.GetOrLoad(Material);

            using (MeshAsset blank = new())
            {
                blank.SetVerticesFromData<MeshAttribute, InterleavedVertex>(MeshAttribute.Interleaved, new InterleavedVertex[vertexCapacity]);
                blank.SetVerticesFromData<MeshAttribute, float3>(MeshAttribute.Position, new float3[vertexCapacity]);
                blank.SetIndicesFromData<int>(new int[indexCapacity]);
                blank.Update();
                if (!meshes.AddMesh(id, blank))
                {
                    why = "KSA would not take the mesh";
                    return null;
                }
            }

            MeshIndirectRef slot = meshes.GetOrLoad(id);
            slot.Data.IndexCount = 0;
            Room after = RoomIn(allocators);
            Log.Info($"runtime mesh '{name}': {vertexCapacity} vertices and {indexCapacity} indices reserved at vertex " +
                     $"{slot.VertexOffset}, index {slot.IndexOffset}; free before: {before}; after: {after}");
            if (slot.VertexOffset != usedAttribs / AttribBytes || slot.IndexOffset != usedIndices / IndexBytes
                || before.Positions - after.Positions != need.Positions)
            {
                why = "the room KSA gave is not the room that was free; something else was loading a mesh";
                return null;
            }

            GltfPbrAssetRef gltf = new(id) { Meshes = [slot], MaterialIndices = [0], Materials = [material], Textures = [] };
            if (!system.GltfSystem.TryAdd(gltf))
            {
                why = $"KSA already has a glTF called '{name}'";
                return null;
            }

            StaticMeshRenderable renderable = new(system.MeshRendererStaticPbr, id, system.MeshRendererStaticPrePass, true);
            why = string.Empty;
            return new RuntimeMesh(meshes, system.DeviceCtx, slot, renderable, vertexCapacity, indexCapacity, before, after);
        }
        catch (Exception e)
        {
            why = $"reserving '{name}' threw: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}";
            return null;
        }
    }

    /// <summary>
    /// Writes a mesh over the one in the slot and has it drawn from the next frame. Waits for the
    /// graphics card, so it is for the frame hook and not for KSA's render. False with the reason;
    /// never throws, and after a failure draws nothing and takes nothing more.
    /// </summary>
    public bool Upload(float3[] positions, float3[] normals, float2[] uvs, int[] indices, double radiusM, out string why)
    {
        if (_broken)
        {
            why = "this mesh failed before and is no longer written";
            return false;
        }
        if (normals.Length != positions.Length || uvs.Length != positions.Length)
        {
            why = "a mesh's positions, normals and UVs are not the same number";
            return false;
        }
        if (positions.Length > VertexCapacity || indices.Length > IndexCapacity || indices.Length % 3 != 0)
        {
            why = $"{positions.Length} vertices and {indices.Length} indices do not fit the {VertexCapacity} and {IndexCapacity} reserved";
            return false;
        }
        foreach (int index in indices)
        {
            if ((uint)index < (uint)positions.Length) continue;
            why = $"index {index} is past the mesh's {positions.Length} vertices";
            return false;
        }

        try
        {
            if (indices.Length > 0)
            {
                for (int i = 0; i < positions.Length; i++) _attribs[i] = new InterleavedVertex { Normal = normals[i], Uv0 = uvs[i] };

                // As MeshIndirectSystem.AddMesh uploads: disposing the pool submits the copies and waits for them.
                using StagingPool pool = _device.CreateStagingPool();
                CommandBuffer commands = pool.NextCommandBuffer();
                commands.Begin();
                VkUtils.StageAndUploadToBuffer(pool, _meshes.VertexAttribBuffer, Bytes((long)_slot.VertexOffset * AttribBytes),
                                               _attribs.AsSpan(0, positions.Length), commands);
                VkUtils.StageAndUploadToBuffer(pool, _meshes.VertexPosBuffer, Bytes((long)_slot.VertexOffset * PositionBytes),
                                               positions.AsSpan(), commands);
                VkUtils.StageAndUploadToBuffer(pool, _meshes.IndexBuffer, Bytes((long)_slot.IndexOffset * IndexBytes),
                                               indices.AsSpan(), commands);
                commands.End();
            }

            // KSA reads these when it writes each frame's draw, and the radius when it culls the shadow.
            _slot.Data.VertexCount = positions.Length;
            _slot.Data.IndexCount = indices.Length;
            _slot.BoundingRadius = (float)radiusM;
            Vertices = positions.Length;
            Indices = indices.Length;
            why = string.Empty;
            return true;
        }
        catch (Exception e)
        {
            _broken = true;
            _slot.Data.IndexCount = 0;
            Indices = 0;
            why = $"uploading threw: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}";
            Log.Error("a runtime mesh could not be uploaded and is no longer drawn", e);
            return false;
        }
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
