#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Modern8bitTanks.Editor
{
    /// <summary>
    /// Automatically configures:
    ///   - *_slices.png & DesertGround.png -> Point filter, Uncompressed, Read/Write enabled
    ///   - Resources/Fonts/*.ttf -> HintedRaster (1-bit binary crisp retro pixel font rasterization, zero AA blur)
    /// </summary>
    public class SpriteStackAssetPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            bool isSlice = assetPath.EndsWith("_slices.png");
            bool isGround = assetPath.EndsWith("DesertGround.png");
            if (!isSlice && !isGround)
                return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 16f;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.isReadable = true;
            importer.alphaIsTransparency = isSlice;
            importer.npotScale = TextureImporterNPOTScale.None;
        }

        private void OnPreprocessAsset()
        {
            if (!assetPath.Contains("Modern8bitTanks/Resources/Fonts/") || !assetPath.EndsWith(".ttf"))
                return;

            if (assetImporter is TrueTypeFontImporter fontImporter)
            {
                fontImporter.fontSize = 8;
                fontImporter.fontRenderingMode = FontRenderingMode.HintedRaster;
                fontImporter.fontTextureCase = FontTextureCase.Dynamic;
            }
        }
    }
}
#endif
