using System.Collections.Generic;
using System.IO;
using KineticForge.Core;
using KineticForge.Crafting;
using KineticForge.Feedback;
using KineticForge.Physics;
using KineticForge.Platform;
using KineticForge.Tracking;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KineticForge.EditorTools
{
    /// <summary>
    /// One-click setup: builds a ready-to-play sandbox scene with the full Dual-Scale loop, in the Editor and on Quest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scene holds a floor, an anvil and a 20 kg bronze relic carrying the whole carving stack:
    /// <see cref="RelicStabilizer"/>, <see cref="RelicGrabDriver"/>, <see cref="RelicHoldSimulator"/>,
    /// <see cref="RelicCarvingSurface"/>, <see cref="CarvingCursor"/> and <see cref="ChiselStrikeController"/>. A systems
    /// object carries the runtime guard, game flow, stance tracker, frame pacing, session monitor, and one
    /// <see cref="MicroGestureTracker"/> and <see cref="HandGrabber"/> per hand. The scene is saved to
    /// <c>Assets/KineticForge/Scenes/Sandbox.unity</c> and added to the build settings.
    /// </para>
    /// <para>
    /// With the Meta XR Core SDK installed, the builder also adds the <c>OVRCameraRig</c> at a floor-level origin, a
    /// full-body <c>OVRBody</c> and <c>OVRSkeleton</c> under its TrackingSpace, and both tracked hands, and enables body
    /// and hand tracking in the project settings. Without the SDK, the default camera stays and the keyboard and mouse
    /// fallbacks drive everything.
    /// </para>
    /// </remarks>
    internal static class SandboxSceneBuilder
    {
        const string LogSource = nameof(SandboxSceneBuilder);

        /// <summary>Where the sandbox scene is saved.</summary>
        internal const string ScenePath = SceneFolder + "/Sandbox.unity";

        const string SceneFolder = "Assets/KineticForge/Scenes";
        const float AnvilHeight = 0.78f;
        const float AnvilDistance = 0.55f;
        const float HoldLift = 0.22f;
        const float EditorEyeHeight = 1.45f;
        const float GuideLineHeight = 0.034f;
        const float TunerScale = 1.2f;
        const float RelicMass = 20f;
        static readonly Vector3 RelicSize = new Vector3(0.3f, 0.3f, 0.08f);

#if KF_META_XR_CORE
        // GUIDs of Prefabs/OVRCameraRig.prefab and Prefabs/OVRHandPrefab.prefab in com.meta.xr.sdk.core.
        const string CameraRigPrefabGuid = "126d619cf4daa52469682f85c1378b4a";
        const string HandPrefabGuid = "835e735ca71bf78459fb2cababd74112";
#endif

        [MenuItem("Kinetic Forge/Create Sandbox Scene", false, 0)]
        static void CreateSandboxSceneMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Kinetic Forge", "Exit Play Mode before creating the sandbox scene.", "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Kinetic Forge", "Sandbox.unity already exists. Replace it?", "Replace", "Cancel"))
            {
                return;
            }

            GameObject systems = BuildSandbox();
            Selection.activeGameObject = systems;
        }

        [MenuItem("Kinetic Forge/Bake Forge Lighting", false, 1)]
        static void BakeLightingMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Kinetic Forge", "Exit Play Mode before baking.", "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            bool baked = BakeLighting();
            EditorUtility.DisplayDialog("Kinetic Forge", baked
                ? "Forge lighting baked: " + LightmapSettings.lightmaps.Length + " lightmap(s), light probes and the reflection probe."
                : "The lighting bake failed or was cancelled. See the Console.", "OK");
        }

        /// <summary>
        /// Opens the sandbox scene, bakes its lightmaps, light probes and reflection probe with the forge's lighting
        /// settings, and saves it. Takes a few minutes on the CPU lightmapper.
        /// </summary>
        /// <returns>True when the bake completed.</returns>
        internal static bool BakeLighting()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                // Lightmaps bake on the CPU, but reflection probes are rendered: without a GPU they come out empty and
                // every metal surface would reflect black.
                ForgeLog.Warn(ForgeLogChannel.Core, LogSource, "BAKE_WITHOUT_GRAPHICS",
                    "Baking without a graphics device (-nographics): lightmaps bake, but the reflection probe renders " +
                    "empty. Bake from the Editor, or run batch mode without -nographics.", null);
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ForgeEnvironmentBuilder.ApplyLightingSettings();
            bool baked = Lightmapping.Bake();
            EditorSceneManager.SaveScene(scene);
            if (baked)
            {
                ForgeLog.Info(ForgeLogChannel.Core, LogSource, "LIGHTING_BAKED",
                    "Baked " + LightmapSettings.lightmaps.Length + " lightmap(s) for " + ScenePath + ".", null);
            }
            else
            {
                ForgeLog.Error(ForgeLogChannel.Core, LogSource, "LIGHTING_BAKE_FAILED",
                    "The lighting bake for " + ScenePath + " failed or was cancelled.", null);
            }

            return baked;
        }

        /// <summary>True when the saved sandbox scene references baked lighting data.</summary>
        /// <param name="scenePath">Scene asset path.</param>
        internal static bool HasBakedLighting(string scenePath)
        {
            if (!File.Exists(scenePath))
            {
                return false;
            }

            // An unbaked scene stores "m_LightingDataAsset: {fileID: 0}"; a baked one references its LightingData asset.
            foreach (string line in File.ReadLines(scenePath))
            {
                int index = line.IndexOf("m_LightingDataAsset:", System.StringComparison.Ordinal);
                if (index >= 0)
                {
                    return line.IndexOf("guid:", index, System.StringComparison.Ordinal) >= 0;
                }
            }

            return false;
        }

        /// <summary>Builds and saves the sandbox scene without any dialogs. Used by the menu and by batch mode.</summary>
        /// <returns>The systems object of the new scene.</returns>
        internal static GameObject BuildSandbox()
        {
            UrpSetup.EnsureUrp();
            ForgeTypography.EnsureEssentials();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Camera defaultCamera = Camera.main;

            ForgeEnvironmentBuilder.Palette palette = ForgeEnvironmentBuilder.CreatePalette();
            ForgeEnvironmentBuilder.SetUpAtmosphere();
            Vector3 anvilBase = new Vector3(0f, 0f, AnvilDistance);
            GameObject forge = ForgeEnvironmentBuilder.BuildForge(palette, anvilBase, AnvilHeight);
            Transform water = ForgeEnvironmentBuilder.BuildTrough(palette, new Vector3(0.66f, 0f, 0.82f));

            Vector3 relicPosition = new Vector3(0f, AnvilHeight + RelicSize.y * 0.5f + 0.004f, AnvilDistance);
            GameObject relic = CreateRelic(relicPosition, palette, water);
            GameObject systems = CreateSystems(palette);
            CreateGuide(palette);
            CreateDiagnostics(palette);
            CreatePour(palette, systems, relic, relicPosition);
            CreateWall(palette, relic);
            CreateTuner(palette, relic);
            CreateMixedReality(systems, forge);

            bool metaRig = false;
#if KF_META_XR_CORE
            metaRig = TryAddMetaRig(defaultCamera);
            ConfigureProjectForTracking(false);
#endif

            if (!metaRig && defaultCamera != null)
            {
                Vector3 eye = new Vector3(0f, EditorEyeHeight, 0f);
                Vector3 lookTarget = relicPosition + Vector3.up * (HoldLift + 0.12f);
                defaultCamera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(lookTarget - eye));
                ForgeEnvironmentBuilder.ConfigureCamera(defaultCamera);
            }

            ForgeEnvironmentBuilder.ApplyLightingSettings();
            EnsureFolder(SceneFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);

            ForgeLog.Info(ForgeLogChannel.Core, LogSource, "SANDBOX_CREATED",
                "Saved " + ScenePath + (metaRig ? " with OVRCameraRig, body and hand tracking." : " with the default camera.") +
                " Press Play: hold Space to brace, right-drag to aim, left-click to strike in rhythm, N to nudge. Relic '" +
                relic.name + "'.", systems);
            return systems;
        }

        static GameObject CreateRelic(Vector3 position, ForgeEnvironmentBuilder.Palette palette, Transform water)
        {
            GameObject relic = new GameObject("Relic");
            relic.transform.position = position;
            relic.AddComponent<MeshFilter>().sharedMesh = GetOrCreateRelicMesh();
            MeshRenderer renderer = relic.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = palette.RelicMetal;
            BoxCollider box = relic.AddComponent<BoxCollider>();
            box.size = RelicSize;

            Rigidbody body = relic.AddComponent<Rigidbody>();
            body.mass = RelicMass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // A gentler wobble than the default: the light tablet is aimed at, so an unbraced stance should unsettle it,
            // not spin it.
            RelicStabilizer stabilizer = relic.AddComponent<RelicStabilizer>();
            ForgeEnvironmentBuilder.SetFloat(stabilizer, "_maxWobbleAcceleration", 4f);
            relic.AddComponent<RelicGrabDriver>();

            // The hold simulator doubles as the forge's levitating cradle: it lifts the relic off the anvil to working
            // height and lets it drift gently, on Quest and in the Editor alike.
            RelicHoldSimulator hold = relic.AddComponent<RelicHoldSimulator>();
            ForgeEnvironmentBuilder.SetVector3(hold, "_holdOffset", new Vector3(0f, HoldLift, 0f));
            ForgeEnvironmentBuilder.SetFloat(hold, "_carryAmplitude", 0.012f);
            ForgeEnvironmentBuilder.SetFloat(hold, "_carryFrequency", 0.12f);

            RelicCarvingSurface surface = relic.AddComponent<RelicCarvingSurface>();
            ForgeEnvironmentBuilder.SetObject(surface, "_markerMaterial", palette.RuneGlow);
            ForgeEnvironmentBuilder.SetBool(surface, "_hideStruckMarkers", true);
            ForgeEnvironmentBuilder.SetFloat(surface, "_markerSize", 0.01f);

            CarvingCursor cursor = relic.AddComponent<CarvingCursor>();
            ForgeEnvironmentBuilder.SetObject(cursor, "_cursorMaterial", palette.RuneGlow);

            // The right hand strikes and cuts; the left thumb swaps hammer and chisel (see RelicVoicing).
            ChiselStrikeController strike = relic.AddComponent<ChiselStrikeController>();
            SetSerializedInt(strike, "_strikingHand", (int)ForgeHand.Right);
            RelicResonance resonance = relic.AddComponent<RelicResonance>();
            relic.AddComponent<RelicVoicing>();
            relic.AddComponent<RelicForgeRenderer>();

            ChladniSand sand = relic.AddComponent<ChladniSand>();
            ForgeEnvironmentBuilder.SetObject(sand, "_material", palette.Sand);

            GroundingFlow grounding = relic.AddComponent<GroundingFlow>();
            ForgeEnvironmentBuilder.SetObject(grounding, "_streamMaterial", palette.Energy);
            ForgeEnvironmentBuilder.SetObject(grounding, "_rippleMaterial", palette.Ring);

            ResonanceRing ring = relic.AddComponent<ResonanceRing>();
            ForgeEnvironmentBuilder.SetObject(ring, "_material", palette.Ring);

            AudioSource audio = relic.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.rolloffMode = AudioRolloffMode.Logarithmic;
            audio.minDistance = 0.6f;
            audio.maxDistance = 20f;
            relic.AddComponent<RelicSynth>();

            RelicQuench quench = relic.AddComponent<RelicQuench>();
            ForgeEnvironmentBuilder.SetObject(quench, "_water", water);
            ForgeEnvironmentBuilder.SetObject(quench, "_steamMaterial", palette.Steam);

            // Under the relic: its heat lights the anvil and the player's hands without flooding the carved face.
            Light glow = ForgeEnvironmentBuilder.PointLight("Relic Glow", relic.transform,
                position + Vector3.down * 0.24f + Vector3.forward * 0.06f, new Color(1f, 0.55f, 0.2f), 0.55f, 0.9f);
            ForgeFlicker flicker = glow.gameObject.AddComponent<ForgeFlicker>();
            ForgeEnvironmentBuilder.SetFloat(flicker, "_intensity", 0.55f);
            ForgeEnvironmentBuilder.SetObject(flicker, "_heatSource", resonance);
            return relic;
        }

        static Mesh GetOrCreateRelicMesh()
        {
            // Saved so the Editor shows the tablet; at runtime the relic forge renderer builds its own carvable copy.
            Mesh mesh = RelicForgeMesh.Build(RelicSize, RelicForgeMesh.DefaultResolution);
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(ForgeEnvironmentBuilder.RelicMeshPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                return existing;
            }

            EnsureFolder(Path.GetDirectoryName(ForgeEnvironmentBuilder.RelicMeshPath)?.Replace('\\', '/'));
            AssetDatabase.CreateAsset(mesh, ForgeEnvironmentBuilder.RelicMeshPath);
            return mesh;
        }

        static void CreatePour(ForgeEnvironmentBuilder.Palette palette, GameObject systems, GameObject relic, Vector3 relicPosition)
        {
            // Hangs above and to the left of the cradled relic, so its tipped lip sits over the relic's center.
            Vector3 pivot = new Vector3(-0.12f, relicPosition.y + HoldLift + 0.3f, relicPosition.z);
            Transform tilt = ForgeEnvironmentBuilder.BuildCrucible(palette, pivot, out Transform lip, out Transform stream);
            MoltenPour pour = systems.AddComponent<MoltenPour>();
            ForgeEnvironmentBuilder.SetObject(pour, "_relic", relic.GetComponent<RelicForgeRenderer>());
            ForgeEnvironmentBuilder.SetObject(pour, "_crucible", tilt);
            ForgeEnvironmentBuilder.SetObject(pour, "_hoist", tilt.parent);
            ForgeEnvironmentBuilder.SetObject(pour, "_lip", lip);
            ForgeEnvironmentBuilder.SetObject(pour, "_stream", stream);
        }

        /// <summary>
        /// Adds the voice tuner low and to the left of the work, turned toward a seated eye: close enough to read at a
        /// glance, clear of the relic, the guide and the wall.
        /// </summary>
        static void CreateTuner(ForgeEnvironmentBuilder.Palette palette, GameObject relic)
        {
            GameObject tuner = new GameObject("Voice Tuner");
            Vector3 eye = new Vector3(0f, 1.25f, 0f);
            Vector3 position = new Vector3(-0.6f, 1.0f, 0.95f);
            tuner.transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - eye, Vector3.up));
            tuner.transform.localScale = Vector3.one * TunerScale;
            VoiceTuner voiceTuner = tuner.AddComponent<VoiceTuner>();
            ForgeEnvironmentBuilder.SetObject(voiceTuner, "_voicing", relic.GetComponent<RelicVoicing>());
            ForgeEnvironmentBuilder.SetObject(voiceTuner, "_panelMaterial", palette.Panel);
            ForgeEnvironmentBuilder.SetObject(voiceTuner, "_gaugeMaterial", palette.Gauge);
            ForgeEnvironmentBuilder.SetObject(voiceTuner, "_titleFont", ForgeTypography.DisplayFont());
            ForgeEnvironmentBuilder.SetObject(voiceTuner, "_textFont", ForgeTypography.TextFont());
        }

        static void CreateWall(ForgeEnvironmentBuilder.Palette palette, GameObject relic)
        {
            // Front left of the player: in view from the Editor camera, a short head turn on Quest.
            GameObject wall = ForgeEnvironmentBuilder.BuildWall(palette, new Vector3(-1.1f, 1.3f, 1.75f), new Vector3(0f, 1.3f, 0f));
            AudioSource audio = wall.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.minDistance = 0.8f;
            audio.maxDistance = 20f;
            ResonanceWall resonanceWall = wall.AddComponent<ResonanceWall>();
            ForgeEnvironmentBuilder.SetObject(resonanceWall, "_plaqueMaterial", palette.RelicMetal);
            ForgeEnvironmentBuilder.SetObject(resonanceWall, "_quench", relic.GetComponent<RelicQuench>());
        }

        static void CreateMixedReality(GameObject systems, GameObject forge)
        {
            MixedRealityMode mode = systems.AddComponent<MixedRealityMode>();
            SerializedObject serialized = new SerializedObject(mode);
            SerializedProperty hide = serialized.FindProperty("_hideInMixedReality");
            string[] parts = { "Floor", "Hall", "Furnace" };
            hide.arraySize = parts.Length;
            for (int i = 0; i < parts.Length; i++)
            {
                Transform part = forge.transform.Find(parts[i]);
                hide.GetArrayElementAtIndex(i).objectReferenceValue = part != null ? part.gameObject : null;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject CreateSystems(ForgeEnvironmentBuilder.Palette palette)
        {
            GameObject systems = new GameObject("Kinetic Forge Systems");
            MetaRuntimeGuard guard = systems.AddComponent<MetaRuntimeGuard>();
            ForgeEnvironmentBuilder.SetFloat(guard, "_fallbackEyeHeight", EditorEyeHeight);
            ForgeEnvironmentBuilder.SetFloat(guard, "_fallbackPitch", 18f);
            systems.AddComponent<ForgeGameManager>();
            systems.AddComponent<StanceTracker>();
            systems.AddComponent<XRFramePacing>();
            systems.AddComponent<XRSessionMonitor>();

            ForgeSparks sparks = systems.AddComponent<ForgeSparks>();
            ForgeEnvironmentBuilder.SetObject(sparks, "_sparkMaterial", palette.Spark);
            ForgeEnvironmentBuilder.SetObject(sparks, "_flashMaterial", palette.Ring);

            CreateHandSystems(systems.transform, ForgeHand.Left, mouseFallback: false);
            GameObject right = CreateHandSystems(systems.transform, ForgeHand.Right, mouseFallback: true);
            HandPointerAim aim = right.AddComponent<HandPointerAim>();
            SetSerializedInt(aim, "_hand", (int)ForgeHand.Right);
            return systems;
        }

        static GameObject CreateHandSystems(Transform parent, ForgeHand hand, bool mouseFallback)
        {
            GameObject handObject = new GameObject(hand + " Hand Input");
            handObject.transform.SetParent(parent, false);

            MicroGestureTracker tracker = handObject.AddComponent<MicroGestureTracker>();
            SetSerializedInt(tracker, "_hand", (int)hand);
            tracker.MouseFallbackEnabled = mouseFallback;

            HandGrabber grabber = handObject.AddComponent<HandGrabber>();
            SetSerializedInt(grabber, "_hand", (int)hand);
            return handObject;
        }

        /// <summary>
        /// Adds the test-build diagnostics panel to the player's right, angled toward a seated eye. It switches itself off
        /// in builds without diagnostics.
        /// </summary>
        static void CreateDiagnostics(ForgeEnvironmentBuilder.Palette palette)
        {
            Vector3 position = new Vector3(0.95f, 1.2f, 0.75f);
            Vector3 eye = new Vector3(0f, 1.25f, 0f);
            GameObject panel = new GameObject("Forge Diagnostics");
            panel.transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - eye, Vector3.up));
            TextMesh text = panel.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 48;
            text.characterSize = 0.022f;
            text.color = new Color(0.86f, 0.9f, 0.95f);
            text.anchor = TextAnchor.UpperLeft;
            text.alignment = TextAlignment.Left;
            text.richText = true;
            text.text = "DIAGNOSTICS";
            MeshRenderer renderer = panel.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = palette.Text;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            panel.AddComponent<WorldText>();
            ForgeDiagnostics diagnostics = panel.AddComponent<ForgeDiagnostics>();
            ForgeEnvironmentBuilder.SetObject(diagnostics, "_text", text);
        }

        static void CreateGuide(ForgeEnvironmentBuilder.Palette palette)
        {
            // Right of center and turned toward a seated eye: the crucible pours left of center, so its silhouette never
            // covers the text during the intro.
            GameObject guide = new GameObject("Forge Guide");
            Vector3 eye = new Vector3(0f, 1.25f, 0f);
            Vector3 position = new Vector3(0.34f, 1.5f, 1.2f);
            Vector3 facing = position - eye;
            facing.y = 0f;
            guide.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing, Vector3.up));
            TextMeshPro text = guide.AddComponent<TextMeshPro>();
            TMP_FontAsset font = ForgeTypography.TextFont();
            if (font != null)
            {
                text.font = font;
            }

            text.fontSize = ForgeGuide.FontSizeFor(text.font, GuideLineHeight);
            text.color = new Color(1f, 0.94f, 0.87f);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.richText = true;
            text.text = "KINETIC FORGE";
            text.rectTransform.sizeDelta = new Vector2(1.2f, 0.4f);
            MeshRenderer renderer = guide.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ForgeGuide forgeGuide = guide.AddComponent<ForgeGuide>();
            ForgeEnvironmentBuilder.SetFloat(forgeGuide, "_lineHeight", GuideLineHeight);
            ForgeEnvironmentBuilder.CreatePlate(guide.transform, text, palette.Panel);
        }

        static void EnsureFolder(string path)
        {
            ForgeEnvironmentBuilder.EnsureFolder(path);
        }

        static void AddSceneToBuildSettings(string path)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].path == path)
                {
                    return;
                }
            }

            List<EditorBuildSettingsScene> updated = new List<EditorBuildSettingsScene>(scenes)
            {
                new EditorBuildSettingsScene(path, true),
            };
            EditorBuildSettings.scenes = updated.ToArray();
        }

        static void SetSerializedInt(Object target, string propertyName, int value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                ForgeLog.Warn(ForgeLogChannel.Core, LogSource, "SERIALIZED_FIELD_MISSING",
                    target.GetType().Name + "." + propertyName + " was not found; set it in the Inspector.", target);
                return;
            }

            property.intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

