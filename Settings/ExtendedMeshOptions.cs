using Frosty.Core;
using Frosty.Core.Controls.Editors;
using FrostySdk.Attributes;
using MeshSetPlugin;

namespace MeshSetExtender.Settings
{
    /// <summary>
    /// Extends the built-in MeshOptions with Auto LOD default settings.
    /// Injected via Harmony patch so everything appears under one "Mesh Options" page.
    /// Uses the same Config keys as each plugin's standalone config, so settings stay in sync.
    /// </summary>
    [DisplayName("Mesh Options")]
    public class ExtendedMeshOptions : MeshOptions
    {
        // --- Auto LOD: Quality ---

        [Category("Auto LOD - Quality")]
        [DisplayName("Preset")]
        [Description("Default quality preset for LOD generation.")]
        public LodQualityPreset Preset { get; set; } = LodQualityPreset.Balanced;

        [Category("Auto LOD - Quality")]
        [DisplayName("Max Error")]
        [Description("Default maximum decimation error (0.01 = strict, 0.1 = loose).")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0.001f, 1.0f, 0.005f, 0.01f, false)]
        public float MaxError { get; set; } = 0.05f;

        [Category("Auto LOD - Quality")]
        [DisplayName("Lock Borders")]
        [Description("Default for preventing edge collapse on mesh boundaries and UV seams.")]
        [Editor(typeof(FrostyBooleanEditor))]
        public bool LockBorders { get; set; } = false;

        // --- Auto LOD: Custom Ratios ---

        [Category("Auto LOD - Custom Ratios")]
        [DisplayName("LOD1 Ratio")]
        [Description("Default triangle ratio for LOD1 relative to LOD0. (0.0 - 1.0)")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0.01f, 1.0f, 0.01f, 0.05f, false)]
        public float Lod1Ratio { get; set; } = 0.5f;

        [Category("Auto LOD - Custom Ratios")]
        [DisplayName("LOD2 Ratio")]
        [Description("Default triangle ratio for LOD2 relative to LOD0. (0.0 - 1.0)")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0.01f, 1.0f, 0.01f, 0.05f, false)]
        public float Lod2Ratio { get; set; } = 0.25f;

        [Category("Auto LOD - Custom Ratios")]
        [DisplayName("LOD3 Ratio")]
        [Description("Default triangle ratio for LOD3 relative to LOD0. (0.0 - 1.0)")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0.01f, 1.0f, 0.01f, 0.05f, false)]
        public float Lod3Ratio { get; set; } = 0.125f;

        [Category("Auto LOD - Custom Ratios")]
        [DisplayName("LOD4 Ratio")]
        [Description("Default triangle ratio for LOD4 relative to LOD0. (0.0 - 1.0)")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0.01f, 1.0f, 0.01f, 0.05f, false)]
        public float Lod4Ratio { get; set; } = 0.0625f;

        [Category("Auto LOD - Custom Ratios")]
        [DisplayName("LOD5 Ratio")]
        [Description("Default triangle ratio for LOD5 relative to LOD0. (0.0 - 1.0)")]
        [Editor(typeof(FrostySliderEditor))]
        [SliderMinMax(0.01f, 1.0f, 0.01f, 0.05f, false)]
        public float Lod5Ratio { get; set; } = 0.03f;

        // --- Auto LOD: Diagnostics ---

        [Category("Auto LOD - Diagnostics")]
        [DisplayName("Enable Debug Logging")]
        [Description("Default for verbose diagnostic logging.")]
        [Editor(typeof(FrostyBooleanEditor))]
        public bool DebugLogging { get; set; } = false;

        public override void Load()
        {
            base.Load();

            // Load Auto LOD settings (same config keys as AutoLodConfig)
            Preset = (LodQualityPreset)Config.Get("AutoLod.Preset", (int)LodQualityPreset.Balanced);
            MaxError = Config.Get("AutoLod.MaxError", 0.05f);
            LockBorders = Config.Get("AutoLod.LockBorders", false);
            Lod1Ratio = Config.Get("AutoLod.Lod1Ratio", 0.5f);
            Lod2Ratio = Config.Get("AutoLod.Lod2Ratio", 0.25f);
            Lod3Ratio = Config.Get("AutoLod.Lod3Ratio", 0.125f);
            Lod4Ratio = Config.Get("AutoLod.Lod4Ratio", 0.0625f);
            Lod5Ratio = Config.Get("AutoLod.Lod5Ratio", 0.03f);
            DebugLogging = Config.Get("AutoLod.DebugLogging", false);
        }

        public override void Save()
        {
            base.Save();

            // Save Auto LOD settings
            Config.Add("AutoLod.Preset", (int)Preset);
            Config.Add("AutoLod.MaxError", MaxError);
            Config.Add("AutoLod.LockBorders", LockBorders);
            Config.Add("AutoLod.Lod1Ratio", Lod1Ratio);
            Config.Add("AutoLod.Lod2Ratio", Lod2Ratio);
            Config.Add("AutoLod.Lod3Ratio", Lod3Ratio);
            Config.Add("AutoLod.Lod4Ratio", Lod4Ratio);
            Config.Add("AutoLod.Lod5Ratio", Lod5Ratio);
            Config.Add("AutoLod.DebugLogging", DebugLogging);
            Config.Save();
        }

        public override bool Validate()
        {
            bool baseValid = base.Validate();

            if (MaxError < 0.001f || MaxError > 1.0f)
            {
                MaxError = 0.05f;
                return false;
            }

            return baseValid;
        }
    }
}
