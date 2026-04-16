using System;
using System.IO;
using System.Reflection;

namespace MeshSetExtender.Decimation
{
    internal static class NativeLibraryLoader
    {
        private static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded)
                return;

            string dllName = "meshoptimizer.dll";

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dllPath = Path.Combine(baseDir, dllName);

            if (!File.Exists(dllPath))
            {
                var assembly = Assembly.GetExecutingAssembly();
                string resourceName = FindResourceName(assembly, dllName);

                using (Stream resource = assembly.GetManifestResourceStream(resourceName))
                using (FileStream file = new FileStream(dllPath, FileMode.Create, FileAccess.Write))
                {
                    resource.CopyTo(file);
                }
            }

            _loaded = true;
        }

        private static string FindResourceName(Assembly assembly, string fileName)
        {
            foreach (string name in assembly.GetManifestResourceNames())
            {
                if (name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                    return name;
            }

            throw new Exception("Embedded meshoptimizer.dll not found.");
        }
    }
}