#if KF_META_XR_CORE
        [MenuItem("Kinetic Forge/Configure Project for Body and Hand Tracking", false, 20)]
        static void ConfigureProjectForTrackingMenu()
        {
            ConfigureProjectForTracking(true);
        }

        static bool TryAddMetaRig(Camera defaultCamera)
        {
            GameObject rigPrefab = FindPrefab(CameraRigPrefabGuid, "OVRCameraRig", typeof(OVRCameraRig));
            if (rigPrefab == null)
            {
                ForgeLog.Warn(ForgeLogChannel.Core, LogSource, "CAMERA_RIG_PREFAB_MISSING",
                    "OVRCameraRig prefab not found in the Meta XR Core SDK. Keeping the default camera.", null);
                return false;
            }

            GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // The forge has no sky: every eye clears to the fog color.
            Camera[] eyes = rig.GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < eyes.Length; i++)
            {
                ForgeEnvironmentBuilder.ConfigureCamera(eyes[i]);
            }

            // Crouch is measured against real heights, so the origin must be the floor; the prefab ships at eye level.
            OVRManager manager = rig.GetComponentInChildren<OVRManager>(true);
            if (manager != null)
            {
                SetSerializedInt(manager, "_trackingOriginType", (int)OVRManager.TrackingOrigin.FloorLevel);

                // Mixed reality: passthrough starts with the app, its layer hidden until the player asks for their room.
                SerializedObject managerSettings = new SerializedObject(manager);
                SerializedProperty passthrough = managerSettings.FindProperty("isInsightPassthroughEnabled");
                if (passthrough != null)
                {
                    passthrough.boolValue = true;
                }

                // Body tracking is a runtime permission on Quest: without the startup request OVRBody never receives
                // data and stance falls back to the head-pose tier.
                SerializedProperty bodyPermission = managerSettings.FindProperty(QuestBuild.BodyPermissionProperty);
                if (bodyPermission != null)
                {
                    bodyPermission.boolValue = true;
                }

                managerSettings.ApplyModifiedPropertiesWithoutUndo();

                OVRPassthroughLayer layer = manager.gameObject.AddComponent<OVRPassthroughLayer>();
                layer.hidden = true;
                EditorUtility.SetDirty(layer);
            }

            // Body bones are written as tracking-space positions under the skeleton, so it lives at the tracking origin.
            Transform trackingSpace = FindChildRecursive(rig.transform, "TrackingSpace");
            GameObject bodyObject = new GameObject("Body Tracking");
            bodyObject.transform.SetParent(trackingSpace != null ? trackingSpace : rig.transform, false);

            OVRBody ovrBody = bodyObject.AddComponent<OVRBody>();
            ovrBody.ProvidedSkeletonType = OVRPlugin.BodyJointSet.FullBody;
            EditorUtility.SetDirty(ovrBody);

            OVRSkeleton skeleton = bodyObject.AddComponent<OVRSkeleton>();
            SetSerializedInt(skeleton, "_skeletonType", (int)OVRSkeleton.SkeletonType.FullBody);

            AddHand(rig.transform, "LeftHandAnchor", false);
            AddHand(rig.transform, "RightHandAnchor", true);

            if (defaultCamera != null)
            {
                Object.DestroyImmediate(defaultCamera.gameObject);
            }

            return true;
        }

        static void AddHand(Transform rig, string anchorName, bool right)
        {
            Transform anchor = FindChildRecursive(rig, anchorName);
            GameObject handPrefab = FindPrefab(HandPrefabGuid, "OVRHandPrefab", typeof(OVRHand));
            if (anchor == null || handPrefab == null)
            {
                ForgeLog.Warn(ForgeLogChannel.Core, LogSource, "HAND_SETUP_SKIPPED",
                    "Could not add the " + (right ? "right" : "left") + " tracked hand (anchor or OVRHandPrefab missing).",
                    null);
                return;
            }

            GameObject hand = (GameObject)PrefabUtility.InstantiatePrefab(handPrefab, anchor);
            hand.name = right ? "Right Hand" : "Left Hand";
            hand.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            // The prefab is set up as a left hand; the right hand flips hand, skeleton and mesh types together.
            OVRHand ovrHand = hand.GetComponentInChildren<OVRHand>(true);
            if (ovrHand != null)
            {
                SetSerializedInt(ovrHand, "HandType", (int)(right ? OVRHand.Hand.HandRight : OVRHand.Hand.HandLeft));
            }

            OVRSkeleton skeleton = hand.GetComponentInChildren<OVRSkeleton>(true);
            if (skeleton != null)
            {
                SetSerializedInt(skeleton, "_skeletonType",
                    (int)(right ? OVRSkeleton.SkeletonType.HandRight : OVRSkeleton.SkeletonType.HandLeft));
            }

            OVRMesh mesh = hand.GetComponentInChildren<OVRMesh>(true);
            if (mesh != null)
            {
                SetSerializedInt(mesh, "_meshType", (int)(right ? OVRMesh.MeshType.HandRight : OVRMesh.MeshType.HandLeft));
            }
        }

        static GameObject FindPrefab(string guid, string searchName, System.Type requiredComponent)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && prefab.GetComponentInChildren(requiredComponent, true) != null)
            {
                return prefab;
            }

            string[] guids = AssetDatabase.FindAssets(searchName + " t:Prefab");
            for (int i = 0; i < guids.Length; i++)
            {
                GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (candidate != null && candidate.GetComponentInChildren(requiredComponent, true) != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Enables what Kinetic Forge needs from Meta's project settings: body tracking (full body, high fidelity) and
        /// hand tracking alongside controllers. Leaves settings that are already compatible untouched.
        /// </summary>
        /// <param name="interactive">Show a summary dialog when done.</param>
        internal static void ConfigureProjectForTracking(bool interactive)
        {
            List<string> changes = new List<string>();

            OVRProjectConfig projectConfig = OVRProjectConfig.CachedProjectConfig;
            if (projectConfig != null)
            {
                bool dirty = false;
                if (projectConfig.bodyTrackingSupport == OVRProjectConfig.FeatureSupport.None)
                {
                    projectConfig.bodyTrackingSupport = OVRProjectConfig.FeatureSupport.Supported;
                    changes.Add("Body Tracking Support set to Supported");
                    dirty = true;
                }

                if (projectConfig.insightPassthroughSupport == OVRProjectConfig.FeatureSupport.None)
                {
                    projectConfig.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
                    changes.Add("Passthrough Support set to Supported");
                    dirty = true;
                }

                if (projectConfig.handTrackingSupport == OVRProjectConfig.HandTrackingSupport.ControllersOnly)
                {
                    projectConfig.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
                    changes.Add("Hand Tracking Support set to Controllers And Hands");
                    dirty = true;
                }

                if (dirty)
                {
                    OVRProjectConfig.CommitProjectConfig(projectConfig);
                }
            }

            OVRRuntimeSettings runtimeSettings = OVRRuntimeSettings.GetRuntimeSettings();
            if (runtimeSettings != null)
            {
                bool dirty = false;
                if (runtimeSettings.BodyTrackingJointSet != OVRPlugin.BodyJointSet.FullBody)
                {
                    runtimeSettings.BodyTrackingJointSet = OVRPlugin.BodyJointSet.FullBody;
                    changes.Add("Body Tracking Joint Set set to Full Body");
                    dirty = true;
                }

                if (runtimeSettings.BodyTrackingFidelity != OVRPlugin.BodyTrackingFidelity2.High)
                {
                    runtimeSettings.BodyTrackingFidelity = OVRPlugin.BodyTrackingFidelity2.High;
                    changes.Add("Body Tracking Fidelity set to High");
                    dirty = true;
                }

                if (dirty)
                {
                    EditorUtility.SetDirty(runtimeSettings);
                    AssetDatabase.SaveAssets();
                }
            }

            string summary = changes.Count == 0
                ? "Body and hand tracking were already configured."
                : string.Join(". ", changes) + ".";
            ForgeLog.Info(ForgeLogChannel.Core, LogSource, "TRACKING_CONFIGURED", summary, null);

            if (interactive)
            {
                EditorUtility.DisplayDialog("Kinetic Forge", summary, "OK");
            }
        }

        static Transform FindChildRecursive(Transform root, string childName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }

                Transform nested = FindChildRecursive(child, childName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }
#endif
    }
}
