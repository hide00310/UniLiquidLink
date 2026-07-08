using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Sets up and tears down the Unity environment for integration tests.
/// Menu: UniLiquidLink > Setup Integration Test
///       UniLiquidLink > Teardown Integration Test
/// </summary>
public static class SetupIntegrationTest
{
    public const string TestObjectName = "UniLiquidLinkTestObject";
    public const string TestAssetPath = "Assets/UniLiquidLinkTest.mat";
    public static string[] TestObjectNames = new string[]
    {
        TestObjectName,
        "UniLiquidLinkTestObject2",
    };
    public static string[] TestAssetPaths = new string[]
    {
        TestAssetPath,
        "Assets/UniLiquidLinkTest2.mat"
    };
    [MenuItem("UniLiquidLink/Tests/Setup Integration Test")]
    public static void Setup()
    {
        // Create test GameObject (destroy and recreate if it already exists)
        foreach (var item in TestObjectNames)
        {
            var existing = GameObject.Find(item);
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = item;
        }

        // Create test material asset (reuse if it already exists)
        foreach (var item in TestAssetPaths)
        {
            string absPath = Path.Combine(Application.dataPath, "../" + item);
            if (!File.Exists(absPath))
            {
                var mat = new Material(Shader.Find("Standard"))
                {
                    name = "UniLiquidLinkTest"
                };
                AssetDatabase.CreateAsset(mat, item);
                AssetDatabase.SaveAssets();
            }
        }

        Debug.Log("[SetupIntegrationTest] Setup complete.");
    }

    [MenuItem("UniLiquidLink/Tests/Teardown Integration Test")]
    public static void Teardown()
    {
        foreach (var item in TestObjectNames)
        {
            var go = GameObject.Find(item);
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }
        foreach (var item in TestAssetPaths)
        {
            AssetDatabase.DeleteAsset(item);
            AssetDatabase.Refresh();
        }

        Debug.Log("[SetupIntegrationTest] Teardown complete.");
    }
}
