// 7DTD AssetBundle builder.
// Put this file in your Unity project under:  Assets/Editor/BuildBundles.cs
// Then use the new top menu:  7DTD > Build Adamant Bundle
using UnityEditor;
using UnityEngine;
using System.IO;

public class BuildBundles
{
    [MenuItem("7DTD/Build Adamant Bundle")]
    static void Build()
    {
        const string outDir = "AssetBundles";
        if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

        // Windows client build target; 7DTD wants the .unity3d file.
        BuildPipeline.BuildAssetBundles(
            outDir,
            BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows64);

        Debug.Log("[7DTD] Bundles written to: " + Path.GetFullPath(outDir));
        EditorUtility.RevealInFinder(outDir);   // opens the folder in Explorer
    }
}
