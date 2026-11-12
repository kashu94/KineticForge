using System.IO;
using KineticForge.Core;
using KineticForge.Feedback;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KineticForge.EditorTools
{
    /// <summary>
    /// Builds the forge around the sandbox: a firelit stone hall with a furnace, two braziers, an anvil, embers, fog,
    /// warm reflections and moonlight, plus the generated textures and materials the scene uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lighting is tuned for Quest: one shadowed directional moonlight, four unshadowed flickering point lights, trilight
    /// ambient and no post-processing. Exponential fog hides the hall's edges and gives depth. Metal needs something to
    /// reflect, so a small generated cubemap of the forge's glow becomes the scene's reflection source.
    /// </para>
    /// <para>
    /// Generated assets live in <c>Assets/KineticForge/Generated</c> and are rebuilt with the scene. Materials are created
    /// once and then reused untouched, so hand-tuned looks survive a rebuild.
    /// </para>
    /// </remarks>
    internal static class ForgeEnvironmentBuilder
    {
        internal const string MaterialFolder = "Assets/KineticForge/Materials";
        const string GeneratedFolder = "Assets/KineticForge/Generated";
        const string StoneTexturePath = GeneratedFolder + "/Forge Stone.png";
        const string ReflectionPath = GeneratedFolder + "/Forge Reflection.cubemap";
        internal const string RelicMeshPath = GeneratedFolder + "/Relic Tablet.asset";

        internal const string MetalShader = "Kinetic Forge/Forge Metal";
        internal const string GlowShader = "Kinetic Forge/Forge Glow";
        internal const string TextShader = "Kinetic Forge/Forge Text";

        internal static readonly Color FogColor = new Color(0.035f, 0.026f, 0.022f);

        /// <summary>Materials of the forge, created on first use.</summary>
        internal sealed class Palette
        {
            public Material Flagstone;
            public Material Stone;
            public Material Iron;
            public Material Molten;
            public Material RelicMetal;
            public Material Spark;
            public Material Ring;
            public Material Ember;
            public Material Text;
            public Material RuneGlow;
            public Material Sand;
            public Material Energy;
            public Material Steam;
            public Material Water;
        }

        /// <summary>Creates or loads every material the forge uses.</summary>
        /// <returns>The palette.</returns>
        internal static Palette CreatePalette()
        {
            Texture2D stone = GetOrCreateStoneTexture();
            Palette palette = new Palette
            {
                Flagstone = GetOrCreateLit("Forge Flagstone", new Color(0.36f, 0.32f, 0.3f), 0f, 0.22f, stone, 6f),
                Stone = GetOrCreateLit("Forge Stone", new Color(0.42f, 0.37f, 0.34f), 0f, 0.12f, stone, 1.5f),
                Iron = GetOrCreateLit("Forge Iron", new Color(0.13f, 0.13f, 0.14f), 0.9f, 0.48f, null, 1f),
                RuneGlow = GetOrCreate("Rune Glow", "Universal Render Pipeline/Unlit", m => m.SetColor("_BaseColor", new Color(1f, 0.62f, 0.2f))),
                Molten = GetOrCreate("Forge Molten", MetalShader, m =>
                {
                    m.SetColor("_BaseColor", new Color(0.25f, 0.12f, 0.06f));
                    m.SetFloat("_Metallic", 0f);
                    m.SetFloat("_Smoothness", 0.35f);
                    m.SetFloat("_Heat", 0.85f);
                    m.SetFloat("_HeatGlow", 1.4f);
                    m.SetFloat("_HammerScale", 14f);
                    m.SetFloat("_HammerStrength", 0.8f);
                }),
                RelicMetal = GetOrCreate("Relic Forge Metal", MetalShader, m =>
                {
                    m.SetColor("_BaseColor", new Color(0.78f, 0.5f, 0.24f));
                    m.SetFloat("_Metallic", 1f);
                    m.SetFloat("_Smoothness", 0.62f);
                    m.SetFloat("_HeatGlow", 0.45f);
                }),
                Spark = GetOrCreate("Forge Spark", GlowShader, m =>
                {
                    m.SetColor("_Color", new Color(1f, 0.75f, 0.4f));
                    m.SetFloat("_Softness", 1.4f);
                }),
                Ember = GetOrCreate("Forge Ember", GlowShader, m =>
                {
                    m.SetColor("_Color", new Color(1f, 0.6f, 0.25f));
                    m.SetFloat("_Softness", 2.2f);
                }),
                Ring = GetOrCreate("Forge Ring", GlowShader, m =>
                {
                    m.SetColor("_Color", Color.white);
                    m.SetFloat("_Ring", 0.14f);
                }),
                Text = GetOrCreate("Forge Text", TextShader, m => m.SetColor("_Color", Color.white)),
                Sand = GetOrCreate("Forge Sand", GlowShader, m =>
                {
                    m.SetColor("_Color", Color.white);
                    m.SetFloat("_Softness", 1.2f);
                }),
                Energy = GetOrCreate("Forge Energy", GlowShader, m =>
                {
                    m.SetColor("_Color", Color.white);
                    m.SetFloat("_Softness", 1.6f);
                }),
                Steam = GetOrCreate("Forge Steam", GlowShader, m =>
                {
                    m.SetColor("_Color", new Color(0.5f, 0.5f, 0.55f));
                    m.SetFloat("_Softness", 2.5f);
                }),
                Water = GetOrCreateLit("Forge Water", new Color(0.02f, 0.05f, 0.07f), 0f, 0.95f, null, 1f),
            };
            return palette;
        }

        /// <summary>Sets fog, ambient light, reflections and the moonlight.</summary>
        internal static void SetUpAtmosphere()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.07f, 0.075f, 0.1f);
            RenderSettings.ambientEquatorColor = new Color(0.09f, 0.06f, 0.045f);
            RenderSettings.ambientGroundColor = new Color(0.035f, 0.025f, 0.02f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.11f;
            RenderSettings.fogColor = FogColor;

            Cubemap reflection = GetOrCreateReflection();
            if (reflection != null)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = reflection;
                RenderSettings.reflectionIntensity = 1f;
            }

            Light moon = Object.FindAnyObjectByType<Light>();
            if (moon != null && moon.type == LightType.Directional)
            {
                moon.name = "Moonlight";
                moon.transform.rotation = Quaternion.Euler(58f, -35f, 0f);
                moon.color = new Color(0.55f, 0.65f, 1f);
                moon.intensity = 0.35f;
                moon.shadows = LightShadows.Soft;
                moon.shadowStrength = 0.8f;
                RenderSettings.sun = moon;
            }
        }

        /// <summary>Configures a camera to clear to the fog color, since the forge has no sky.</summary>
        /// <param name="camera">The camera.</param>
        internal static void ConfigureCamera(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = FogColor;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 60f;
        }

        /// <summary>Builds the hall, furnace, braziers and anvil.</summary>
        /// <param name="palette">Materials.</param>
        /// <param name="anvilPosition">Floor point under the anvil.</param>
        /// <param name="anvilTop">Height of the anvil face.</param>
        /// <returns>The forge's root object; its Floor, Hall and Furnace children are the parts mixed reality hides.</returns>
        internal static GameObject BuildForge(Palette palette, Vector3 anvilPosition, float anvilTop)
        {
            GameObject root = new GameObject("Forge");

            GameObject floor = Primitive(PrimitiveType.Plane, "Floor", root.transform, Vector3.zero, new Vector3(1.4f, 1f, 1.4f), palette.Flagstone);
            floor.isStatic = true;

            // A ring of pillars, open where the furnace stands.
            Transform hall = Group("Hall", root.transform);
            const float Radius = 3.6f;
            for (int i = 0; i < 12; i++)
            {
                float angle = 15f + 30f * i;
                if (Mathf.Abs(Mathf.DeltaAngle(angle, 0f)) < 25f)
                {
                    continue;
                }

                float radians = angle * Mathf.Deg2Rad;
                Vector3 position = new Vector3(Mathf.Sin(radians) * Radius, 1.8f, Mathf.Cos(radians) * Radius);
                GameObject pillar = Primitive(PrimitiveType.Cube, "Pillar " + (i + 1), hall, position, new Vector3(0.6f, 3.6f, 0.6f), palette.Stone);
                pillar.transform.rotation = Quaternion.Euler(0f, angle, 0f);
                pillar.isStatic = true;
                GameObject cap = Primitive(PrimitiveType.Cube, "Capital", hall, position + Vector3.up * 1.75f, new Vector3(0.8f, 0.18f, 0.8f), palette.Stone);
                cap.transform.rotation = pillar.transform.rotation;
                cap.isStatic = true;
            }

            BuildFurnace(palette, root.transform, new Vector3(0f, 0f, 2.3f));
            BuildBrazier(palette, root.transform, new Vector3(-1.45f, 0f, 1.25f));
            BuildBrazier(palette, root.transform, new Vector3(1.45f, 0f, 1.25f));
            BuildAnvil(palette, root.transform, anvilPosition, anvilTop);
            return root;
        }

        /// <summary>Builds the quench trough and returns the point on its water surface.</summary>
        /// <param name="palette">Materials.</param>
        /// <param name="position">Floor point under the trough's center.</param>
        /// <returns>The water surface point.</returns>
        internal static Transform BuildTrough(Palette palette, Vector3 position)
        {
            Transform trough = Group("Quench Trough", null);
            trough.position = position;
            const float Height = 0.5f;
            Vector3 size = new Vector3(0.5f, Height, 0.36f);
            float wall = 0.04f;
            Static(Primitive(PrimitiveType.Cube, "Base", trough, position + new Vector3(0f, 0.03f, 0f), new Vector3(size.x, 0.06f, size.z), palette.Stone));
            Static(Primitive(PrimitiveType.Cube, "Front", trough, position + new Vector3(0f, Height * 0.5f, -size.z * 0.5f + wall * 0.5f), new Vector3(size.x, Height, wall), palette.Stone));
            Static(Primitive(PrimitiveType.Cube, "Back", trough, position + new Vector3(0f, Height * 0.5f, size.z * 0.5f - wall * 0.5f), new Vector3(size.x, Height, wall), palette.Stone));
            Static(Primitive(PrimitiveType.Cube, "Left", trough, position + new Vector3(-size.x * 0.5f + wall * 0.5f, Height * 0.5f, 0f), new Vector3(wall, Height, size.z), palette.Stone));
            Static(Primitive(PrimitiveType.Cube, "Right", trough, position + new Vector3(size.x * 0.5f - wall * 0.5f, Height * 0.5f, 0f), new Vector3(wall, Height, size.z), palette.Stone));
            GameObject water = Primitive(PrimitiveType.Cube, "Water", trough, position + new Vector3(0f, Height - 0.04f, 0f),
                new Vector3(size.x - 2f * wall, 0.01f, size.z - 2f * wall), palette.Water);
            Object.DestroyImmediate(water.GetComponent<Collider>());

            // The relic plunges through the trough, so none of it may collide.
            foreach (Collider collider in trough.GetComponentsInChildren<Collider>())
            {
                Object.DestroyImmediate(collider);
            }

            Transform surface = new GameObject("Water Surface").transform;
            surface.SetParent(trough, false);
            surface.position = position + new Vector3(0f, Height - 0.035f, 0f);
            return surface;
        }

        /// <summary>Builds the hanging crucible and its molten stream for the pour.</summary>
        /// <param name="palette">Materials.</param>
        /// <param name="pivot">Point the crucible tips around.</param>
        /// <param name="lip">Receives the stream's start point.</param>
        /// <param name="stream">Receives the stream cylinder.</param>
        /// <returns>The crucible pivot.</returns>
        internal static Transform BuildCrucible(Palette palette, Vector3 pivot, out Transform lip, out Transform stream)
        {
            Transform root = Group("Crucible", null);
            root.position = pivot;
            GameObject chain = Primitive(PrimitiveType.Cylinder, "Chain", root, pivot + Vector3.up * 1.2f, new Vector3(0.012f, 1.15f, 0.012f), palette.Iron);
            Object.DestroyImmediate(chain.GetComponent<Collider>());

            Transform tilt = Group("Tilt", root);
            tilt.position = pivot;
            GameObject pot = Primitive(PrimitiveType.Cylinder, "Pot", tilt, pivot, new Vector3(0.13f, 0.07f, 0.13f), palette.Iron);
            Object.DestroyImmediate(pot.GetComponent<Collider>());
            GameObject melt = Primitive(PrimitiveType.Cylinder, "Melt", tilt, pivot + Vector3.up * 0.068f, new Vector3(0.11f, 0.004f, 0.11f), palette.Molten);
            Object.DestroyImmediate(melt.GetComponent<Collider>());

            lip = new GameObject("Lip").transform;
            lip.SetParent(tilt, false);
            lip.position = pivot + new Vector3(0.065f, 0.07f, 0f);

            GameObject pour = Primitive(PrimitiveType.Cylinder, "Molten Stream", root, pivot, Vector3.one * 0.01f, palette.Molten);
            Object.DestroyImmediate(pour.GetComponent<Collider>());
            pour.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            stream = pour.transform;
            return tilt;
        }

        /// <summary>Builds the Resonance Wall's backboard and title, facing <paramref name="viewer"/>.</summary>
        /// <param name="palette">Materials.</param>
        /// <param name="position">Wall center.</param>
        /// <param name="viewer">Point the wall faces.</param>
        /// <returns>The wall object, ready for a <see cref="ResonanceWall"/>.</returns>
        internal static GameObject BuildWall(Palette palette, Vector3 position, Vector3 viewer)
        {
            GameObject wall = new GameObject("Resonance Wall");
            Vector3 away = position - viewer;
            away.y = 0f;
            wall.transform.SetPositionAndRotation(position, Quaternion.LookRotation(away.normalized, Vector3.up));
            GameObject board = Primitive(PrimitiveType.Cube, "Backboard", wall.transform, position, new Vector3(1.02f, 0.78f, 0.03f), palette.Iron);
            board.transform.SetPositionAndRotation(position + wall.transform.forward * 0.03f, wall.transform.rotation);
            Object.DestroyImmediate(board.GetComponent<Collider>());

            GameObject title = new GameObject("Wall Title");
            title.transform.SetParent(wall.transform, false);
            title.transform.localPosition = new Vector3(0f, 0.47f, 0f);
            TextMesh text = title.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 48;
            text.characterSize = 0.1f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = new Color(1f, 0.8f, 0.5f);
            text.text = "RESONANCE WALL";
            title.transform.localScale = Vector3.one * 0.05f;
            MeshRenderer textRenderer = title.GetComponent<MeshRenderer>();
            textRenderer.sharedMaterial = palette.Text;
            textRenderer.shadowCastingMode = ShadowCastingMode.Off;
            title.AddComponent<WorldText>();
            return wall;
        }

        static void BuildFurnace(Palette palette, Transform parent, Vector3 position)
        {
            Transform furnace = Group("Furnace", parent);
            furnace.position = position;
            Static(Primitive(PrimitiveType.Cube, "Body", furnace, position + new Vector3(0f, 0.7f, 0f), new Vector3(2f, 1.4f, 1.2f), palette.Stone));
            Static(Primitive(PrimitiveType.Cube, "Chimney", furnace, position + new Vector3(0f, 2.7f, 0.15f), new Vector3(1f, 2.7f, 0.9f), palette.Stone));
            Static(Primitive(PrimitiveType.Cube, "Lintel", furnace, position + new Vector3(0f, 1.18f, -0.62f), new Vector3(1.3f, 0.16f, 0.12f), palette.Iron));
            Static(Primitive(PrimitiveType.Cube, "Mouth", furnace, position + new Vector3(0f, 0.78f, -0.6f), new Vector3(1.05f, 0.62f, 0.04f), palette.Molten));
            Static(Primitive(PrimitiveType.Cube, "Hearth", furnace, position + new Vector3(0f, 0.42f, -0.72f), new Vector3(1.3f, 0.1f, 0.3f), palette.Iron));

            Light light = PointLight("Furnace Light", furnace, position + new Vector3(0f, 0.85f, -0.95f), new Color(1f, 0.5f, 0.18f), 4f, 7.5f);
            ForgeFlicker flicker = light.gameObject.AddComponent<ForgeFlicker>();
            SetFloat(flicker, "_intensity", 4f);
            SetFloat(flicker, "_flicker", 0.3f);

            Embers("Furnace Embers", furnace, position + new Vector3(0f, 1.05f, -0.75f), palette.Ember, 22f, 0.45f);
        }

        static void BuildBrazier(Palette palette, Transform parent, Vector3 position)
        {
            Transform brazier = Group("Brazier", parent);
            brazier.position = position;
            Static(Primitive(PrimitiveType.Cylinder, "Stand", brazier, position + new Vector3(0f, 0.45f, 0f), new Vector3(0.14f, 0.45f, 0.14f), palette.Iron));
            Static(Primitive(PrimitiveType.Cylinder, "Bowl", brazier, position + new Vector3(0f, 0.93f, 0f), new Vector3(0.5f, 0.05f, 0.5f), palette.Iron));
            Static(Primitive(PrimitiveType.Sphere, "Coals", brazier, position + new Vector3(0f, 0.98f, 0f), new Vector3(0.42f, 0.12f, 0.42f), palette.Molten));

            Light light = PointLight("Brazier Light", brazier, position + new Vector3(0f, 1.3f, 0f), new Color(1f, 0.55f, 0.22f), 1.6f, 3.5f);
            ForgeFlicker flicker = light.gameObject.AddComponent<ForgeFlicker>();
            SetFloat(flicker, "_intensity", 1.6f);
            SetFloat(flicker, "_flicker", 0.35f);
            SetFloat(flicker, "_speed", 9f);

            Embers("Brazier Embers", brazier, position + new Vector3(0f, 1.02f, 0f), palette.Ember, 7f, 0.16f);
        }

        static void BuildAnvil(Palette palette, Transform parent, Vector3 position, float top)
        {
            Transform anvil = Group("Anvil", parent);
            anvil.position = position;
            float faceHeight = 0.14f;
            float waistTop = top - faceHeight;
            Primitive(PrimitiveType.Cube, "Base", anvil, position + new Vector3(0f, 0.2f, 0f), new Vector3(0.44f, 0.4f, 0.34f), palette.Iron);
            Primitive(PrimitiveType.Cube, "Waist", anvil, position + new Vector3(0f, (0.4f + waistTop) * 0.5f, 0f), new Vector3(0.22f, waistTop - 0.4f, 0.18f), palette.Iron);
            Primitive(PrimitiveType.Cube, "Face", anvil, position + new Vector3(0f, top - faceHeight * 0.5f, 0f), new Vector3(0.6f, faceHeight, 0.26f), palette.Iron);
            GameObject horn = Primitive(PrimitiveType.Cylinder, "Horn", anvil, position + new Vector3(0.39f, top - faceHeight * 0.45f, 0f), new Vector3(0.09f, 0.12f, 0.09f), palette.Iron);
            horn.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
        }

        #region Helpers

        internal static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            if (parent != null)
            {
                primitive.transform.SetParent(parent, false);
            }

            primitive.transform.SetPositionAndRotation(position, Quaternion.identity);
            primitive.transform.localScale = scale;
            if (material != null && primitive.TryGetComponent(out Renderer renderer))
            {
                renderer.sharedMaterial = material;
            }

            return primitive;
        }

        static void Static(GameObject gameObject)
        {
            gameObject.isStatic = true;
        }

        static Transform Group(string name, Transform parent)
        {
            GameObject group = new GameObject(name);
            group.transform.SetParent(parent, false);
            return group.transform;
        }

        internal static Light PointLight(string name, Transform parent, Vector3 position, Color color, float intensity, float range)
        {
            GameObject host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.transform.position = position;
            Light light = host.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }

        static void Embers(string name, Transform parent, Vector3 position, Material material, float rate, float radius)
        {
            GameObject host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.transform.position = position;
            ForgeEmbers embers = host.AddComponent<ForgeEmbers>();
            SetObject(embers, "_material", material);
            SetFloat(embers, "_rate", rate);
            SetFloat(embers, "_radius", radius);
        }

        internal static void SetFloat(Object target, string property, float value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty found = serialized.FindProperty(property);
            if (found == null)
            {
                Missing(target, property);
                return;
            }

            found.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetBool(Object target, string property, bool value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty found = serialized.FindProperty(property);
            if (found == null)
            {
                Missing(target, property);
                return;
            }

            found.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetVector3(Object target, string property, Vector3 value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty found = serialized.FindProperty(property);
            if (found == null)
            {
                Missing(target, property);
                return;
            }

            found.vector3Value = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetObject(Object target, string property, Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty found = serialized.FindProperty(property);
            if (found == null)
            {
                Missing(target, property);
                return;
            }

            found.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Missing(Object target, string property)
        {
            ForgeLog.Warn(ForgeLogChannel.Core, nameof(ForgeEnvironmentBuilder), "SERIALIZED_FIELD_MISSING",
                target.GetType().Name + "." + property + " was not found; set it in the Inspector.", target);
        }

        #endregion

        #region Assets

        static Material GetOrCreateLit(string name, Color color, float metallic, float smoothness, Texture2D baseMap, float tiling)
        {
            return GetOrCreate(name, "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Metallic", metallic);
                m.SetFloat("_Smoothness", smoothness);
                if (baseMap != null)
                {
                    m.SetTexture("_BaseMap", baseMap);
                    m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
                }
            });
        }

        static Material GetOrCreate(string name, string shaderName, System.Action<Material> setup)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                ForgeLog.Warn(ForgeLogChannel.Core, nameof(ForgeEnvironmentBuilder), "SHADER_MISSING",
                    "Shader '" + shaderName + "' not found; material '" + name + "' was not created.", null);
                return null;
            }

            EnsureFolder(MaterialFolder);
            Material material = new Material(shader) { name = name };
            setup(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Texture2D GetOrCreateStoneTexture()
        {
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(StoneTexturePath);
            if (existing != null)
            {
                return existing;
            }

            EnsureFolder(GeneratedFolder);
            const int Size = 256;
            const int Cell = 64;
            Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    // Flagstones: fractal noise per stone, a per-stone tone, and dark mortar joints. Tiles seamlessly.
                    int row = y / Cell;
                    int shift = (row & 1) * (Cell / 2);
                    int cx = ((x + shift) % Size) / Cell;
                    float tone = 0.8f + 0.25f * Hash(cx * 7 + row * 13);
                    float noise = 0.55f * Noise(x, y, 32) + 0.3f * Noise(x, y, 16) + 0.15f * Noise(x, y, 8);
                    int lx = (x + shift) % Cell;
                    int ly = y % Cell;
                    float edge = Mathf.Min(Mathf.Min(lx, Cell - 1 - lx), Mathf.Min(ly, Cell - 1 - ly));
                    float mortar = Mathf.SmoothStep(0.35f, 1f, Mathf.Clamp01(edge / 3f));
                    float value = Mathf.Clamp01(tone * (0.55f + 0.45f * noise) * Mathf.Lerp(0.3f, 1f, mortar));
                    byte v = (byte)Mathf.RoundToInt(value * 255f);
                    pixels[y * Size + x] = new Color32(v, v, v, 255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(StoneTexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(StoneTexturePath);
            if (AssetImporter.GetAtPath(StoneTexturePath) is TextureImporter importer)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(StoneTexturePath);
        }

        static Cubemap GetOrCreateReflection()
        {
            Cubemap existing = AssetDatabase.LoadAssetAtPath<Cubemap>(ReflectionPath);
            if (existing != null)
            {
                return existing;
            }

            EnsureFolder(GeneratedFolder);
            const int Size = 32;
            Cubemap cubemap = new Cubemap(Size, TextureFormat.RGBA32, false) { name = "Forge Reflection" };
            for (int face = 0; face < 6; face++)
            {
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        float s = 2f * (x + 0.5f) / Size - 1f;
                        float t = 2f * (y + 0.5f) / Size - 1f;
                        Vector3 direction;
                        switch ((CubemapFace)face)
                        {
                            case CubemapFace.PositiveX: direction = new Vector3(1f, t, -s); break;
                            case CubemapFace.NegativeX: direction = new Vector3(-1f, t, s); break;
                            case CubemapFace.PositiveY: direction = new Vector3(s, 1f, -t); break;
                            case CubemapFace.NegativeY: direction = new Vector3(s, -1f, t); break;
                            case CubemapFace.PositiveZ: direction = new Vector3(s, t, 1f); break;
                            default: direction = new Vector3(-s, t, -1f); break;
                        }

                        cubemap.SetPixel((CubemapFace)face, x, y, Environment(direction.normalized));
                    }
                }
            }

            cubemap.Apply();
            AssetDatabase.CreateAsset(cubemap, ReflectionPath);
            return cubemap;
        }

        static Color Environment(Vector3 direction)
        {
            // Dim stone all around, the furnace's glow ahead, two braziers to the sides, warm bounce from the floor.
            float furnace = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(direction, new Vector3(0f, 0.1f, 1f).normalized)), 5f);
            float left = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(direction, new Vector3(-0.75f, 0.1f, 0.65f).normalized)), 18f);
            float right = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(direction, new Vector3(0.75f, 0.1f, 0.65f).normalized)), 18f);
            float horizon = 1f - Mathf.Abs(direction.y);
            float floor = Mathf.Max(0f, -direction.y);
            Color color = new Color(0.11f, 0.09f, 0.08f) * (0.5f + 0.5f * horizon);
            color += new Color(1f, 0.45f, 0.12f) * (furnace * 1.3f);
            color += new Color(1f, 0.5f, 0.18f) * ((left + right) * 0.7f);
            color += new Color(0.2f, 0.1f, 0.05f) * floor;
            color.a = 1f;
            return color;
        }

        static float Hash(int n)
        {
            n = (n << 13) ^ n;
            return 1f - ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824f * 0.5f;
        }

        static float Noise(int x, int y, int period)
        {
            // Value noise that tiles over the 256 px texture.
            int cells = 256 / period;
            float fx = (float)x / period;
            float fy = (float)y / period;
            int x0 = Mathf.FloorToInt(fx);
            int y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0;
            float ty = fy - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            float a = Lattice(x0, y0, cells);
            float b = Lattice(x0 + 1, y0, cells);
            float c = Lattice(x0, y0 + 1, cells);
            float d = Lattice(x0 + 1, y0 + 1, cells);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        static float Lattice(int x, int y, int cells)
        {
            x = ((x % cells) + cells) % cells;
            y = ((y % cells) + cells) % cells;
            return Mathf.Clamp01(Hash(x * 1619 + y * 31337 + cells * 6971));
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        #endregion
    }
}
