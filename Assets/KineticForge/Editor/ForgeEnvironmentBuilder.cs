using System.Collections.Generic;
using System.IO;
using KineticForge.Core;
using KineticForge.Feedback;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KineticForge.EditorTools
{
    /// <summary>
    /// Builds the forge around the sandbox: an enclosed, firelit stone hall built from scanned materials and models, with
    /// a furnace, two hearth fires, an anvil, a workbench, a quench trough, moonlight through a barred window, fog, light
    /// probes and a box-projected reflection probe, plus the generated textures and materials the scene uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lighting is tuned for Quest: the hall's light is baked (moonlight, the lantern and the furnace mouth's area glow),
    /// and the three fires are mixed lights, baking their bounce while their flickering direct light stays real time.
    /// Light probes carry the baked light onto moving objects. Exponential fog gives depth. A small generated cubemap
    /// remains the fallback reflection outside the baked probe.
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

        const string LightingSettingsPath = "Assets/KineticForge/Settings/Forge Lighting.lighting";

        // The hall: an enclosed stone forge around the player at the origin, who faces +Z.
        const float HallHalfWidth = 3.6f;
        const float HallFront = -2.6f;
        const float HallBack = 3.8f;
        const float HallHeight = 4f;
        const float WallThickness = 0.2f;
        const float WindowZ = 0.2f;
        const float WindowWidth = 1.2f;
        const float WindowSill = 1.3f;
        const float WindowTop = 2.7f;
        const float FurnaceZ = 2.6f;

        // Real-world repeat of each scanned material, in meters.
        const float FloorTile = 1.8f;
        const float BrickTile = 2.5f;
        const float TimberTile = 2f;
        const float PlankTile = 2f;
        const float IronTile = 1f;

        internal const string MetalShader = "Kinetic Forge/Forge Metal";
        internal const string GlowShader = "Kinetic Forge/Forge Glow";
        internal const string TextShader = "Kinetic Forge/Forge Text";
        internal const string PanelShader = "Kinetic Forge/Forge Panel";

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
            public Material FloorStone;
            public Material WallBrick;
            public Material Timber;
            public Material Planks;
            public Material ForgedIron;
            public Material AnvilFace;
            public Material NightSky;
            public Material Panel;
            public Material Flame;
            public Material Coals;
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

            // Scanned surfaces (Poly Haven, CC0), each falling back to a plain material when its maps are missing.
            palette.FloorStone = Scanned("monastery_stone_floor", 2048, Color.white, 1f, palette.Flagstone);
            palette.WallBrick = Scanned("castle_brick_07", 2048, new Color(0.9f, 0.86f, 0.84f), 1f, palette.Stone);
            palette.Timber = Scanned("medieval_wood", 1024, new Color(0.78f, 0.7f, 0.64f), 1f, palette.Stone);
            palette.Planks = Scanned("old_planks_02", 1024, new Color(0.72f, 0.65f, 0.6f), 1f, palette.Stone);
            palette.ForgedIron = Scanned("rusty_metal_02", 1024, new Color(0.5f, 0.47f, 0.46f), 0.9f, palette.Iron);
            palette.AnvilFace = GetOrCreateLit("Forge Anvil Face", new Color(0.64f, 0.63f, 0.62f), 1f, 0.48f, null, 1f);
            palette.NightSky = GetOrCreate("Forge Night Sky", "Universal Render Pipeline/Unlit",
                m => m.SetColor("_BaseColor", new Color(0.03f, 0.045f, 0.09f)));
            palette.Panel = GetOrCreate("Forge Panel", PanelShader, m => { });
            // Glowing coals: rough and non-metallic, so their edges never mirror the room; the oxide crust breaks up the glow.
            palette.Coals = GetOrCreate("Forge Coals", MetalShader, m =>
            {
                m.SetColor("_BaseColor", new Color(0.07f, 0.05f, 0.04f));
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Smoothness", 0.08f);
                m.SetFloat("_Heat", 0.62f);
                m.SetFloat("_HeatGlow", 1.25f);
                m.SetFloat("_OxideScale", 1f);
                m.SetFloat("_OxideSize", 30f);
                m.SetFloat("_HammerScale", 18f);
                m.SetFloat("_HammerStrength", 1f);
            });
            palette.Flame = GetOrCreate("Forge Flame", GlowShader, m =>
            {
                m.SetColor("_Color", new Color(1.6f, 1.05f, 0.55f));
                m.SetFloat("_Softness", 1.15f);
            });
            return palette;
        }

        /// <summary>Sets fog, ambient light, reflections and the moonlight.</summary>
        internal static void SetUpAtmosphere()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.035f, 0.04f, 0.055f);
            RenderSettings.ambientEquatorColor = new Color(0.045f, 0.032f, 0.025f);
            RenderSettings.ambientGroundColor = new Color(0.018f, 0.013f, 0.01f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.07f;
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
                // Baked: it falls through the window onto the floor, and the walls shade the rest of the hall.
                moon.name = "Moonlight";
                moon.transform.rotation = Quaternion.Euler(34f, 90f, 0f);
                moon.color = new Color(0.5f, 0.62f, 1f);
                moon.intensity = 0.9f;
                moon.shadows = LightShadows.Soft;
                moon.shadowStrength = 1f;
                moon.lightmapBakeType = LightmapBakeType.Baked;
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

        /// <summary>
        /// Builds the enclosed hall (stone floor, brick walls with a moonlit window, plank ceiling on timber beams), the
        /// furnace, two hearth fires, the anvil, workshop props, and the light and reflection probes.
        /// </summary>
        /// <param name="palette">Materials.</param>
        /// <param name="anvilPosition">Floor point under the anvil.</param>
        /// <param name="anvilTop">Height of the anvil face.</param>
        /// <returns>The forge's root object; its Floor, Hall and Furnace children are the parts mixed reality hides.</returns>
        internal static GameObject BuildForge(Palette palette, Vector3 anvilPosition, float anvilTop)
        {
            GameObject root = new GameObject("Forge");
            float depth = HallBack - HallFront;
            float centerZ = (HallBack + HallFront) * 0.5f;

            GameObject floor = Block("Floor", root.transform, new Vector3(0f, -0.05f, centerZ),
                new Vector3(2f * (HallHalfWidth + WallThickness), 0.1f, depth + 2f * WallThickness), palette.FloorStone, FloorTile,
                collider: true);

            Transform hall = Group("Hall", root.transform);
            BuildHall(palette, hall);
            BuildProps(palette, hall);

            BuildFurnace(palette, root.transform, new Vector3(0f, 0f, FurnaceZ));
            BuildHearthFire(palette, root.transform, new Vector3(-1.55f, 0f, 1.35f), 40f);
            BuildHearthFire(palette, root.transform, new Vector3(1.55f, 0f, 1.35f), 160f);
            BuildAnvil(palette, root.transform, anvilPosition, anvilTop);
            BuildLightingVolumes(root.transform);
            return root;
        }

        static void BuildHall(Palette palette, Transform hall)
        {
            float y = HallHeight * 0.5f;
            float depth = HallBack - HallFront;
            float centerZ = (HallBack + HallFront) * 0.5f;
            float outerWidth = 2f * (HallHalfWidth + WallThickness);
            float halfT = WallThickness * 0.5f;

            Block("Back Wall", hall, new Vector3(0f, y, HallBack + halfT), new Vector3(outerWidth, HallHeight, WallThickness), palette.WallBrick, BrickTile, collider: true);
            Block("Front Wall", hall, new Vector3(0f, y, HallFront - halfT), new Vector3(outerWidth, HallHeight, WallThickness), palette.WallBrick, BrickTile, collider: true);
            Block("Right Wall", hall, new Vector3(HallHalfWidth + halfT, y, centerZ), new Vector3(WallThickness, HallHeight, depth), palette.WallBrick, BrickTile, collider: true);

            // The left wall frames a barred window onto the night; moonlight falls through it onto the floor.
            float x = -HallHalfWidth - halfT;
            float near = WindowZ - WindowWidth * 0.5f;
            float far = WindowZ + WindowWidth * 0.5f;
            Block("Left Wall Front", hall, new Vector3(x, y, (HallFront + near) * 0.5f), new Vector3(WallThickness, HallHeight, near - HallFront), palette.WallBrick, BrickTile, collider: true);
            Block("Left Wall Back", hall, new Vector3(x, y, (far + HallBack) * 0.5f), new Vector3(WallThickness, HallHeight, HallBack - far), palette.WallBrick, BrickTile, collider: true);
            Block("Left Wall Below Window", hall, new Vector3(x, WindowSill * 0.5f, WindowZ), new Vector3(WallThickness, WindowSill, WindowWidth), palette.WallBrick, BrickTile, collider: true);
            Block("Left Wall Above Window", hall, new Vector3(x, (WindowTop + HallHeight) * 0.5f, WindowZ), new Vector3(WallThickness, HallHeight - WindowTop, WindowWidth), palette.WallBrick, BrickTile, collider: true);
            Block("Window Sill", hall, new Vector3(x + 0.05f, WindowSill - 0.03f, WindowZ), new Vector3(WallThickness + 0.14f, 0.06f, WindowWidth + 0.12f), palette.Timber, TimberTile);
            Block("Window Header", hall, new Vector3(x + 0.05f, WindowTop + 0.05f, WindowZ), new Vector3(WallThickness + 0.12f, 0.1f, WindowWidth + 0.12f), palette.Timber, TimberTile);
            for (int i = 0; i < 4; i++)
            {
                float z = near + WindowWidth * (i + 1) / 5f;
                Block("Window Bar", hall, new Vector3(x, (WindowSill + WindowTop) * 0.5f, z), new Vector3(0.024f, WindowTop - WindowSill, 0.024f), palette.ForgedIron, IronTile);
            }

            GameObject night = Block("Night", hall, new Vector3(x - 1.6f, (WindowSill + WindowTop) * 0.5f, WindowZ),
                new Vector3(0.02f, 3.2f, 3.6f), palette.NightSky, 1f, isStatic: false);
            night.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            Block("Ceiling", hall, new Vector3(0f, HallHeight + 0.05f, centerZ), new Vector3(outerWidth, 0.1f, depth + 2f * WallThickness), palette.Planks, PlankTile);
            float[] beams = { -1.5f, 0.55f, 2.6f };
            for (int i = 0; i < beams.Length; i++)
            {
                Block("Beam", hall, new Vector3(0f, HallHeight - 0.15f, beams[i]), new Vector3(2f * HallHalfWidth, 0.3f, 0.26f), palette.Timber, TimberTile);
            }

            for (int side = -1; side <= 1; side += 2)
            {
                for (int end = 0; end < 2; end++)
                {
                    float z = end == 0 ? HallFront + 0.15f : HallBack - 0.15f;
                    Block("Post", hall, new Vector3(side * (HallHalfWidth - 0.15f), y, z), new Vector3(0.26f, HallHeight, 0.26f), palette.Timber, TimberTile);
                }
            }
        }

        static void BuildProps(Palette palette, Transform hall)
        {
            // A workbench against the right wall: vice, hammer and a lantern that lights it.
            GameObject bench = ForgeAssetLibrary.Place("WoodenTable_03", "Workbench", hall, new Vector3(2.95f, 0f, 0.95f),
                Quaternion.Euler(0f, 90f, 0f), 1.327f, ForgeAssetLibrary.SizeAxis.Largest);
            if (bench != null)
            {
                float top = ForgeAssetLibrary.BoundsOf(bench).max.y;
                ForgeAssetLibrary.Place("bench_vice_01", "Bench Vice", hall, new Vector3(2.95f, top, 0.5f),
                    Quaternion.Euler(0f, 90f, 0f), 0.396f, ForgeAssetLibrary.SizeAxis.Largest);
                ForgeAssetLibrary.Place("cross_pein_hammer", "Cross Pein Hammer", hall, new Vector3(2.85f, top, 1.05f),
                    Quaternion.Euler(90f, 25f, 0f), 0.3f, ForgeAssetLibrary.SizeAxis.Largest);
                GameObject lantern = ForgeAssetLibrary.Place("wooden_lantern_01", "Lantern", hall, new Vector3(3.05f, top, 1.4f),
                    Quaternion.Euler(0f, -20f, 0f), 0.53f, ForgeAssetLibrary.SizeAxis.Largest);
                if (lantern != null)
                {
                    Bounds bounds = ForgeAssetLibrary.BoundsOf(lantern);
                    Light light = PointLight("Lantern Light", hall, bounds.center + Vector3.up * 0.04f, new Color(1f, 0.64f, 0.32f), 0.9f, 2.6f);
                    light.lightmapBakeType = LightmapBakeType.Baked;
                }
            }

            ForgeAssetLibrary.Place("worn_metal_rack", "Tool Rack", hall, new Vector3(3.28f, 0f, 2.7f), Quaternion.Euler(0f, -90f, 0f), 1.9f, ForgeAssetLibrary.SizeAxis.Largest);
            ForgeAssetLibrary.Place("barrel_03", "Barrel", hall, new Vector3(3.0f, 0f, -1.95f), Quaternion.Euler(0f, 25f, 0f), 0.93f, ForgeAssetLibrary.SizeAxis.Largest);
            ForgeAssetLibrary.Place("barrel_03", "Barrel", hall, new Vector3(2.3f, 0f, -2.15f), Quaternion.Euler(0f, 70f, 0f), 0.93f, ForgeAssetLibrary.SizeAxis.Largest);
            GameObject crate = ForgeAssetLibrary.Place("wooden_crate_01", "Crate", hall, new Vector3(-2.85f, 0f, 3.3f), Quaternion.Euler(0f, 8f, 0f), 0.825f, ForgeAssetLibrary.SizeAxis.Largest);
            if (crate != null)
            {
                float top = ForgeAssetLibrary.BoundsOf(crate).max.y;
                ForgeAssetLibrary.Place("wooden_crate_01", "Crate", hall, new Vector3(-2.8f, top, 3.28f), Quaternion.Euler(0f, -10f, 0f), 0.825f, ForgeAssetLibrary.SizeAxis.Largest);
            }

            ForgeAssetLibrary.Place("wooden_crate_01", "Crate", hall, new Vector3(-1.85f, 0f, 3.35f), Quaternion.Euler(0f, 84f, 0f), 0.825f, ForgeAssetLibrary.SizeAxis.Largest);
            ForgeAssetLibrary.Place("wooden_stool_01", "Stool", hall, new Vector3(-2.4f, 0f, -1.7f), Quaternion.Euler(0f, 30f, 0f), 0.442f, ForgeAssetLibrary.SizeAxis.Largest);
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
            const float Wall = 0.04f;
            Block("Base", trough, position + new Vector3(0f, 0.03f, 0f), new Vector3(size.x, 0.06f, size.z), palette.Planks, PlankTile);
            Block("Front", trough, position + new Vector3(0f, Height * 0.5f, -size.z * 0.5f + Wall * 0.5f), new Vector3(size.x, Height, Wall), palette.Planks, PlankTile);
            Block("Back", trough, position + new Vector3(0f, Height * 0.5f, size.z * 0.5f - Wall * 0.5f), new Vector3(size.x, Height, Wall), palette.Planks, PlankTile);
            Block("Left", trough, position + new Vector3(-size.x * 0.5f + Wall * 0.5f, Height * 0.5f, 0f), new Vector3(Wall, Height, size.z - 2f * Wall), palette.Planks, PlankTile);
            Block("Right", trough, position + new Vector3(size.x * 0.5f - Wall * 0.5f, Height * 0.5f, 0f), new Vector3(Wall, Height, size.z - 2f * Wall), palette.Planks, PlankTile);
            for (int i = 0; i < 2; i++)
            {
                float bandY = i == 0 ? 0.12f : Height - 0.1f;
                Block("Iron Band", trough, position + new Vector3(0f, bandY, 0f), new Vector3(size.x + 0.012f, 0.035f, size.z + 0.012f), palette.ForgedIron, IronTile);
            }

            GameObject water = Primitive(PrimitiveType.Cube, "Water", trough, position + new Vector3(0f, Height - 0.04f, 0f),
                new Vector3(size.x - 2f * Wall, 0.01f, size.z - 2f * Wall), palette.Water);
            Object.DestroyImmediate(water.GetComponent<Collider>());

            ForgeAssetLibrary.Place("wooden_bucket_01", "Bucket", trough, position + new Vector3(0.45f, 0f, -0.3f),
                Quaternion.Euler(0f, 35f, 0f), 0.42f, ForgeAssetLibrary.SizeAxis.Height);

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
            GameObject chain = Primitive(PrimitiveType.Cylinder, "Chain", root, pivot + Vector3.up * 1.2f, new Vector3(0.012f, 1.15f, 0.012f), palette.ForgedIron);
            Object.DestroyImmediate(chain.GetComponent<Collider>());

            Transform tilt = Group("Tilt", root);
            tilt.position = pivot;

            // A scanned pot when present: it moves, so it stays dynamic and is lit by the light probes.
            float rim = 0.07f;
            float radius = 0.065f;
            GameObject pot = ForgeAssetLibrary.Place("brass_pot_01", "Pot", tilt, pivot, Quaternion.identity, 0.18f,
                ForgeAssetLibrary.SizeAxis.Largest, centerVertically: true, isStatic: false);
            if (pot != null)
            {
                Bounds bounds = ForgeAssetLibrary.BoundsOf(pot);
                rim = bounds.max.y - pivot.y;
                radius = bounds.extents.x;
            }
            else
            {
                pot = Primitive(PrimitiveType.Cylinder, "Pot", tilt, pivot, new Vector3(0.13f, 0.07f, 0.13f), palette.ForgedIron);
                Object.DestroyImmediate(pot.GetComponent<Collider>());
            }

            GameObject melt = Primitive(PrimitiveType.Cylinder, "Melt", tilt, pivot + Vector3.up * (rim - 0.012f),
                new Vector3(radius * 1.6f, 0.004f, radius * 1.6f), palette.Molten);
            Object.DestroyImmediate(melt.GetComponent<Collider>());

            lip = new GameObject("Lip").transform;
            lip.SetParent(tilt, false);
            lip.position = pivot + new Vector3(radius * 0.96f, rim, 0f);

            GameObject pour = Primitive(PrimitiveType.Cylinder, "Molten Stream", root, pivot, Vector3.one * 0.01f, palette.Molten);
            Object.DestroyImmediate(pour.GetComponent<Collider>());
            pour.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            stream = pour.transform;
            return tilt;
        }

        /// <summary>Builds the Resonance Wall's timber board, iron frame and title, facing <paramref name="viewer"/>.</summary>
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
            const float Width = 1.02f;
            const float Height = 0.78f;
            LocalBlock("Backboard", wall.transform, new Vector3(0f, 0f, 0.03f), new Vector3(Width, Height, 0.03f), palette.Timber, TimberTile);
            LocalBlock("Frame Top", wall.transform, new Vector3(0f, Height * 0.5f, 0.02f), new Vector3(Width + 0.06f, 0.035f, 0.045f), palette.ForgedIron, IronTile);
            LocalBlock("Frame Bottom", wall.transform, new Vector3(0f, -Height * 0.5f, 0.02f), new Vector3(Width + 0.06f, 0.035f, 0.045f), palette.ForgedIron, IronTile);
            LocalBlock("Frame Left", wall.transform, new Vector3(-Width * 0.5f - 0.012f, 0f, 0.02f), new Vector3(0.035f, Height, 0.045f), palette.ForgedIron, IronTile);
            LocalBlock("Frame Right", wall.transform, new Vector3(Width * 0.5f + 0.012f, 0f, 0.02f), new Vector3(0.035f, Height, 0.045f), palette.ForgedIron, IronTile);

            GameObject title = new GameObject("Wall Title");
            title.transform.SetParent(wall.transform, false);
            title.transform.localPosition = new Vector3(0f, Height * 0.5f + 0.07f, 0f);
            TextMeshPro text = title.AddComponent<TextMeshPro>();
            TMP_FontAsset font = ForgeTypography.DisplayFont();
            if (font != null)
            {
                text.font = font;
            }

            text.fontSize = ForgeGuide.FontSizeFor(text.font, 0.05f);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.characterSpacing = 8f;
            text.color = new Color(1f, 0.8f, 0.5f);
            text.text = "RESONANCE WALL";
            text.rectTransform.sizeDelta = new Vector2(1.2f, 0.12f);
            MeshRenderer textRenderer = title.GetComponent<MeshRenderer>();
            textRenderer.shadowCastingMode = ShadowCastingMode.Off;
            textRenderer.receiveShadows = false;
            return wall;
        }

        static void BuildFurnace(Palette palette, Transform parent, Vector3 position)
        {
            // A brick furnace with an open fire chamber: brick piers around the opening, a coal bed inside, and flames
            // rising in front of the chamber's back wall, which the fire lights.
            Transform furnace = Group("Furnace", parent);
            furnace.position = position;
            const float Width = 2f;
            const float Height = 1.4f;
            const float Depth = 1.2f;
            const float Plinth = 0.16f;
            const float OpeningWidth = 1.05f;
            const float OpeningBottom = 0.47f;
            const float OpeningTop = 1.09f;
            const float ChamberDepth = 0.42f;
            float front = position.z - Depth * 0.5f;
            float back = position.z + Depth * 0.5f;
            float pierWidth = (Width - OpeningWidth) * 0.5f;
            float chamberBack = front + ChamberDepth;

            Block("Plinth", furnace, position + new Vector3(0f, Plinth * 0.5f, 0f), new Vector3(Width + 0.2f, Plinth, Depth + 0.2f), palette.FloorStone, FloorTile, collider: true);
            for (int side = -1; side <= 1; side += 2)
            {
                Block(side < 0 ? "Pier Left" : "Pier Right", furnace,
                    new Vector3(position.x + side * (OpeningWidth + pierWidth) * 0.5f, (Plinth + Height) * 0.5f, position.z),
                    new Vector3(pierWidth, Height - Plinth, Depth), palette.WallBrick, BrickTile, collider: true);
            }

            Block("Below Opening", furnace, new Vector3(position.x, (Plinth + OpeningBottom) * 0.5f, position.z),
                new Vector3(OpeningWidth, OpeningBottom - Plinth, Depth), palette.WallBrick, BrickTile, collider: true);
            Block("Above Opening", furnace, new Vector3(position.x, (OpeningTop + Height) * 0.5f, position.z),
                new Vector3(OpeningWidth, Height - OpeningTop, Depth), palette.WallBrick, BrickTile, collider: true);
            Block("Chamber Back", furnace, new Vector3(position.x, (OpeningBottom + OpeningTop) * 0.5f, (chamberBack + back) * 0.5f),
                new Vector3(OpeningWidth, OpeningTop - OpeningBottom, back - chamberBack), palette.WallBrick, BrickTile, collider: true);
            Block("Chimney", furnace, new Vector3(position.x, (Height + HallHeight) * 0.5f, position.z + 0.15f), new Vector3(1.1f, HallHeight - Height, 0.9f), palette.WallBrick, BrickTile, collider: true);
            Block("Lintel", furnace, new Vector3(position.x, OpeningTop + 0.05f, front - 0.06f), new Vector3(1.3f, 0.16f, 0.12f), palette.ForgedIron, IronTile);
            Block("Hearth", furnace, new Vector3(position.x, OpeningBottom - 0.05f, front - 0.15f), new Vector3(1.3f, 0.1f, 0.3f), palette.ForgedIron, IronTile);

            // The coal bed: glowing metal-hot embers along the chamber floor.
            GameObject coals = Primitive(PrimitiveType.Sphere, "Coal Bed", furnace,
                new Vector3(position.x, OpeningBottom + 0.02f, front + ChamberDepth * 0.5f), new Vector3(OpeningWidth - 0.12f, 0.1f, ChamberDepth - 0.08f), palette.Coals);
            Object.DestroyImmediate(coals.GetComponent<Collider>());

            Light chamber = PointLight("Chamber Light", furnace, new Vector3(position.x, OpeningBottom + 0.3f, front + 0.12f), new Color(1f, 0.52f, 0.2f), 2.4f, 2.2f);
            chamber.lightmapBakeType = LightmapBakeType.Mixed;
            ForgeFlicker chamberFlicker = chamber.gameObject.AddComponent<ForgeFlicker>();
            SetFloat(chamberFlicker, "_intensity", 2.4f);
            SetFloat(chamberFlicker, "_flicker", 0.35f);
            SetFloat(chamberFlicker, "_speed", 8f);

            Light light = PointLight("Furnace Light", furnace, new Vector3(position.x, 0.85f, front - 0.35f), new Color(1f, 0.5f, 0.18f), 2.6f, 7f);
            light.lightmapBakeType = LightmapBakeType.Mixed;
            ForgeFlicker flicker = light.gameObject.AddComponent<ForgeFlicker>();
            SetFloat(flicker, "_intensity", 2.6f);
            SetFloat(flicker, "_flicker", 0.3f);

            // The opening as a soft baked area light, so the hall carries the fire's warm bounce.
            AreaGlow("Furnace Mouth Glow", furnace, new Vector3(position.x, (OpeningBottom + OpeningTop) * 0.5f, front - 0.02f),
                new Vector2(OpeningWidth - 0.05f, OpeningTop - OpeningBottom - 0.05f), new Color(1f, 0.45f, 0.15f), 4f);

            Flame("Furnace Flames", furnace, new Vector3(position.x, OpeningBottom + 0.04f, front + ChamberDepth * 0.45f), palette.Flame, 0.38f, 1.1f, 40f);
            Embers("Furnace Embers", furnace, new Vector3(position.x, OpeningTop - 0.05f, front - 0.1f), palette.Ember, 22f, 0.45f);
        }

        static void BuildHearthFire(Palette palette, Transform parent, Vector3 position, float yaw)
        {
            Transform hearth = Group("Hearth Fire", parent);
            hearth.position = position;
            GameObject pit = ForgeAssetLibrary.Place("stone_fire_pit", "Fire Pit", hearth, position, Quaternion.Euler(0f, yaw, 0f),
                0.95f, ForgeAssetLibrary.SizeAxis.Largest);
            float bed = 0.1f;
            if (pit == null)
            {
                // Without the scanned stones: an iron brazier on a stand.
                Block("Stand", hearth, position + new Vector3(0f, 0.45f, 0f), new Vector3(0.12f, 0.9f, 0.12f), palette.ForgedIron, IronTile);
                Block("Bowl", hearth, position + new Vector3(0f, 0.93f, 0f), new Vector3(0.5f, 0.08f, 0.5f), palette.ForgedIron, IronTile);
                bed = 0.98f;
            }

            GameObject coals = Primitive(PrimitiveType.Sphere, "Coals", hearth, position + Vector3.up * bed, new Vector3(0.42f, 0.08f, 0.42f), palette.Coals);
            Object.DestroyImmediate(coals.GetComponent<Collider>());

            Light light = PointLight("Fire Light", hearth, position + Vector3.up * (bed + 0.5f), new Color(1f, 0.55f, 0.22f), 1.8f, 3.6f);
            light.lightmapBakeType = LightmapBakeType.Mixed;
            ForgeFlicker flicker = light.gameObject.AddComponent<ForgeFlicker>();
            SetFloat(flicker, "_intensity", 1.8f);
            SetFloat(flicker, "_flicker", 0.35f);
            SetFloat(flicker, "_speed", 9f);

            Flame("Flames", hearth, position + Vector3.up * (bed + 0.02f), palette.Flame, 0.15f, 0.9f, 24f);
            Embers("Fire Embers", hearth, position + Vector3.up * (bed + 0.1f), palette.Ember, 7f, 0.16f);
        }

        static void BuildAnvil(Palette palette, Transform parent, Vector3 position, float top)
        {
            // A London-pattern anvil on a timber stump: feet, a waist that narrows, a body with its heel to the left and
            // a lower step to the right, then the horn. The relic rests on the polished face, centered on the anvil.
            Transform anvil = Group("Anvil", parent);
            anvil.position = position;
            const float StumpHeight = 0.4f;
            const float FeetHeight = 0.08f;
            const float FootTaper = 0.06f;
            const float BodyHeight = 0.13f;
            const float PlateHeight = 0.01f;
            const float StepDrop = 0.028f;
            float bodyBottom = top - PlateHeight - BodyHeight;
            float feetTop = StumpHeight + FeetHeight;
            float taperTop = feetTop + FootTaper;

            Block("Stump", anvil, position + new Vector3(0f, StumpHeight * 0.5f, 0f), new Vector3(0.52f, StumpHeight, 0.44f), palette.Timber, TimberTile, collider: true);
            Block("Feet", anvil, position + new Vector3(0f, StumpHeight + FeetHeight * 0.5f, 0f), new Vector3(0.46f, FeetHeight, 0.32f), palette.ForgedIron, IronTile, collider: true);
            Block("Lower Waist", anvil, position + new Vector3(0f, feetTop + FootTaper * 0.5f, 0f), new Vector3(0.3f, FootTaper, 0.22f), palette.ForgedIron, IronTile, collider: true);
            Block("Waist", anvil, position + new Vector3(0f, (taperTop + bodyBottom) * 0.5f, 0f), new Vector3(0.19f, bodyBottom - taperTop, 0.15f), palette.ForgedIron, IronTile, collider: true);
            Block("Body", anvil, position + new Vector3(-0.03f, bodyBottom + BodyHeight * 0.5f, 0f), new Vector3(0.5f, BodyHeight, 0.24f), palette.ForgedIron, IronTile, collider: true);
            Block("Face", anvil, position + new Vector3(-0.03f, top - PlateHeight * 0.5f, 0f), new Vector3(0.48f, PlateHeight, 0.22f), palette.AnvilFace, IronTile, collider: true);
            Block("Step", anvil, position + new Vector3(0.26f, bodyBottom + (BodyHeight - StepDrop) * 0.5f, 0f), new Vector3(0.08f, BodyHeight - StepDrop, 0.2f), palette.ForgedIron, IronTile, collider: true);

            GameObject horn = new GameObject("Horn");
            horn.transform.SetParent(anvil, false);
            horn.transform.position = position + new Vector3(0.3f, bodyBottom + (BodyHeight - StepDrop) * 0.55f, 0f);
            horn.AddComponent<MeshFilter>().sharedMesh = ForgeMeshes.Horn(0.058f, 0.01f, 0.24f, IronTile);
            horn.AddComponent<MeshRenderer>().sharedMaterial = palette.ForgedIron;
            ForgeAssetLibrary.MarkStatic(horn);

            // A sledgehammer lies on the floor behind and right of the anvil, out of the line of sight to the work.
            ForgeAssetLibrary.Place("sledgehammer_01", "Sledgehammer", anvil, position + new Vector3(0.5f, 0f, 0.42f),
                Quaternion.Euler(0f, -35f, 90f), 1.006f, ForgeAssetLibrary.SizeAxis.Largest);
        }

        static void BuildLightingVolumes(Transform parent)
        {
            // Light probes carry the baked bounce onto everything that moves: the relic, the crucible, the hands.
            GameObject probes = new GameObject("Light Probes");
            probes.transform.SetParent(parent, false);
            float[] xs = { -2.6f, -1.6f, -0.8f, 0f, 0.8f, 1.6f, 2.6f };
            float[] ys = { 0.3f, 0.9f, 1.5f, 2.3f, 3.4f };
            float[] zs = { -1.8f, -0.8f, 0f, 0.55f, 1.1f, 1.7f };
            List<Vector3> positions = new List<Vector3>(xs.Length * ys.Length * zs.Length);
            for (int i = 0; i < xs.Length; i++)
            {
                for (int j = 0; j < ys.Length; j++)
                {
                    for (int k = 0; k < zs.Length; k++)
                    {
                        positions.Add(new Vector3(xs[i], ys[j], zs[k]));
                    }
                }
            }

            probes.AddComponent<LightProbeGroup>().probePositions = positions.ToArray();

            // One box-projected probe for the room, so metal reflects the actual hall.
            float centerZ = (HallBack + HallFront) * 0.5f;
            GameObject reflectionObject = new GameObject("Reflection Probe");
            reflectionObject.transform.SetParent(parent, false);
            reflectionObject.transform.position = new Vector3(0f, 1.4f, centerZ);
            ReflectionProbe reflection = reflectionObject.AddComponent<ReflectionProbe>();
            reflection.mode = ReflectionProbeMode.Baked;
            reflection.boxProjection = true;
            reflection.size = new Vector3(2f * HallHalfWidth, HallHeight, HallBack - HallFront);
            reflection.center = new Vector3(0f, HallHeight * 0.5f - 1.4f, 0f);
            reflection.resolution = 128;
            reflection.importance = 1;
        }

        /// <summary>
        /// Creates the forge's lighting settings and makes them the active scene's: baked global illumination with mixed
        /// lights baking their bounce only, sized for Quest.
        /// </summary>
        internal static void ApplyLightingSettings()
        {
            LightingSettings settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (settings == null)
            {
                EnsureFolder(System.IO.Path.GetDirectoryName(LightingSettingsPath).Replace('\\', '/'));
                settings = new LightingSettings { name = "Forge Lighting" };
                AssetDatabase.CreateAsset(settings, LightingSettingsPath);
            }

            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            settings.lightmapResolution = 12f;
            settings.lightmapPadding = 3;
            settings.lightmapMaxSize = 1024;
            settings.directionalityMode = LightmapsMode.NonDirectional;
            settings.lightmapCompression = LightmapCompression.NormalQuality;
            settings.ao = true;
            settings.aoMaxDistance = 0.5f;
            settings.directSampleCount = 32;
            settings.indirectSampleCount = 256;
            settings.environmentSampleCount = 64;
            settings.maxBounces = 3;
            EditorUtility.SetDirty(settings);
            Lightmapping.lightingSettings = settings;
        }

        /// <summary>A static block with world-scaled texture coordinates.</summary>
        static GameObject Block(string name, Transform parent, Vector3 center, Vector3 size, Material material, float tile,
            bool isStatic = true, bool collider = false)
        {
            GameObject block = new GameObject(name);
            block.transform.SetParent(parent, false);
            block.transform.SetPositionAndRotation(center, Quaternion.identity);
            return FinishBlock(block, size, material, tile, isStatic, collider);
        }

        /// <summary>A static block placed in its parent's space, for rotated assemblies such as the wall.</summary>
        static GameObject LocalBlock(string name, Transform parent, Vector3 localCenter, Vector3 size, Material material, float tile)
        {
            GameObject block = new GameObject(name);
            block.transform.SetParent(parent, false);
            block.transform.localPosition = localCenter;
            block.transform.localRotation = Quaternion.identity;
            return FinishBlock(block, size, material, tile, true, false);
        }

        static GameObject FinishBlock(GameObject block, Vector3 size, Material material, float tile, bool isStatic, bool collider)
        {
            block.AddComponent<MeshFilter>().sharedMesh = ForgeMeshes.Box(size, tile);
            block.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (collider)
            {
                block.AddComponent<BoxCollider>();
            }

            if (isStatic)
            {
                ForgeAssetLibrary.MarkStatic(block);
            }

            return block;
        }

        static void Flame(string name, Transform parent, Vector3 position, Material material, float radius, float scale, float rate)
        {
            GameObject host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.transform.position = position;
            ForgeFlame flame = host.AddComponent<ForgeFlame>();
            SetObject(flame, "_material", material);
            SetFloat(flame, "_radius", radius);
            SetFloat(flame, "_scale", scale);
            SetFloat(flame, "_rate", rate);
        }

        static void AreaGlow(string name, Transform parent, Vector3 position, Vector2 size, Color color, float intensity)
        {
            GameObject host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.back, Vector3.up));
            Light light = host.AddComponent<Light>();
            light.type = LightType.Rectangle;
            light.areaSize = size;
            light.color = color;
            light.intensity = intensity;
            light.lightmapBakeType = LightmapBakeType.Baked;
        }

        /// <summary>Adds a glass plate behind a world-space label, fitted to its text by a <see cref="ForgePanel"/>.</summary>
        /// <param name="label">The label's transform; the plate becomes its child.</param>
        /// <param name="text">The label.</param>
        /// <param name="material">Forge Panel material.</param>
        internal static void CreatePlate(Transform label, TMP_Text text, Material material)
        {
            if (material == null)
            {
                return;
            }

            GameObject plate = new GameObject("Plate");
            plate.transform.SetParent(label, false);
            plate.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            MeshRenderer renderer = plate.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            ForgePanel panel = plate.AddComponent<ForgePanel>();
            SetObject(panel, "_text", text);
        }

        static Material Scanned(string id, int maxSize, Color tint, float smoothness, Material fallback)
        {
            Material material = ForgeAssetLibrary.Has(id) ? ForgeAssetLibrary.Material(id, Vector2.one, maxSize, tint, smoothness) : null;
            return material != null ? material : fallback;
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
