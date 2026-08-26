using System.IO;
using UnityEngine;

namespace Garment.EditorTools
{
    /// <summary>
    /// Where the self-tests drop their renders: a folder beside Assets, never inside it. These
    /// are throwaway artefacts — kept in Assets each one earns a .meta, an asset import on every
    /// run and a line in .gitignore, none of which a debug picture deserves.
    /// </summary>
    public static class DiagnosticsOutput
    {
        public static string PathFor(string fileName)
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Diagnostics"));
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, fileName);
        }
    }
}
