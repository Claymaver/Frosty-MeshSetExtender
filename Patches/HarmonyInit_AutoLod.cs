using Frosty.Core;
using FrostySdk.Interfaces;
using HarmonyLib;
using System;

namespace MeshSetExtender.Patches
{
    public class AutoLodHarmonyInit : StartupAction
    {
        public override Action<ILogger> Action => logger =>
        {
            if (App.PluginManager.ManagerType == PluginManagerType.Editor)
            {
                logger.Log("[Auto LOD] Applying editor patches...");
                var harmony = new Harmony("com.claymaver.autolod");
                harmony.PatchCategory("autolod");
            }
        };
    }
}
