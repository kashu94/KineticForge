using System;
using System.Collections.Generic;
using System.IO;
using KineticForge.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KineticForge.EditorTools
{
    /// <summary>
    /// Builds the Quest 3 APK: applies the player settings Meta requires, checks the project and the build scenes for
    /// anything that would break on the headset or at upload, then builds and reports.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Settings applied: IL2CPP, ARM64 only, Vulkan only, Android API 32 or later, linear color, ASTC textures, an APK
    /// rather than an app bundle, multiview stereo, and Quest 2, Quest Pro, Quest 3 and Quest 3S as target devices. The
    /// Oculus XR plugin rewrites the manifest's supported-device list from those targets, so a Quest 3 build must list
    /// Quest 3 there.
    /// </para>
    /// <para>
    /// Release builds raise the version code by one, as Meta requires a higher code for every upload. They are signed
    /// with a release key when one is configured, through <c>UserSettings/KineticForgeSigning.json</c> (git-ignored) or
    /// the environment variables <c>KF_KEYSTORE</c>, <c>KF_KEYSTORE_PASS</c>, <c>KF_KEY_ALIAS</c> and
    /// <c>KF_KEY_PASS</c>. Signing settings are restored after the build, so no local path or password reaches
    /// ProjectSettings. Without a key the APK is debug-signed: fine for adb, rejected by Meta's dashboard.
    /// </para>
    /// </remarks>
    public static class QuestBuild
    {
        /// <summary>Android package name. Meta locks it to the app at the first upload.</summary>
        public const string PackageName = "com.kashifastudios.kineticforge";

        /// <summary>Company shown by the system.</summary>
        public const string CompanyName = "Kashifa Studios";

        /// <summary>Application name shown in the headset library.</summary>
        public const string ProductName = "Kinetic Forge";

        /// <summary>Version name prefix; the version code is appended.</summary>
        public const string VersionPrefix = "1.0.";

        /// <summary>Serialized OVRManager field that requests the body tracking permission at startup.</summary>
        internal const string BodyPermissionProperty = "requestBodyTrackingPermissionOnStartup";

        /// <summary>Scripting define that compiles the in-headset diagnostics panel into a release build.</summary>
        public const string DiagnosticsDefine = "KF_DIAGNOSTICS";

        /// <summary>Folder the APKs are written to, relative to the project. Git-ignored.</summary>
        public const string OutputFolder = "Builds";

        const string LogSource = nameof(QuestBuild);
        const string SigningFile = "UserSettings/KineticForgeSigning.json";
        const int MinimumApiLevel = 32;
        const int MultiviewMode = 2;

        static readonly string[] s_questTargets = { "TargetQuest2", "TargetQuestPro", "TargetQuest3", "TargetQuest3S" };

        [Serializable]
        sealed class SigningConfig
        {
            public string keystorePath = string.Empty;
            public string keystorePassword = string.Empty;
            public string keyAlias = string.Empty;
            public string keyPassword = string.Empty;

            public bool IsComplete =>
                !string.IsNullOrEmpty(keystorePath) && !string.IsNullOrEmpty(keystorePassword) &&
                !string.IsNullOrEmpty(keyAlias) && !string.IsNullOrEmpty(keyPassword);
        }

        [MenuItem("Kinetic Forge/Quest Build/Apply Quest Player Settings", false, 40)]
        static void ApplyMenu()
        {
            List<string> changes = new List<string>();
            ApplyPlayerSettings(changes);
            List<string> problems = new List<string>();
            CollectProblems(problems);
            string message = (changes.Count == 0 ? "Player settings were already set for Quest." : string.Join("\n", changes)) +
                             (problems.Count == 0 ? "\n\nPreflight passed." : "\n\nStill to fix:\n" + string.Join("\n", problems));
            EditorUtility.DisplayDialog("Kinetic Forge", message, "OK");
        }

        [MenuItem("Kinetic Forge/Quest Build/Run Preflight Check", false, 41)]
        static void PreflightMenu()
        {
            List<string> problems = new List<string>();
            CollectProblems(problems);
            EditorUtility.DisplayDialog("Kinetic Forge", problems.Count == 0
                ? "Preflight passed: the project is ready to build for Quest."
                : "Preflight found " + problems.Count + " problem(s):\n\n" + string.Join("\n", problems), "OK");
        }

        [MenuItem("Kinetic Forge/Quest Build/Build Release APK", false, 60)]
        static void BuildReleaseMenu()
        {
            BuildFromMenu(false, false);
        }

        [MenuItem("Kinetic Forge/Quest Build/Build Test APK (Diagnostics Panel)", false, 61)]
        static void BuildTestMenu()
        {
            BuildFromMenu(false, true);
        }

        [MenuItem("Kinetic Forge/Quest Build/Build Development APK", false, 62)]
        static void BuildDevelopmentMenu()
        {
            BuildFromMenu(true, false);
        }

        static void BuildFromMenu(bool development, bool diagnostics)
        {
            // Builds read scenes from disk, so unsaved edits would silently be left out.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            BuildReport report = Build(development, diagnostics, null);
            if (report != null && report.summary.result == BuildResult.Succeeded)
            {
                EditorUtility.RevealInFinder(report.summary.outputPath);
            }
        }

        /// <summary>Applies every Quest player setting. Idempotent.</summary>
        /// <param name="changes">Receives one line per setting that changed.</param>
        public static void ApplyPlayerSettings(List<string> changes)
        {
            // Read the package name before touching company or product: while no identifier has been set explicitly,
            // Unity derives one from them, and that derived name must still count as a placeholder.
            string identifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            bool placeholderIdentifier = IsPlaceholderIdentifier(identifier);

            if (PlayerSettings.companyName != CompanyName)
            {
                PlayerSettings.companyName = CompanyName;
                changes.Add("Company name set to " + CompanyName);
            }

            if (PlayerSettings.productName != ProductName)
            {
                PlayerSettings.productName = ProductName;
                changes.Add("Product name set to " + ProductName);
            }

            // Only replace Unity's placeholder: a package name chosen on purpose is never overwritten.
            if (placeholderIdentifier)
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageName);
                changes.Add("Package name set to " + PackageName);
            }

            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP)
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                changes.Add("Scripting backend set to IL2CPP");
            }

            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
            {
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                changes.Add("Target architecture set to ARM64 only");
            }

            GraphicsDeviceType[] apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            if (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android) || apis.Length != 1 ||
                apis[0] != GraphicsDeviceType.Vulkan)
            {
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
                changes.Add("Graphics API set to Vulkan only");
            }

            if ((int)PlayerSettings.Android.minSdkVersion < MinimumApiLevel)
            {
                PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)MinimumApiLevel;
                changes.Add("Minimum API level set to " + MinimumApiLevel);
            }

            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                PlayerSettings.colorSpace = ColorSpace.Linear;
                changes.Add("Color space set to Linear");
            }

            if (EditorUserBuildSettings.androidBuildSubtarget != MobileTextureSubtarget.ASTC)
            {
                EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
                changes.Add("Texture compression set to ASTC");
            }

            if (EditorUserBuildSettings.buildAppBundle)
            {
                EditorUserBuildSettings.buildAppBundle = false;
                changes.Add("Output set to APK (Meta does not accept app bundles)");
            }

            SerializedObject oculus = LoadOculusSettings();
            if (oculus != null)
            {
                bool dirty = false;
                for (int i = 0; i < s_questTargets.Length; i++)
                {
                    SerializedProperty target = oculus.FindProperty(s_questTargets[i]);
                    if (target != null && !target.boolValue)
                    {
                        target.boolValue = true;
                        changes.Add("Oculus target device enabled: " + s_questTargets[i]);
                        dirty = true;
                    }
                }

                SerializedProperty stereo = oculus.FindProperty("m_StereoRenderingModeAndroid");
                if (stereo != null && stereo.intValue != MultiviewMode)
                {
                    stereo.intValue = MultiviewMode;
                    changes.Add("Android stereo rendering set to Multiview");
                    dirty = true;
                }

                if (dirty)
                {
                    oculus.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(oculus.targetObject);
                }
            }

            if (changes.Count > 0)
            {
                AssetDatabase.SaveAssets();
                ForgeLog.Info(ForgeLogChannel.Core, LogSource, "QUEST_SETTINGS_APPLIED", string.Join(". ", changes) + ".", null);
            }
        }

        /// <summary>
        /// Lists everything that would make the APK fail on the headset or at upload: player settings, XR settings, Meta
        /// project features and the build scenes' rig.
        /// </summary>
        /// <param name="problems">Receives one line per problem. Empty when the project is ready.</param>
        public static void CollectProblems(List<string> problems)
        {
            if (IsPlaceholderIdentifier(PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)))
            {
                problems.Add("The package name is still Unity's placeholder.");
            }

            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP)
            {
                problems.Add("The scripting backend is not IL2CPP; Quest needs ARM64, which Mono cannot build.");
            }

            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
            {
                problems.Add("The target architecture is not ARM64 only.");
            }

            GraphicsDeviceType[] apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            if (apis.Length == 0 || apis[0] != GraphicsDeviceType.Vulkan)
            {
                problems.Add("Vulkan is not the first Android graphics API.");
            }

            if ((int)PlayerSettings.Android.minSdkVersion < MinimumApiLevel)
            {
                problems.Add("The minimum Android API level is below " + MinimumApiLevel + ".");
            }

            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                problems.Add("The color space is not Linear; the forge shaders are authored for linear lighting.");
            }

            if (ActiveInputHandling() == InputHandlingBoth)
            {
                problems.Add("Active Input Handling is \"Both\", which Unity does not support on Android. Set Project Settings > " +
                             "Player > Other Settings > Active Input Handling to \"Input System Package (New)\" and let the " +
                             "Editor restart. The game's Editor fallbacks use the Input System; Quest input comes from Meta's SDK.");
            }

            SerializedObject oculus = LoadOculusSettings();
            if (oculus == null && !UsesOpenXr())
            {
                problems.Add("No XR plugin is set up: neither the Oculus nor the OpenXR loader was found " +
                             "(Project Settings > XR Plug-in Management > Android).");
            }
            else if (oculus != null)
            {
                SerializedProperty quest3 = oculus.FindProperty("TargetQuest3");
                if (quest3 != null && !quest3.boolValue)
                {
                    problems.Add("Quest 3 is not an Oculus target device, so the manifest would not list Quest 3.");
                }

                SerializedProperty stereo = oculus.FindProperty("m_StereoRenderingModeAndroid");
                if (stereo != null && stereo.intValue != MultiviewMode)
                {
                    problems.Add("Android stereo rendering is not Multiview, which doubles the draw cost.");
                }
            }

            CollectMetaProblems(problems);
            CollectSceneProblems(problems);
        }

        /// <summary>
        /// Builds the APK. Applies settings, runs the preflight and refuses to build while problems remain.
        /// </summary>
        /// <param name="development">True for a development build: debuggable, profiler-ready, version code unchanged.</param>
        /// <param name="diagnostics">
        /// True for a test build: a release build (uploadable to a release channel) that also shows the in-headset
        /// diagnostics panel. Never use it for the competition upload.
        /// </param>
        /// <param name="outputPath">APK path, or null for <c>Builds/KineticForge-&lt;version&gt;.apk</c>.</param>
        /// <returns>The build report, or null when the build did not start.</returns>
        public static BuildReport Build(bool development, bool diagnostics, string outputPath)
        {
            if (BuildPipeline.isBuildingPlayer)
            {
                ForgeLog.Warn(ForgeLogChannel.Core, LogSource, "BUILD_BUSY", "A player build is already running.", null);
                return null;
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                ForgeLog.Error(ForgeLogChannel.Core, LogSource, "BUILD_TARGET_SWITCH_FAILED",
                    "Could not switch the active build target to Android. Is Android Build Support installed?", null);
                return null;
            }

            List<string> changes = new List<string>();
            ApplyPlayerSettings(changes);

            List<string> problems = new List<string>();
            CollectProblems(problems);
            if (problems.Count > 0)
            {
                ForgeLog.Error(ForgeLogChannel.Core, LogSource, "PREFLIGHT_FAILED",
                    "The Quest build was not started. Fix these first:\n- " + string.Join("\n- ", problems), null);
                return null;
            }

            string[] scenes = EnabledScenes();
            if (!development)
            {
                PlayerSettings.Android.bundleVersionCode = Mathf.Max(1, PlayerSettings.Android.bundleVersionCode + 1);
            }

            int versionCode = PlayerSettings.Android.bundleVersionCode;
            PlayerSettings.bundleVersion = VersionPrefix + versionCode;
            AssetDatabase.SaveAssets();

            if (string.IsNullOrEmpty(outputPath))
            {
                outputPath = Path.Combine(OutputFolder, development
                    ? "KineticForge-dev.apk"
                    : "KineticForge-" + PlayerSettings.bundleVersion + (diagnostics ? "-test" : string.Empty) + ".apk");
            }

            string folder = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development ? BuildOptions.Development : BuildOptions.None,
                extraScriptingDefines = diagnostics ? new[] { DiagnosticsDefine } : null,
            };

            // Release code generation for development builds too, so profiling on the headset shows real frame times.
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android, Il2CppCompilerConfiguration.Release);

            bool releaseSigned = ApplySigning(out bool hadCustomKeystore, out string previousKeystore, out string previousAlias);
            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                RestoreSigning(hadCustomKeystore, previousKeystore, previousAlias);
            }

            LogReport(report, development, diagnostics, releaseSigned, versionCode);
            return report;
        }

        static void LogReport(BuildReport report, bool development, bool diagnostics, bool releaseSigned, int versionCode)
        {
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                ForgeLog.Error(ForgeLogChannel.Core, LogSource, "BUILD_FAILED",
                    "Quest build " + summary.result + " after " + summary.totalTime.TotalSeconds.ToString("0") + " s with " +
                    summary.totalErrors + " error(s). See the Console or the log above for the first error.", null);
                return;
            }

            // The summary's total size counts uncompressed build output; the APK on disk is what gets uploaded.
            FileInfo apk = new FileInfo(summary.outputPath);
            string size = apk.Exists ? (apk.Length / (1024d * 1024d)).ToString("0.0") : "?";
            ForgeLog.Info(ForgeLogChannel.Core, LogSource, "BUILD_SUCCEEDED",
                (development ? "Development" : diagnostics ? "Test (release + diagnostics)" : "Release") + " APK built: " + summary.outputPath + " (" + size + " MB, version " +
                PlayerSettings.bundleVersion + ", code " + versionCode + ", " + summary.totalTime.TotalSeconds.ToString("0") +
                " s).", null);

            if (!development && !releaseSigned)
            {
                ForgeLog.Warn(ForgeLogChannel.Core, LogSource, "DEBUG_SIGNED",
                    "The release APK is signed with the debug key: install it with adb, but Meta's dashboard rejects it. " +
                    "Configure a release key in " + SigningFile + " (see README > Building for Quest).", null);
            }
        }

        static bool ApplySigning(out bool hadCustomKeystore, out string previousKeystore, out string previousAlias)
        {
            hadCustomKeystore = PlayerSettings.Android.useCustomKeystore;
            previousKeystore = PlayerSettings.Android.keystoreName;
            previousAlias = PlayerSettings.Android.keyaliasName;

            SigningConfig config = LoadSigning();
            if (!config.IsComplete)
            {
                PlayerSettings.Android.useCustomKeystore = false;
                return false;
            }

            if (!File.Exists(config.keystorePath))
            {
                ForgeLog.Warn(ForgeLogChannel.Core, LogSource, "KEYSTORE_MISSING",
                    "The configured keystore was not found at " + config.keystorePath + "; building debug-signed.", null);
                PlayerSettings.Android.useCustomKeystore = false;
                return false;
            }

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = config.keystorePath;
            PlayerSettings.Android.keystorePass = config.keystorePassword;
            PlayerSettings.Android.keyaliasName = config.keyAlias;
            PlayerSettings.Android.keyaliasPass = config.keyPassword;
            return true;
        }

        static void RestoreSigning(bool hadCustomKeystore, string previousKeystore, string previousAlias)
        {
            PlayerSettings.Android.useCustomKeystore = hadCustomKeystore;
            PlayerSettings.Android.keystoreName = previousKeystore;
            PlayerSettings.Android.keyaliasName = previousAlias;
            PlayerSettings.Android.keystorePass = string.Empty;
            PlayerSettings.Android.keyaliasPass = string.Empty;
        }

        static SigningConfig LoadSigning()
        {
            SigningConfig config = new SigningConfig();
            if (File.Exists(SigningFile))
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(SigningFile), config);
                }
                catch (Exception exception)
                {
                    ForgeLog.Warn(ForgeLogChannel.Core, LogSource, "SIGNING_FILE_INVALID",
                        SigningFile + " could not be read (" + exception.GetType().Name + "); building debug-signed.", null);
                    return new SigningConfig();
                }
            }

            config.keystorePath = Environment.GetEnvironmentVariable("KF_KEYSTORE") ?? config.keystorePath;
            config.keystorePassword = Environment.GetEnvironmentVariable("KF_KEYSTORE_PASS") ?? config.keystorePassword;
            config.keyAlias = Environment.GetEnvironmentVariable("KF_KEY_ALIAS") ?? config.keyAlias;
            config.keyPassword = Environment.GetEnvironmentVariable("KF_KEY_PASS") ?? config.keyPassword;
            if (config.keystorePath.StartsWith("~/", StringComparison.Ordinal))
            {
                config.keystorePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    config.keystorePath.Substring(2));
            }

            return config;
        }

        /// <summary>Player setting value for "Both" input systems.</summary>
        const int InputHandlingBoth = 2;

        /// <summary>Reads Active Input Handling (0 legacy, 1 Input System, 2 both); -1 when unreadable.</summary>
        static int ActiveInputHandling()
        {
            UnityEngine.Object[] settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (settings.Length == 0 || settings[0] == null)
            {
                return -1;
            }

            SerializedProperty property = new SerializedObject(settings[0]).FindProperty("activeInputHandler");
            return property != null ? property.intValue : -1;
        }

        static bool IsPlaceholderIdentifier(string identifier)
        {
            return string.IsNullOrEmpty(identifier) ||
                   string.Equals(identifier, DerivedIdentifier(), StringComparison.Ordinal) ||
                   identifier.StartsWith("com.DefaultCompany.", StringComparison.Ordinal) ||
                   identifier.StartsWith("com.Company.", StringComparison.Ordinal) ||
                   identifier.StartsWith("com.unity.template", StringComparison.Ordinal);
        }

        /// <summary>The identifier Unity derives from company and product while none is set explicitly.</summary>
        static string DerivedIdentifier()
        {
            return "com." + StripForIdentifier(PlayerSettings.companyName) + "." + StripForIdentifier(PlayerSettings.productName);
        }

        static string StripForIdentifier(string value)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// True when the project uses the Unity OpenXR plugin, found by its loader asset. The loader's type lives in a
        /// package this assembly does not reference, so it is looked up by name.
        /// </summary>
        static bool UsesOpenXr()
        {
            return AssetDatabase.FindAssets("t:OpenXRLoader").Length > 0;
        }

        static SerializedObject LoadOculusSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:OculusSettings");
            for (int i = 0; i < guids.Length; i++)
            {
                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (asset != null)
                {
                    return new SerializedObject(asset);
                }
            }

            return null;
        }

        static string[] EnabledScenes()
        {
            List<string> scenes = new List<string>();
            EditorBuildSettingsScene[] all = EditorBuildSettings.scenes;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].enabled && File.Exists(all[i].path))
                {
                    scenes.Add(all[i].path);
                }
            }

            return scenes.ToArray();
        }

        static void CollectSceneProblems(List<string> problems)
        {
            string[] scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                problems.Add("No enabled scene in Build Settings. Run Kinetic Forge > Create Sandbox Scene.");
                return;
            }

            if (!SandboxSceneBuilder.HasBakedLighting(scenes[0]))
            {
                problems.Add(scenes[0] + " has no baked lighting, so the hall would render dark and flat. Run Kinetic Forge > " +
                             "Bake Forge Lighting.");
            }

