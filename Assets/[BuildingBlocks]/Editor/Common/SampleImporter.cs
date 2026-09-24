using System.IO;
using UnityEditor;
using UnityEngine;

namespace Blocks
{
    /// <summary>
    /// Imports the template's optional samples and answers whether one is already in the project.
    ///
    /// A sample ships as a plain folder under <see cref="SourceRoot"/>, a tilde folder Unity never imports, so
    /// its scripts do not compile and its files do not appear in the Project window until someone asks for
    /// it. Importing copies the folder, <c>.meta</c> files included, to <see cref="ImportRoot"/> and refreshes;
    /// the GUIDs travel with it, so importing twice lands on the same assets. Deleting the imported folder
    /// removes the sample again.
    ///
    /// They are folders rather than <c>.unitypackage</c> files because Unity 6 checks a package's signature
    /// on import and puts a "Missing Signature" dialog in front of any package not exported by an editor
    /// signed in to Unity Cloud — which a template built in CI cannot be. A folder copy has no such gate.
    ///
    /// Lives outside <c>Editor/Readme/</c> so that "Remove Readme Assets" leaves the menu item behind as a
    /// way to still import a sample.
    /// </summary>
    static class SampleImporter
    {
        /// <summary>Where the shipped samples are, one folder per sample, named after the sample.</summary>
        public const string SourceRoot = "Assets/[BuildingBlocks]/Samples~";

        /// <summary>Where a sample's files land: <c>ImportRoot/&lt;sample name&gt;/</c>.</summary>
        public const string ImportRoot = "Assets/Samples";

        public static string SourceFolder(string sampleName) => $"{SourceRoot}/{sampleName}";

        public static string ImportFolder(string sampleName) => $"{ImportRoot}/{sampleName}";

        /// <summary>Whether the sample is shipped in this project, so there is something to import.</summary>
        public static bool IsAvailable(string sampleName) =>
            !string.IsNullOrEmpty(sampleName) && Directory.Exists(SourceFolder(sampleName));

        /// <summary>Whether the sample's files are already in the project.</summary>
        public static bool IsImported(string sampleName) =>
            !string.IsNullOrEmpty(sampleName) && AssetDatabase.IsValidFolder(ImportFolder(sampleName));

        /// <summary>Imports the named sample. Logs, rather than throws, when it is missing or already in.</summary>
        public static void Import(string sampleName)
        {
            if (IsImported(sampleName))
            {
                Debug.Log($"[BuildingBlocks] Sample \"{sampleName}\" is already in {ImportFolder(sampleName)}.");
                return;
            }

            string source = SourceFolder(sampleName);
            if (!Directory.Exists(source))
            {
                Debug.LogWarning($"[BuildingBlocks] Sample \"{sampleName}\" is not in this project: {source} is missing.");
                return;
            }

            string destination = ImportFolder(sampleName);
            Directory.CreateDirectory(ImportRoot);
            FileUtil.CopyFileOrDirectory(source, destination);

            // The folder's own .meta sits next to it, so the imported folder keeps its GUID too.
            if (File.Exists(source + ".meta")) File.Copy(source + ".meta", destination + ".meta", true);

            AssetDatabase.Refresh();
            Debug.Log($"[BuildingBlocks] Imported sample \"{sampleName}\" into {destination}.");
        }

        /// <summary>
        /// The fallback for a project whose Readme has been removed: pick a sample folder to import.
        /// </summary>
        [MenuItem("Building Blocks/Import Sample...", false, 100)]
        static void ImportFromMenu()
        {
            string picked = EditorUtility.OpenFolderPanel("Import a Building Blocks sample",
                Path.GetFullPath(SourceRoot), "");
            if (string.IsNullOrEmpty(picked)) return;

            var folder = new DirectoryInfo(picked);
            bool isSample = folder.Parent != null &&
                            Path.GetFullPath(folder.Parent.FullName) == Path.GetFullPath(SourceRoot);
            if (!isSample)
            {
                Debug.LogWarning($"[BuildingBlocks] Pick one of the sample folders directly under {SourceRoot}.");
                return;
            }

            Import(folder.Name);
        }
    }
}
