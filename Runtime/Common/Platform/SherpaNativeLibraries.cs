using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace PonyuDev.SherpaOnnx.Common.Platform
{
    /// <summary>
    /// Makes Windows load the bundled onnxruntime.dll instead of an older copy
    /// in System32.
    /// <para/>
    /// Windows 11 ships an older onnxruntime.dll (Windows ML) in System32.
    /// Unity loads native plugins by a mixed-slash path, so Windows resolves
    /// sherpa-onnx-c-api's onnxruntime import from System32 instead of the
    /// plugin folder, and the API mismatch crashes natively on the first model.
    /// Loading the bundled copies by full path before anything else makes every
    /// later lookup reuse them. Runs automatically in the Windows Editor and
    /// Windows players; does nothing on other platforms.
    /// </summary>
    public static class SherpaNativeLibraries
    {
        private const string RuntimeFile = "onnxruntime.dll";
        private const string SherpaFile = "sherpa-onnx-c-api.dll";
        private const uint LoadWithAlteredSearchPath = 0x8;

        /// <summary>
        /// Why local speech cannot run, or null when the bundled native
        /// libraries are in use (or the platform needs no preload).
        /// </summary>
        public static string Problem { get; private set; }

        /// <summary>Throws <see cref="InvalidOperationException"/> when <see cref="Problem"/> is set.</summary>
        public static void ThrowIfUnavailable()
        {
            if (Problem != null)
                throw new InvalidOperationException(Problem);
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void PreloadForPlayer() =>
            Preload(Path.Combine(Application.dataPath, "Plugins", "x86_64"));
#endif

#if UNITY_EDITOR_WIN
        [UnityEditor.InitializeOnLoadMethod]
        private static void PreloadForEditor()
        {
            foreach (UnityEditor.PluginImporter importer in UnityEditor.PluginImporter.GetAllImporters())
            {
                if (!importer.isNativePlugin || !importer.GetCompatibleWithEditor())
                    continue;
                if (!string.Equals(Path.GetFileName(importer.assetPath), RuntimeFile, StringComparison.OrdinalIgnoreCase))
                    continue;

                Preload(Path.GetDirectoryName(Path.GetFullPath(
                    UnityEditor.FileUtil.GetPhysicalPath(importer.assetPath))));
                return;
            }
        }
#endif

        /// <summary>
        /// Loads onnxruntime.dll and sherpa-onnx-c-api.dll from
        /// <paramref name="folder"/> by full path. Sets <see cref="Problem"/>
        /// when another onnxruntime.dll is already loaded or a file is missing.
        /// </summary>
        public static void Preload(string folder)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // Backslashes matter: with forward slashes Windows ignores the DLL's own folder.
            string runtime = Path.GetFullPath(Path.Combine(folder, RuntimeFile)).Replace('/', '\\');
            string sherpa = Path.GetFullPath(Path.Combine(folder, SherpaFile)).Replace('/', '\\');
            if (!File.Exists(sherpa))
                return;

            string loaded = LoadedPath(RuntimeFile);
            if (loaded == null)
            {
                if (!File.Exists(runtime))
                {
                    Fail("Local speech is unavailable: " + RuntimeFile + " is missing from " + folder +
                         ". Reinstall the application or restore the file if security software removed it.");
                    return;
                }

                if (LoadLibraryExW(runtime, IntPtr.Zero, LoadWithAlteredSearchPath) == IntPtr.Zero)
                {
                    Fail("Local speech is unavailable: " + runtime + " could not be loaded (Windows error " +
                         Marshal.GetLastWin32Error() + ").");
                    return;
                }

                loaded = LoadedPath(RuntimeFile);
            }

            if (!SamePath(loaded, runtime))
            {
                Fail("Local speech is unavailable: another " + RuntimeFile + " was already loaded from " + loaded +
                     " (often injected by overlay or security software). Expected " + runtime + ".");
                return;
            }

            if (LoadLibraryExW(sherpa, IntPtr.Zero, LoadWithAlteredSearchPath) == IntPtr.Zero)
            {
                Fail("Local speech is unavailable: " + sherpa + " could not be loaded (Windows error " +
                     Marshal.GetLastWin32Error() + ").");
                return;
            }

            Problem = null;
#endif
        }

        private static void Fail(string problem)
        {
            Problem = problem;
            SherpaOnnxLog.RuntimeError("[SherpaOnnx] " + problem);
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private static string LoadedPath(string module)
        {
            IntPtr handle = GetModuleHandleW(module);
            if (handle == IntPtr.Zero)
                return null;

            var path = new StringBuilder(1024);
            int length = GetModuleFileNameW(handle, path, path.Capacity);
            return length > 0 ? path.ToString(0, length) : module;
        }

        private static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string module);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern int GetModuleFileNameW(IntPtr module, StringBuilder path, int size);
#endif
    }
}
