using Frosty.Core;
using FrostySdk.Interfaces;
using HarmonyLib;
using System;

namespace MeshSetExtender.Patches
{
    public class MeshSetExporterHarmonyInit : StartupAction
    {
        public override Action<ILogger> Action => logger =>
        {
            if (App.PluginManager.ManagerType == PluginManagerType.Editor)
            {
                logger.Log("[MeshSet Exporter] Applying editor patches...");
                var harmony = new Harmony("com.claymaver.meshsetexporter");
                harmony.PatchCategory("meshsetexporter");
            }
        };
    }
}
