using KineticForge.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KineticForge.EditorTools
{
    /// <summary>
    /// Makes URP the active render pipeline with Quest-friendly settings, so URP materials render instead of showing
    /// magenta.
    /// </summary>
    /// <remarks>
    /// Creates <c>Assets/KineticForge/Settings/Quest URP.asset</c> and its renderer when missing, then assigns it as the
    /// default pipeline and to every quality level. Settings follow Meta's guidance for Quest 3: 4x MSAA, no HDR and no
    /// post-processing, which keep the GPU inside a 90 Hz budget. An existing pipeline asset is reused untouched.
    /// </remarks>
    internal static class UrpSetup
    {
        const string LogSource = nameof(UrpSetup);
        const string Folder = "Assets/KineticForge/Settings";
        const string AssetPath = Folder + "/Quest URP.asset";
        const string RendererPath = Folder + "/Quest URP Renderer.asset";

        [MenuItem("Kinetic Forge/Set Up URP for Quest", false, 10)]
        static void SetUpMenu()
        {
            EnsureUrp();
        }

        /// <summary>Ensures a URP asset exists and is active everywhere.</summary>
        /// <returns>True when anything changed.</returns>
        internal static bool EnsureUrp()
        {
            UniversalRenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
            if (pipeline == null)
            {
                pipeline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            }

            bool changed = false;
            if (pipeline == null)
            {
                if (!AssetDatabase.IsValidFolder(Folder))
                {
                    AssetDatabase.CreateFolder("Assets/KineticForge", "Settings");
                }

                UniversalRendererData renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);

                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.msaaSampleCount = 4;
                pipeline.supportsHDR = false;
                pipeline.renderScale = 1f;
                pipeline.shadowDistance = 8f;
                AssetDatabase.CreateAsset(pipeline, AssetPath);
                changed = true;
            }

            if (GraphicsSettings.defaultRenderPipeline != pipeline)
            {
                GraphicsSettings.defaultRenderPipeline = pipeline;
                changed = true;
            }

            int current = QualitySettings.GetQualityLevel();
            for (int level = 0; level < QualitySettings.names.Length; level++)
            {
                QualitySettings.SetQualityLevel(level, false);
                if (QualitySettings.renderPipeline != pipeline)
                {
                    QualitySettings.renderPipeline = pipeline;
                    changed = true;
                }
            }

            QualitySettings.SetQualityLevel(current, false);

            if (changed)
            {
                AssetDatabase.SaveAssets();
                ForgeLog.Info(ForgeLogChannel.Core, LogSource, "URP_ACTIVE",
                    "URP is now the active render pipeline for every quality level (" + AssetPath + ").", pipeline);
            }

            return changed;
        }
    }
}
