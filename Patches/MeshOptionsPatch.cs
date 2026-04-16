using MeshSetExtender.Settings;
using Frosty.Core;
using Frosty.Core.Windows;
using HarmonyLib;
using MeshSetPlugin;
using System.Collections.Generic;

namespace MeshSetExtender.Patches
{
    [HarmonyPatch(typeof(OptionsWindow))]
    [HarmonyPatchCategory("autolod")]
    public class MeshOptionsPatch
    {
        private static readonly HashSet<OptionsWindow> PatchedInstances = new HashSet<OptionsWindow>();

        private static readonly AccessTools.FieldRef<OptionsWindow, List<OptionsExtension>> optionDataListRef =
            AccessTools.FieldRefAccess<OptionsWindow, List<OptionsExtension>>("optionDataList");

        /// <summary>
        /// Runs after OptionsWindow_Loaded to replace the MeshOptions instance
        /// with our ExtendedMeshOptions that includes Auto LOD settings.
        /// </summary>
        [HarmonyPatch("OptionsWindow_Loaded")]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        public static void InjectAutoLodOptions(OptionsWindow __instance)
        {
            if (!PatchedInstances.Add(__instance))
                return;

            List<OptionsExtension> optionDataList = optionDataListRef(__instance);

            for (int i = 0; i < optionDataList.Count; i++)
            {
                // Only replace if it's exactly MeshOptions, not already extended by another plugin
                if (optionDataList[i].GetType() == typeof(MeshOptions))
                {
                    var extended = new ExtendedMeshOptions();
                    extended.Load();
                    optionDataList[i] = extended;
                    break;
                }
            }

            // Remove the standalone AutoLodConfig entry (now merged into Mesh Options)
            optionDataList.RemoveAll(o => o is AutoLodConfig);
        }
    }
}
