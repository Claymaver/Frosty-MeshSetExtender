using System;
using Frosty.Core.Controls.Editors;
using FrostySdk.Attributes;
using MeshSetPlugin;

namespace MeshSetExtender.Settings
{
    public enum LodQualityPreset
    {
        Conservative,
        Balanced,
        Aggressive,
        Custom
    }

    [DisplayName("Import Mesh (Auto LOD)")]
    public class AutoLodImportSettings : FrostyMeshImportSettings
    {
        [Category("Quality")]
        [DisplayName("Preset")]
        [Description("Quality preset that sets LOD ratios automatically. Choose 'Custom' to set ratios manually.")]
        public LodQualityPreset Preset { get; set; } = LodQualityPreset.Balanced;

        [Category("Quality")]
        [DisplayName("Max Error")]
        [Description("Maximum allowed decimation error (0.01 = very strict, 0.1 = loose). Controls how much the mesh shape can deviate during simplification.")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0.001f, 1.0f, 0.005f, 0.01f, false)]
        // Smaller error preserves geometry better during decimation (tighter tolerance)
        public float MaxError { get; set; } = 0.01f;

        [Category("Quality")]
        [DisplayName("Lock Borders")]
        [Description("Prevent simplification from collapsing edges on mesh boundaries and UV seams. Helps avoid visible seam artifacts on lower LODs.")]
        public bool LockBorders { get; set; } = false;

        [Category("Custom Ratios")]
        [DisplayName("LOD1 Ratio")]
        [Description("Triangle ratio for LOD1 relative to LOD0. Only used when Preset is 'Custom'. (0.0 - 1.0)")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0.01f, 1.0f, 0.01f, 0.05f, false)]
        public float Lod1Ratio { get; set; } = 0.5f;

        [Category("Custom Ratios")]
        [DisplayName("LOD2 Ratio")]
        [Description("Triangle ratio for LOD2 relative to LOD0. Only used when Preset is 'Custom'. (0.0 - 1.0)")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0.01f, 1.0f, 0.01f, 0.05f, false)]
        public float Lod2Ratio { get; set; } = 0.25f;

        [Category("Custom Ratios")]
        [DisplayName("LOD3 Ratio")]
        [Description("Triangle ratio for LOD3 relative to LOD0. Only used when Preset is 'Custom'. (0.0 - 1.0)")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0.01f, 1.0f, 0.01f, 0.05f, false)]
        public float Lod3Ratio { get; set; } = 0.125f;

        [Category("Custom Ratios")]
        [DisplayName("LOD4 Ratio")]
        [Description("Triangle ratio for LOD4 relative to LOD0. Only used when Preset is 'Custom'. (0.0 - 1.0)")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0.01f, 1.0f, 0.01f, 0.05f, false)]
        public float Lod4Ratio { get; set; } = 0.0625f;

        [Category("Custom Ratios")]
        [DisplayName("LOD5 Ratio")]
        [Description("Triangle ratio for LOD5 relative to LOD0. Only used when Preset is 'Custom'. (0.0 - 1.0)")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0.01f, 1.0f, 0.01f, 0.05f, false)]
        public float Lod5Ratio { get; set; } = 0.03f;

        [Category("Diagnostics")]
        [DisplayName("Enable Debug Logging")]
        [Description("Outputs detailed diagnostic info to the log. Enable this when reporting issues.")]
        public bool DebugLogging { get; set; } = false;

        public float[] GetRatios()
        {
            switch (Preset)
            {
                case LodQualityPreset.Conservative:
                    return new[] { 0.7f, 0.5f, 0.35f, 0.2f, 0.1f };
                case LodQualityPreset.Balanced:
                    return new[] { 0.5f, 0.25f, 0.125f, 0.0625f, 0.03f };
                case LodQualityPreset.Aggressive:
                    return new[] { 0.35f, 0.15f, 0.06f, 0.03f, 0.015f };
                case LodQualityPreset.Custom:
                    return new[] { Lod1Ratio, Lod2Ratio, Lod3Ratio, Lod4Ratio, Lod5Ratio };
                default:
                    return new[] { 0.5f, 0.25f, 0.125f, 0.0625f, 0.03f };
            }
        }

        /// <summary>
        /// Returns a human-readable string of the preset and ratios being used.
        /// e.g. "Preset: Balanced [0.50, 0.25, 0.13, 0.06, 0.03]"
        /// </summary>
        public string GetRatiosSummary(int lodCount)
        {
            float[] ratios = GetRatios();
            int count = System.Math.Min(ratios.Length, lodCount - 1);
            string[] parts = new string[count];
            for (int i = 0; i < count; i++)
                parts[i] = ratios[i].ToString("F2");
            return $"Preset: {Preset} [{string.Join(", ", parts)}]";
        }

        /// <summary>
        /// Saves the current dialog settings back to the persistent AutoLodConfig.
        /// </summary>
        public void SaveAsDefaults()
        {
            var config = new AutoLodConfig();
            config.Preset = Preset;
            config.MaxError = MaxError;
            config.LockBorders = LockBorders;
            config.Lod1Ratio = Lod1Ratio;
            config.Lod2Ratio = Lod2Ratio;
            config.Lod3Ratio = Lod3Ratio;
            config.Lod4Ratio = Lod4Ratio;
            config.Lod5Ratio = Lod5Ratio;
            config.DebugLogging = DebugLogging;
            config.Save();
        }
    }
}
