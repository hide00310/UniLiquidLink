using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public static void DestroyImmediate(Object obj) { }
        public override string ToString() { return string.Format("{0} ({1})", name, GetType().FullName); }
    }
    public class Component : Object
    {
    }
    public class GameObject : Object
    {
        public GameObject() { transform = new Transform { gameObject = this }; }
        public GameObject(string name) : this() { this.name = name; }
        public Transform transform { get; private set; }
        public bool activeSelf { get; set; }
        public HideFlags hideFlags { get; set; }
        public string tag { get; set; } = "Untagged";
        public static GameObject Find(string name) { return null; }
        public static GameObject CreatePrimitive(PrimitiveType type) { return new GameObject(); }
        public Component GetComponent(Type type) { return null; }
        public bool CompareTag(string tag) { return this.tag == tag; }
    }
    public class Transform : Component
    {
        public GameObject gameObject { get; set; }
        public Vector3 position { get; set; }
        public void Rotate(float x, float y, float z, Space relativeTo) { }
    }
    public struct Vector3
    {
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public enum Space { World, Self }
    public enum PrimitiveType { Cube }
    public enum HideFlags { None }
    public class Material : Object
    {
        public Material(Shader shader) { }
    }
    public class Shader
    {
        public static Shader Find(string name) { return null; }
    }
    public static class Application
    {
        public static string dataPath { get; private set; } = System.IO.Directory.GetCurrentDirectory();
    }
    public static class GUILayout
    {
        public static bool Toggle(bool value, string text, string style) { return value; }
    }
    public class JsonUtility
    {
        public static object FromJson(string json, Type type) => JsonSerializer.Deserialize(json, type);
        public static string ToJson(object obj) => JsonSerializer.Serialize(obj, obj.GetType());
    }
    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogError(object message) { }
        public static void LogErrorFormat(string format, params object[] args) { }
        public static void LogException(Exception exception) { }
    }

    namespace Internal
    {
        public class DefaultValueAttribute : Attribute
        {
        }
    }
}

namespace UnityEditor
{
    public static class EditorApplication
    {
        public static event Action update;
        public static void Exit(int returnValue) { }
    }
    public class EditorWindow
    {
        public static T GetWindow<T>(string title) where T : EditorWindow, new() { return new T(); }
        public void Repaint() { }
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class MenuItemAttribute : Attribute
    {
        public MenuItemAttribute(string itemName) { }
    }
    [AttributeUsage(AttributeTargets.Class)]
    public class InitializeOnLoadAttribute : Attribute
    {
    }
    public static class EditorPrefs
    {
        public static string GetString(string key, string defaultValue) { return defaultValue; }
        public static void SetString(string key, string value) { }
    }
    public static class SessionState
    {
        public static void SetBool(string key, bool value) { }
        public static bool GetBool(string key, bool defaultValue) { return defaultValue; }
    }
    public static class AssemblyReloadEvents
    {
        public static event Action beforeAssemblyReload;
        public static event Action afterAssemblyReload;
    }
    public static class EditorGUILayout
    {
        public static void LabelField(string label) { }
        public static string TextField(string text) { return text; }
    }
    public static class AssetDatabase
    {
        public static UnityEngine.Object LoadAssetAtPath(string assetPath, Type type) { return null; }
        public static void Refresh() { }
        public static void CreateAsset(UnityEngine.Object asset, string path) { }
        public static void SaveAssets() { }
        public static void DeleteAsset(string path) { }
    }
}
