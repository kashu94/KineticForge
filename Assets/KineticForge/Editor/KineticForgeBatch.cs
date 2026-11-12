using System;
#if KF_META_XR_CORE
using System.Threading.Tasks;
#endif
using KineticForge.Core;
using UnityEditor;
using UnityEngine;

namespace KineticForge.EditorTools
{
    /// <summary>
    /// Command-line entry points, so the project can be set up and checked without opening the Editor window.
    /// </summary>
    /// <remarks>
    /// Run from a terminal, with no Editor open on the project:
    /// <code>
    /// Unity -batchmode -nographics -projectPath &lt;project&gt; -buildTarget Android \
    ///   -executeMethod KineticForge.EditorTools.KineticForgeBatch.SetUpQuestSandbox -logFile setup.log
    /// </code>
    /// The method builds the sandbox scene, enables body and hand tracking, applies Meta's Project Setup Tool fixes for
    /// Android over several passes, saves, and exits with code 0 on success or 1 on failure.
    /// </remarks>
    public static class KineticForgeBatch
    {
        const string LogSource = nameof(KineticForgeBatch);

#if KF_META_XR_CORE
        const int FixPasses = 3;
        const double TimeoutSeconds = 900d;

        static Task s_fixTask;
        static int s_pass;
        static double s_deadline;
#endif

        /// <summary>
        /// Compiles every pass of the Kinetic Forge shaders for Quest (Vulkan, Android), with and without the main lighting
        /// and fog keywords, reports any error or warning, then exits Unity with code 0 when all compile.
        /// </summary>
        /// <remarks>
        /// Checks the compiler directly rather than the shader's active subshader: outside rendering, batch mode reports
        /// URP shaders, Lit included, as running their fallback.
        /// </remarks>
        public static void ValidateShaders()
        {
            string[] names = { "Kinetic Forge/Forge Metal", "Kinetic Forge/Forge Glow", "Kinetic Forge/Forge Text" };
            string[][] keywordSets =
            {
                Array.Empty<string>(),
                new[] { "_MAIN_LIGHT_SHADOWS_CASCADE", "_ADDITIONAL_LIGHTS", "_SHADOWS_SOFT", "FOG_EXP2", "INSTANCING_ON" },
                new[] { "_ADDITIONAL_LIGHTS_VERTEX", "FOG_LINEAR", "STEREO_INSTANCING_ON", "INSTANCING_ON" },
            };

            int problems = 0;
            int variants = 0;
            for (int i = 0; i < names.Length; i++)
            {
                Shader shader = Shader.Find(names[i]);
                if (shader == null)
                {
                    ForgeLog.Error(ForgeLogChannel.Core, LogSource, "SHADER_MISSING", names[i] + " was not found.", null);
                    problems++;
                    continue;
                }

                ShaderData.Subshader subshader = ShaderUtil.GetShaderData(shader).GetSubshader(0);
                string passes = string.Empty;
                for (int p = 0; p < subshader.PassCount; p++)
                {
                    ShaderData.Pass pass = subshader.GetPass(p);
                    passes += (p > 0 ? ", " : string.Empty) + pass.Name;
                    for (int k = 0; k < keywordSets.Length; k++)
                    {
                        for (int stage = 0; stage < 2; stage++)
                        {
                            UnityEditor.Rendering.ShaderType type = stage == 0
                                ? UnityEditor.Rendering.ShaderType.Vertex
                                : UnityEditor.Rendering.ShaderType.Fragment;
                            ShaderData.VariantCompileInfo info = pass.CompileVariant(type, keywordSets[k],
                                UnityEditor.Rendering.ShaderCompilerPlatform.Vulkan, BuildTarget.Android, true);
                            variants++;
                            if (!info.Success)
                            {
                                problems++;
                            }

                            for (int m = 0; m < info.Messages.Length; m++)
                            {
                                ShaderMessage message = info.Messages[m];
                                string text = names[i] + " / " + pass.Name + " / " + type + ": " + message.message + " " +
                                              message.messageDetails + " (" + message.file + ":" + message.line + ")";
                                if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                                {
                                    ForgeLog.Error(ForgeLogChannel.Core, LogSource, "SHADER_ERROR", text, shader);
                                }
                                else
                                {
                                    ForgeLog.Warn(ForgeLogChannel.Core, LogSource, "SHADER_WARNING", text, shader);
                                }
                            }
                        }
                    }
                }

                ForgeLog.Info(ForgeLogChannel.Core, LogSource, "SHADER_CHECKED", names[i] + ": passes [" + passes + "].", shader);
            }

            Finish(problems == 0 ? 0 : 1, problems == 0
                ? "All shaders compiled for Quest (" + variants + " variants)."
                : problems + " shader variants failed to compile.");
        }

        /// <summary>Rebuilds only the sandbox scene, then exits Unity. Quicker than a full setup after code changes.</summary>
        public static void RebuildSandboxScene()
        {
            try
            {
                SandboxSceneBuilder.BuildSandbox();
                AssetDatabase.SaveAssets();
                Finish(0, "Sandbox scene rebuilt.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Finish(1, "Sandbox rebuild failed with " + exception.GetType().Name + ".");
            }
        }

        /// <summary>Builds the sandbox scene and applies Quest project settings, then exits Unity.</summary>
        public static void SetUpQuestSandbox()
        {
            try
            {
                // Building the sandbox also enables body and hand tracking when the Meta XR Core SDK is installed.
                SandboxSceneBuilder.BuildSandbox();
                AssetDatabase.SaveAssets();

#if KF_META_XR_CORE
                s_pass = 0;
                s_deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
                StartFixPass();
                EditorApplication.update += PollFixes;
#else
                Finish(0, "Sandbox created. The Meta XR Core SDK is not installed, so Meta project fixes were skipped.");
#endif
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Finish(1, "Setup failed with " + exception.GetType().Name + ".");
            }
        }

#if KF_META_XR_CORE
        static void StartFixPass()
        {
            s_pass++;
            ForgeLog.Info(ForgeLogChannel.Core, LogSource, "FIX_PASS", "Applying Meta project setup fixes for Android, pass " +
                                                                       s_pass + " of " + FixPasses + ".", null);
            s_fixTask = OVRProjectSetup.FixAllAsync(BuildTargetGroup.Android);
        }

        static void PollFixes()
        {
            if (EditorApplication.timeSinceStartup > s_deadline)
            {
                Finish(1, "Meta project setup fixes did not finish within " + TimeoutSeconds + " s.");
                return;
            }

            if (s_fixTask == null || !s_fixTask.IsCompleted)
            {
                return;
            }

            if (s_fixTask.IsFaulted)
            {
                Debug.LogException(s_fixTask.Exception);
                Finish(1, "Meta project setup fixes failed.");
                return;
            }

            // Some fixes unlock others, so a few passes reach a stable configuration.
            if (s_pass < FixPasses)
            {
                StartFixPass();
                return;
            }

            AssetDatabase.SaveAssets();
            Finish(0, "Sandbox created; body and hand tracking enabled; Meta project fixes applied for Android in " +
                      s_pass + " passes.");
        }
#endif

        static void Finish(int exitCode, string message)
        {
#if KF_META_XR_CORE
            EditorApplication.update -= PollFixes;
#endif
            if (exitCode == 0)
            {
                ForgeLog.Info(ForgeLogChannel.Core, LogSource, "SETUP_COMPLETE", message, null);
            }
            else
            {
                ForgeLog.Error(ForgeLogChannel.Core, LogSource, "SETUP_FAILED", message, null);
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }
    }
}
