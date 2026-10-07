using System.Reflection;
using KSA;

namespace KSACars;

/// <summary>
/// The box a road is drawn with, as something KSA's static mesh renderer will draw wherever it is told.
///
/// <para>The name of an asset is built and KSA's asset managers are asked by reflection. If any of it is not found the road is
/// not drawn, and says so once.</para>
/// </summary>
internal static class RoadMesh
{
    private const string Gltf = "KSACars_RoadSlab_Glb", Material = "KSACars_Road_Material";

    private static readonly Type? AssetNameType = typeof(LoadedAssetRef).GetProperty("Id")?.PropertyType;

    private static StaticMeshRenderable? _slab;
    private static bool _tried;

    public static StaticMeshRenderable? Slab
    {
        get
        {
            if (_tried) return _slab;
            _tried = true;
            if (!TryBuild(out _slab, out string why)) Log.Warn($"roads cannot be drawn: {why}");
            return _slab;
        }
    }

    private static bool TryBuild(out StaticMeshRenderable? mesh, out string why)
    {
        mesh = null;
        why = string.Empty;
        try
        {
            if (AssetNameType is null)
            {
                why = "LoadedAssetRef.Id not found";
                return false;
            }

            SuperMeshRenderSystem system = Program.Instance.SuperMeshRenderSystem;
            object gltfName = Activator.CreateInstance(AssetNameType, Gltf)!;
            object materialName = Activator.CreateInstance(AssetNameType, Material)!;

            if (Load(system, "GltfSystem", gltfName) is not GltfPbrAssetRef gltf)
            {
                why = $"glTF '{Gltf}' did not load";
                return false;
            }
            if (Load(system, "MaterialSystem", materialName) is not GpuObjectAssetRef material)
            {
                why = $"material '{Material}' did not load";
                return false;
            }

            // The .glb names one material slot, which is what gives this array a length.
            for (int i = 0; i < gltf.Materials.Length; i++) gltf.Materials[i] = material;

            mesh = (StaticMeshRenderable?)Activator.CreateInstance(
                typeof(StaticMeshRenderable), system.MeshRendererStaticPbr, gltfName, system.MeshRendererStaticPrePass, true);
            if (mesh is null) why = "the renderable could not be built";
            return mesh is not null;
        }
        catch (Exception e)
        {
            why = $"building '{Gltf}' threw: {e.GetBaseException().Message}";
            return false;
        }
    }

    private static object? Load(SuperMeshRenderSystem system, string managerField, object name)
    {
        object? manager = system.GetType().GetField(managerField, BindingFlags.Instance | BindingFlags.Public)?.GetValue(system);
        MethodInfo? load = manager?.GetType().GetMethod("GetOrLoad", [AssetNameType!]);
        return load?.Invoke(manager, [name]);
    }
}
