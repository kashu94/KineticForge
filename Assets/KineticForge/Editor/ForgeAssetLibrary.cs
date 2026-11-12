using System.Collections.Generic;
using KineticForge.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KineticForge.EditorTools
{
    /// <summary>
    /// The scanned CC0 assets the forge is built from: configures their importers for Quest, builds URP Lit materials
    /// from their maps, and places models at a real-world size.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Assets come from Poly Haven (CC0) and live in <c>Assets/KineticForge/ThirdParty/PolyHaven/&lt;id&gt;</c>, one folder
    /// per asset: <c>&lt;id&gt;.fbx</c> for models, <c>&lt;id&gt;_albedo.jpg</c>, <c>&lt;id&gt;_normal.jpg</c> (OpenGL
    /// convention) and <c>&lt;id&gt;_mask.png</c>, packed for URP Lit as metallic (R), occlusion (G) and smoothness (A).
    /// </para>
    /// <para>
    /// Every call is idempotent: importers are only re-saved when a setting differs, and materials are refreshed in
    /// place, so a rebuild never duplicates assets. A missing asset returns null and the caller falls back to a primitive,
    /// so the scene always builds.
    /// </para>
    /// </remarks>
    internal static class ForgeAssetLibrary
    {
        internal const string Root = "Assets/KineticForge/ThirdParty/PolyHaven";
        const string MaterialFolder = "Assets/KineticForge/Materials/Scanned";
        const string LitShader = "Universal Render Pipeline/Lit";
        const float SmallPartThickness = 0.03f;
        const float SmallPartSize = 0.3f;

        const StaticEditorFlags EnvironmentFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                                                   StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic |
                                                   StaticEditorFlags.ReflectionProbeStatic;

        enum MapKind
        {
            Albedo,
            Normal,
            Mask,
        }

        /// <summary>Axis a placed model is sized along.</summary>
        internal enum SizeAxis
        {
            /// <summary>Width (x).</summary>
            Width,

            /// <summary>Height (y).</summary>
            Height,

            /// <summary>Depth (z).</summary>
            Depth,

            /// <summary>The largest dimension.</summary>
            Largest,
        }

        /// <summary>True when the asset's folder exists.</summary>
        /// <param name="id">Poly Haven asset id.</param>
        internal static bool Has(string id) => AssetDatabase.IsValidFolder(Root + "/" + id);

        /// <summary>Builds or refreshes the URP Lit material for an asset.</summary>
        /// <param name="id">Poly Haven asset id.</param>
        /// <param name="tiling">Texture repeats across the surface's UV range.</param>
        /// <param name="maxSize">Largest texture size imported, in pixels.</param>
        /// <param name="tint">Multiplied into the albedo.</param>
        /// <param name="smoothnessScale">Multiplies the smoothness map.</param>
        /// <returns>The material, or null when the asset's maps are missing.</returns>
        internal static Material Material(string id, Vector2 tiling, int maxSize, Color tint, float smoothnessScale = 1f)
        {
            string folder = Root + "/" + id;
            Texture2D albedo = Texture(folder + "/" + id + "_albedo.jpg", MapKind.Albedo, maxSize);
            Texture2D normal = Texture(folder + "/" + id + "_normal.jpg", MapKind.Normal, maxSize);
            Texture2D mask = Texture(folder + "/" + id + "_mask.png", MapKind.Mask, Mathf.Max(256, maxSize / 2));
            if (albedo == null)
            {
                return null;
            }

            Shader shader = Shader.Find(LitShader);
            if (shader == null)
            {
                ForgeLog.Warn(ForgeLogChannel.Core, nameof(ForgeAssetLibrary), "SHADER_MISSING",
                    LitShader + " was not found; scanned materials fall back to primitives.", null);
                return null;
            }

            bool tiled = tiling != Vector2.one;
            string name = id + (tiled ? " x" + tiling.x.ToString("0.##") + "x" + tiling.y.ToString("0.##") : string.Empty) +
                          (tint != Color.white ? " " + ColorUtility.ToHtmlStringRGB(tint) : string.Empty);
            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created)
            {
                ForgeEnvironmentBuilder.EnsureFolder(MaterialFolder);
                material = new Material(shader) { name = name };
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetFloat("_WorkflowMode", 1f);
            material.SetColor("_BaseColor", tint);
            material.SetTexture("_BaseMap", albedo);
            material.SetTextureScale("_BaseMap", tiling);
            material.SetFloat("_Smoothness", smoothnessScale);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetFloat("_Metallic", 1f);
            SetMap(material, "_BumpMap", normal, "_NORMALMAP");
            SetMap(material, "_MetallicGlossMap", mask, "_METALLICSPECGLOSSMAP");
            SetMap(material, "_OcclusionMap", mask, "_OCCLUSIONMAP");
            material.SetFloat("_BumpScale", 1f);
            material.SetFloat("_OcclusionStrength", 1f);
            material.enableInstancing = true;

            if (created)
            {
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        /// <summary>
        /// Places a scanned model: scaled uniformly so <paramref name="axis"/> measures <paramref name="size"/> meters in
        /// the model's own upright orientation, then rotated, then moved so it rests on <paramref name="position"/>.y (or is
        /// centered on it) and is centered on its x and z. The model root's own import rotation and scale are preserved.
        /// </summary>
        /// <param name="id">Poly Haven asset id.</param>
        /// <param name="name">Name of the placed object.</param>
        /// <param name="parent">Parent transform.</param>
        /// <param name="position">Point under (or at) the model's center.</param>
        /// <param name="rotation">World rotation applied after scaling.</param>
        /// <param name="size">Target size along <paramref name="axis"/>, in meters.</param>
        /// <param name="axis">Axis measured.</param>
        /// <param name="centerVertically">Centers the model on <paramref name="position"/>.y instead of resting on it.</param>
        /// <param name="material">Material for every renderer; the asset's own when null.</param>
        /// <param name="isStatic">Marks mesh renderers static, for baked lighting and batching.</param>
        /// <returns>The placed object, or null when the model is missing.</returns>
        internal static GameObject Place(string id, string name, Transform parent, Vector3 position, Quaternion rotation,
            float size, SizeAxis axis, bool centerVertically = false, Material material = null, bool isStatic = true)
        {
            string modelPath = Root + "/" + id + "/" + id + ".fbx";
            ConfigureModel(modelPath);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                return null;
            }

            if (material == null)
            {
                material = Material(id, Vector2.one, 1024, Color.white);
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = name;
            Transform transform = instance.transform;

            // The model's root carries the exporter's axis correction (and unit scale); keep it, and apply ours on top.
            Quaternion nativeRotation = transform.localRotation;
            Vector3 nativeScale = transform.localScale;
            transform.SetParent(parent, false);
            transform.SetPositionAndRotation(Vector3.zero, nativeRotation);
            transform.localScale = nativeScale;

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Object.DestroyImmediate(instance);
                return null;
            }

            Bounds bounds = WorldBounds(renderers);
            float measured = axis switch
            {
                SizeAxis.Width => bounds.size.x,
                SizeAxis.Height => bounds.size.y,
                SizeAxis.Depth => bounds.size.z,
                _ => Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)),
            };

            float scale = measured > 1e-5f ? size / measured : 1f;
            transform.localScale = nativeScale * scale;
            transform.rotation = rotation * nativeRotation;
            bounds = WorldBounds(renderers);
            float lift = centerVertically ? position.y - bounds.center.y : position.y - bounds.min.y;
            transform.position += new Vector3(position.x - bounds.center.x, lift, position.z - bounds.center.z);

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (material != null)
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (int m = 0; m < materials.Length; m++)
                    {
                        materials[m] = material;
                    }

                    renderer.sharedMaterials = materials;
                }

                renderer.shadowCastingMode = ShadowCastingMode.On;
                if (isStatic && renderer is MeshRenderer meshRenderer)
                {
                    // Thin or small parts (a bucket's wire handle, a hammer's head) cannot hold a usable lightmap, so they
                    // take their baked light from the probes; they still contribute to the bake.
                    Vector3 extent = renderer.bounds.size;
                    float thinnest = Mathf.Min(extent.x, Mathf.Min(extent.y, extent.z));
                    float largest = Mathf.Max(extent.x, Mathf.Max(extent.y, extent.z));
                    bool small = thinnest < SmallPartThickness || largest < SmallPartSize;
                    meshRenderer.receiveGI = small ? ReceiveGI.LightProbes : ReceiveGI.Lightmaps;
                    GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, EnvironmentFlags);
                }
                else
                {
                    renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                }
            }

            return instance;
        }

        /// <summary>Marks an environment object static for baked lighting, batching and occlusion.</summary>
        /// <param name="gameObject">The object; its children are left alone.</param>
        internal static void MarkStatic(GameObject gameObject)
        {
            GameObjectUtility.SetStaticEditorFlags(gameObject, EnvironmentFlags);
            if (gameObject.TryGetComponent(out MeshRenderer renderer))
            {
                renderer.receiveGI = ReceiveGI.Lightmaps;
            }
        }

        /// <summary>Lists the asset ids present, for the credits and the preflight.</summary>
        /// <returns>Folder names under <see cref="Root"/>.</returns>
        internal static List<string> PresentIds()
        {
            List<string> ids = new List<string>();
            if (!AssetDatabase.IsValidFolder(Root))
            {
                return ids;
            }

            string[] folders = AssetDatabase.GetSubFolders(Root);
            for (int i = 0; i < folders.Length; i++)
            {
                ids.Add(System.IO.Path.GetFileName(folders[i]));
            }

            return ids;
        }

        /// <summary>Combined world bounds of every renderer under <paramref name="root"/>.</summary>
        /// <param name="root">The object.</param>
        /// <returns>The bounds, or empty bounds at the object's position when it has no renderer.</returns>
        internal static Bounds BoundsOf(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            return renderers.Length > 0 ? WorldBounds(renderers) : new Bounds(root.transform.position, Vector3.zero);
        }

        static Bounds WorldBounds(Renderer[] renderers)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        static void SetMap(Material material, string property, Texture2D texture, string keyword)
        {
            material.SetTexture(property, texture);
            if (texture != null)
            {
                material.EnableKeyword(keyword);
            }
            else
            {
                material.DisableKeyword(keyword);
            }
        }

        static Texture2D Texture(string path, MapKind kind, int maxSize)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return null;
            }

            bool dirty = false;
            TextureImporterType type = kind == MapKind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != type)
            {
                importer.textureType = type;
                dirty = true;
            }

            bool sRGB = kind == MapKind.Albedo;
            if (kind != MapKind.Normal && importer.sRGBTexture != sRGB)
            {
                importer.sRGBTexture = sRGB;
                dirty = true;
            }

            TextureImporterAlphaSource alpha = kind == MapKind.Mask ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            if (importer.alphaSource != alpha)
            {
                importer.alphaSource = alpha;
                dirty = true;
            }

            if (importer.maxTextureSize != maxSize)
            {
                importer.maxTextureSize = maxSize;
                dirty = true;
            }

            if (!importer.mipmapEnabled)
            {
                importer.mipmapEnabled = true;
                dirty = true;
            }

            if (importer.anisoLevel != 4)
            {
                importer.anisoLevel = 4;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void ConfigureModel(string path)
        {
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                return;
            }

            bool dirty = false;
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                dirty = true;
            }

            if (!importer.generateSecondaryUV)
            {
                importer.generateSecondaryUV = true;
                dirty = true;
            }

            if (importer.importAnimation || importer.importCameras || importer.importLights || importer.importBlendShapes)
            {
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.importBlendShapes = false;
                dirty = true;
            }

            if (importer.animationType != ModelImporterAnimationType.None)
            {
                importer.animationType = ModelImporterAnimationType.None;
                dirty = true;
            }

            if (importer.meshCompression != ModelImporterMeshCompression.Medium)
            {
                importer.meshCompression = ModelImporterMeshCompression.Medium;
                dirty = true;
            }

            if (importer.isReadable)
            {
                importer.isReadable = false;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
            }
        }
    }
}