#if KF_META_XR_CORE
            // Only the first scene boots; it must carry a correctly configured rig.
            string path = scenes[0];
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                openedHere = true;
            }

            try
            {
                OVRManager manager = null;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length && manager == null; i++)
                {
                    manager = roots[i].GetComponentInChildren<OVRManager>(true);
                }

                if (manager == null)
                {
                    problems.Add(path + " has no OVRManager. Rebuild it with Kinetic Forge > Create Sandbox Scene.");
                    return;
                }

                SerializedObject settings = new SerializedObject(manager);
                SerializedProperty bodyPermission = settings.FindProperty(BodyPermissionProperty);
                if (bodyPermission != null && !bodyPermission.boolValue)
                {
                    problems.Add(path + ": OVRManager does not request the body tracking permission, so stance would run " +
                                 "on the head-pose fallback only. Rebuild the sandbox scene.");
                }

                SerializedProperty passthrough = settings.FindProperty("isInsightPassthroughEnabled");
                if (passthrough != null && !passthrough.boolValue)
                {
                    problems.Add(path + ": passthrough is not enabled on OVRManager, so mixed reality cannot start.");
                }

                SerializedProperty origin = settings.FindProperty("_trackingOriginType");
                if (origin != null && origin.intValue != (int)OVRManager.TrackingOrigin.FloorLevel)
                {
                    problems.Add(path + ": the tracking origin is not floor level; crouch depth and seated detection " +
                                 "would be measured from the wrong height.");
                }
            }
            finally
            {
                if (openedHere)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
#endif
        }

        static void CollectMetaProblems(List<string> problems)
        {
#if KF_META_XR_CORE
            OVRProjectConfig config = OVRProjectConfig.CachedProjectConfig;
            if (config == null)
            {
                problems.Add("Meta project config was not found.");
                return;
            }

            if (config.handTrackingSupport == OVRProjectConfig.HandTrackingSupport.ControllersOnly)
            {
                problems.Add("Hand tracking is off in the Meta project config; the competition requires hands-only play.");
            }

            if (config.bodyTrackingSupport == OVRProjectConfig.FeatureSupport.None)
            {
                problems.Add("Body tracking is off in the Meta project config.");
            }

            if (config.insightPassthroughSupport == OVRProjectConfig.FeatureSupport.None)
            {
                problems.Add("Passthrough is off in the Meta project config, so mixed reality cannot start.");
            }

            // The Meta SDK writes the manifest's supported-device list from these targets, for either XR plugin.
            if (config.targetDeviceTypes == null || !config.targetDeviceTypes.Contains(OVRProjectConfig.DeviceType.Quest3))
            {
                problems.Add("Quest 3 is not a target device in the Meta project config, so the manifest would not list it.");
            }
#endif
        }
    }
}
