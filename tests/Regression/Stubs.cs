using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        public static void DontDestroyOnLoad(Object value) { }
        public static void Destroy(Object value)
        {
            if (value is GameObject go)
                GameObject.All.Remove(go);
        }
    }

    public class MonoBehaviour : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject.transform;
    }

    public class Transform
    {
        public Transform Parent { get; private set; }
        public void SetParent(Transform parent) => Parent = parent;
    }

    public class GameObject : Object
    {
        public static readonly List<GameObject> All = new();
        public readonly List<MonoBehaviour> Components = new();
        public readonly Transform transform = new();
        public readonly string Name;
        public bool activeSelf = true;
        private bool awakened;

        public GameObject(string name)
        {
            Name = name;
            All.Add(this);
        }

        public static GameObject Find(string name) => All.FirstOrDefault(go => go.Name == name);
        public MonoBehaviour AddComponent(Type type)
        {
            var component = (MonoBehaviour)Activator.CreateInstance(type);
            component.gameObject = this;
            Components.Add(component);
            if (activeSelf) Awake();
            return component;
        }

        public void SetActive(bool active)
        {
            activeSelf = active;
            if (active) Awake();
        }

        private void Awake()
        {
            if (awakened) return;
            awakened = true;
            foreach (var component in Components)
                component.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.Invoke(component, null);
        }
    }

    public static class Application
    {
        public static void Quit(int code) => throw new InvalidOperationException("Tests must not quit or install updates.");
    }
}

namespace UnityEngine.UI { public class Button { } }
namespace HarmonyLib
{
    public class Harmony
    {
        public Harmony(string id) { }
        public void PatchAll() { }
        public void PatchAll(Type type) { }
    }
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string name) { } }
    public class HarmonyPostfix : Attribute { }
}
namespace Silk.API { public class Weapons { } }
namespace Silk
{
    public static class Logger
    {
        public static readonly List<string> Messages = new();
        public static void Log(string text) => Messages.Add(text);
        public static void LogInfo(string text) => Log(text);
        public static void LogWarning(string text) => Log(text);
        public static void LogError(string text) => Log(text);
    }
    public static class Main { public static string GetSilkVersion() => "0.7.0"; }
    public static class VersionHandler { public static bool IsOlderThan(string version) => false; }
    public static class ModsUI
    {
        public static int Calls;
        public static void Initialize() => Calls++;
    }
}
public class CustomTiersScreen { }
public class SteamLeaderboards { }
public class Announcer
{
    public static Announcer instance = new();
    public void Announce(string text, UnityEngine.Color color, bool force) { }
    public static void ShowAnnounce(string text, float duration, UnityEngine.Color color) { }
    public static void InformationPopup(string text) { }
    public static void TwoOptionsPopup(string text, string yes, string no, Action accept, Action reject, object extra) { }
}
namespace UnityEngine
{
    public struct Color
    {
        public Color(float red, float green, float blue, float alpha = 1) { }
    }
}
