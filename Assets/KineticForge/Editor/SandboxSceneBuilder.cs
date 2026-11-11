using System.Collections.Generic;
using System.IO;
using KineticForge.Core;
using KineticForge.Crafting;
using KineticForge.Physics;
using KineticForge.Platform;
using KineticForge.Tracking;
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
        const string MaterialFolder = "Assets/KineticForge/Materials";
        const float AnvilHeight = 0.9f;
        const float AnvilDistance = 0.55f;
        const float RelicSize = 0.28f;
        const float RelicMass = 20f;

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

        /// <summary>Builds and saves the sandbox scene without any dialogs. Used by the menu and by batch mode.</summary>
        /// <returns>The systems object of the new scene.</returns>
        internal static GameObject BuildSandbox()
        {
            UrpSetup.EnsureUrp();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Camera defaultCamera = Camera.main;
            SetUpLighting();

            Material floorMaterial = GetOrCreateLitMaterial("Forge Floor", new Color(0.16f, 0.15f, 0.14f), 0f, 0.2f);
            Material anvilMaterial = GetOrCreateLitMaterial("Forge Anvil", new Color(0.22f, 0.22f, 0.24f), 0.85f, 0.45f);
            Material relicMaterial = GetOrCreateLitMaterial("Relic Bronze", new Color(0.80f, 0.50f, 0.22f), 1f, 0.62f);
            Material glowMaterial = GetOrCreateUnlitMaterial("Rune Glow", new Color(1f, 0.62f, 0.2f));

            CreatePrimitive(PrimitiveType.Plane, "Forge Floor", Vector3.zero, Vector3.one, floorMaterial);
            CreatePrimitive(PrimitiveType.Cube, "Anvil",
                new Vector3(0f, AnvilHeight * 0.5f, AnvilDistance), new Vector3(0.45f, AnvilHeight, 0.3f), anvilMaterial);

            Vector3 relicPosition = new Vector3(0f, AnvilHeight + RelicSize * 0.5f + 0.002f, AnvilDistance);
            GameObject relic = CreateRelic(relicPosition, relicMaterial, glowMaterial);
            GameObject systems = CreateSystems();

            bool metaRig = false;
#if KF_META_XR_CORE
            metaRig = TryAddMetaRig(defaultCamera);
            ConfigureProjectForTracking(false);
#endif

            if (!metaRig && defaultCamera != null)
            {
                Vector3 eye = new Vector3(0f, 1.6f, -0.35f);
                Vector3 lookTarget = relicPosition + Vector3.up * 0.2f;
                defaultCamera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(lookTarget - eye));
                defaultCamera.nearClipPlane = 0.05f;
            }

            EnsureFolder(SceneFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);

            ForgeLog.Info(ForgeLogChannel.Core, LogSource, "SANDBOX_CREATED",
                "Saved " + ScenePath + (metaRig ? " with OVRCameraRig, body and hand tracking." : " with the default camera.") +
                " Press Play: hold Space to brace, right-drag to aim, left-click to strike, N to nudge. Relic '" +
                relic.name + "'.", systems);
            return systems;
        }

        static GameObject CreateRelic(Vector3 position, Material relicMaterial, Material glowMaterial)
        {
            GameObject relic = CreatePrimitive(PrimitiveType.Cube, "Relic", position, Vector3.one * RelicSize, relicMaterial);
            Rigidbody body = relic.AddComponent<Rigidbody>();
            body.mass = RelicMass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            relic.AddComponent<RelicStabilizer>();
            relic.AddComponent<RelicGrabDriver>();
            relic.AddComponent<RelicHoldSimulator>();

            RelicCarvingSurface surface = relic.AddComponent<RelicCarvingSurface>();
            CarvingCursor cursor = relic.AddComponent<CarvingCursor>();
            relic.AddComponent<ChiselStrikeController>();

            if (glowMaterial != null)
            {
                SetSerializedObject(surface, "_markerMaterial", glowMaterial);
                SetSerializedObject(cursor, "_cursorMaterial", glowMaterial);
            }

            return relic;
        }

        static GameObject CreateSystems()
        {
            GameObject systems = new GameObject("Kinetic Forge Systems");
            systems.AddComponent<MetaRuntimeGuard>();
            systems.AddComponent<ForgeGameManager>();
            systems.AddComponent<StanceTracker>();
            systems.AddComponent<XRFramePacing>();
            systems.AddComponent<XRSessionMonitor>();

            CreateHandSystems(systems.transform, ForgeHand.Left, mouseFallback: false);
            CreateHandSystems(systems.transform, ForgeHand.Right, mouseFallback: true);
            return systems;
        }

        static void CreateHandSystems(Transform parent, ForgeHand hand, bool mouseFallback)
        {
            GameObject handObject = new GameObject(hand + " Hand Input");
            handObject.transform.SetParent(parent, false);

            MicroGestureTracker tracker = handObject.AddComponent<MicroGestureTracker>();
            SetSerializedInt(tracker, "_hand", (int)hand);
            tracker.MouseFallbackEnabled = mouseFallback;

            HandGrabber grabber = handObject.AddComponent<HandGrabber>();
            SetSerializedInt(grabber, "_hand", (int)hand);
        }

        static void SetUpLighting()
        {
            Light sun = Object.FindAnyObjectByType<Light>();
            if (sun == null)
            {
                return;
            }

            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            sun.color = new Color(1f, 0.93f, 0.84f);
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
        }

        static GameObject CreatePrimitive(PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            primitive.transform.SetPositionAndRotation(position, Quaternion.identity);
            primitive.transform.localScale = scale;

            if (material != null && primitive.TryGetComponent(out Renderer renderer))
            {
                renderer.sharedMaterial = material;
            }

            return primitive;
        }

        static Material GetOrCreateLitMaterial(string name, Color color, float metallic, float smoothness)
        {
            return GetOrCreateMaterial(name, "Universal Render Pipeline/Lit", color, metallic, smoothness);
        }

        static Material GetOrCreateUnlitMaterial(string name, Color color)
        {
            return GetOrCreateMaterial(name, "Universal Render Pipeline/Unlit", color, -1f, -1f);
        }

        static Material GetOrCreateMaterial(string name, string shaderName, Color color, float metallic, float smoothness)
        {
            // Existing materials are reused untouched, so hand-tuned looks survive a rebuild of the scene.
            string path = MaterialFolder + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                return null;
            }

            EnsureFolder(MaterialFolder);
            Material material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            if (metallic >= 0f && material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }

            if (smoothness >= 0f && material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void EnsureFolder(string path)
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

        static void SetSerializedObject(Object target, string propertyName, Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                ForgeLog.Warn(ForgeLogChannel.Core, LogSource, "SERIALIZED_FIELD_MISSING",
                    target.GetType().Name + "." + propertyName + " was not found; set it in the Inspector.", target);
                return;
            }

            property.objectReferenceValue = value;
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

            // Crouch is measured against real heights, so the origin must be the floor; the prefab ships at eye level.
            OVRManager manager = rig.GetComponentInChildren<OVRManager>(true);
            if (manager != null)
            {
                SetSerializedInt(manager, "_trackingOriginType", (int)OVRManager.TrackingOrigin.FloorLevel);
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
