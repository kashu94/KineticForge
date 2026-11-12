using System.IO;
using KineticForge.Core;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace KineticForge.EditorTools
{
    /// <summary>
    /// Sets up TextMesh Pro for the forge's world-space text: imports TMP's essential resources when they are missing,
    /// and builds signed-distance-field font assets for Cinzel (display) and Inter (text), both SIL Open Font License.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Font files live in <c>Assets/KineticForge/Fonts</c> with their licenses. Font assets are written to
    /// <c>Assets/KineticForge/Resources/Fonts &amp; Materials</c>, the folder TMP searches for <c>&lt;font="..."&gt;</c>
    /// tags, so a single text can mix the display and text faces.
    /// </para>
    /// <para>
    /// Each asset is filled with every character the game prints, then made static: no glyph is rasterized on the
    /// headset, so text never hitches the frame and never shows a missing glyph.
    /// </para>
    /// </remarks>
    internal static class ForgeTypography
    {
        internal const string FontFolder = "Assets/KineticForge/Fonts";
        internal const string FontAssetFolder = "Assets/KineticForge/Resources/Fonts & Materials";

        /// <summary>Name of the display face's font asset, for <c>&lt;font&gt;</c> tags.</summary>
        internal const string DisplayFontName = "Cinzel SDF";

        /// <summary>Name of the text face's font asset.</summary>
        internal const string TextFontName = "Inter SDF";

        const string EssentialsPackage = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";
        const string DistanceFieldShader = "TextMeshPro/Distance Field";
        const int SamplingPointSize = 90;
        const int AtlasPadding = 9;
        const int AtlasSize = 1024;

        /// <summary>Every character the game prints: printable ASCII plus typographic punctuation and symbols.</summary>
        static string Charset
        {
            get
            {
                System.Text.StringBuilder builder = new System.Text.StringBuilder(128);
                for (char c = ' '; c <= '~'; c++)
                {
                    builder.Append(c);
                }

                builder.Append("–—·…•‘’“”×°→←↑↓♪");
                return builder.ToString();
            }
        }

        /// <summary>
        /// Checks that TMP's essential resources (settings and shaders) are in the project, and queues their import when
        /// they are missing.
        /// </summary>
        /// <returns>True when TMP's distance field shader and settings are available now.</returns>
        internal static bool EnsureEssentials()
        {
            if (Shader.Find(DistanceFieldShader) != null && AssetDatabase.FindAssets("t:TMP_Settings").Length > 0)
            {
                return true;
            }

            string package = Path.GetFullPath(EssentialsPackage);
            if (!File.Exists(package))
            {
                ForgeLog.Warn(ForgeLogChannel.Core, nameof(ForgeTypography), "TMP_ESSENTIALS_MISSING",
                    "TMP Essential Resources were not found at " + EssentialsPackage + "; world text keeps its fallback font.", null);
                return false;
            }

            // The project ships TMP's essential resources in Assets/TextMesh Pro. If they were deleted, queue the import;
            // it completes after this call returns, so text uses TMP's fallback until the sandbox is rebuilt.
            AssetDatabase.ImportPackage(package, false);
            ForgeLog.Warn(ForgeLogChannel.Core, nameof(ForgeTypography), "TMP_ESSENTIALS_QUEUED",
                "TMP Essential Resources were missing and are being imported. Run Kinetic Forge > Create Sandbox Scene again " +
                "once the import finishes, so the forge's fonts are built.", null);
            return false;
        }

        /// <summary>The display face (Cinzel), for titles and grades.</summary>
        internal static TMP_FontAsset DisplayFont() => GetOrCreate("Cinzel.ttf", DisplayFontName);

        /// <summary>The text face (Inter), for instructions and readouts.</summary>
        internal static TMP_FontAsset TextFont() => GetOrCreate("Inter.ttf", TextFontName);

        static TMP_FontAsset GetOrCreate(string fontFile, string name)
        {
            string assetPath = FontAssetFolder + "/" + name + ".asset";
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null)
            {
                return existing;
            }

            Font font = AssetDatabase.LoadAssetAtPath<Font>(FontFolder + "/" + fontFile);
            if (font == null || !EnsureEssentials())
            {
                return null;
            }

            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font, SamplingPointSize, AtlasPadding, GlyphRenderMode.SDFAA,
                AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, true);
            if (asset == null)
            {
                ForgeLog.Warn(ForgeLogChannel.Core, nameof(ForgeTypography), "FONT_ASSET_FAILED",
                    "Could not build a font asset from " + fontFile + "; world text keeps its fallback font.", null);
                return null;
            }

            asset.name = name;
            if (!asset.TryAddCharacters(Charset, out string missing) && !string.IsNullOrEmpty(missing))
            {
                ForgeLog.Info(ForgeLogChannel.Core, nameof(ForgeTypography), "FONT_GLYPHS_MISSING",
                    fontFile + " has no glyph for: " + missing, null);
            }

            // Every character is baked now; nothing is rasterized at runtime.
            asset.atlasPopulationMode = AtlasPopulationMode.Static;

            ForgeEnvironmentBuilder.EnsureFolder(FontAssetFolder);
            AssetDatabase.CreateAsset(asset, assetPath);
            asset.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            Texture2D[] atlases = asset.atlasTextures;
            for (int i = 0; i < atlases.Length; i++)
            {
                if (atlases[i] != null)
                {
                    atlases[i].name = name + " Atlas " + i;
                    AssetDatabase.AddObjectToAsset(atlases[i], asset);
                }
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            ForgeLog.Info(ForgeLogChannel.Core, nameof(ForgeTypography), "FONT_ASSET_CREATED",
                "Created " + assetPath + " with " + asset.characterTable.Count + " characters.", asset);
            return asset;
        }
    }
}
