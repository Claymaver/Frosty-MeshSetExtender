namespace MeshSetExtender.Helpers
{
    /// <summary>
    /// Centralizes cloth-asset naming-suffix checks.
    /// EACloth ("_eacloth") is the old naming; "_cloth" is the newer convention
    /// used by later SWBF2 assets. ClothWrappingAsset is always per-mesh.
    /// </summary>
    internal static class ClothAssetNaming
    {
        // Order matters: test "_eacloth" first (more specific, avoids false double-strip).
        public static bool IsClothAsset(string resNameLower)
            => resNameLower.EndsWith("_eacloth") || resNameLower.EndsWith("_cloth");

        public static bool IsClothWrappingAsset(string resNameLower)
            => resNameLower.EndsWith("_clothwrappingasset");

        /// <summary>
        /// Strips trailing "_eacloth" or "_cloth" (whichever matches) from a name.
        /// Returns the name unchanged if neither suffix applies.
        /// </summary>
        public static string StripClothSuffix(string resNameLower)
        {
            if (resNameLower.EndsWith("_eacloth"))
                return resNameLower.Substring(0, resNameLower.Length - "_eacloth".Length);
            if (resNameLower.EndsWith("_cloth"))
                return resNameLower.Substring(0, resNameLower.Length - "_cloth".Length);
            return resNameLower;
        }
    }
}
