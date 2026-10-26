using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Mono.Cecil;
using Silk;
using UnityEngine;

internal static class Program
{
    private static int failures;
    private static int total;
    private static readonly string RunId = Guid.NewGuid().ToString("N");
    private static string Id(string name) => RunId + "-" + name;
    private static Dictionary<string, object> Defaults() => new()
    {
        ["surge"] = new Dictionary<string, object> { ["enabled"] = true, ["strength"] = 1.5f },
        ["stats"] = new Dictionary<string, object> { ["enabled"] = true }
    };

    public static async Task<int> Main()
    {
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        Directory.CreateDirectory(Config.ConfigPath);
        Run("new mod configs do not mutate supplied defaults", () =>
        {
            var defaults = Defaults();
            Config.LoadModConfig(Id("new"), defaults);
            Config.SetModConfigValue(Id("new"), "surge.enabled", false);
            Check((bool)((Dictionary<string, object>)defaults["surge"])["enabled"], "default was mutated");
        });
        Run("missing sections remain independent after merging", () =>
        {
            var id = Id("merge");
            WriteMod(id, "surge:\n  strength: 2.5\ncustom: keep\n");
            var defaults = Defaults();
            Config.LoadModConfig(id, defaults);
            Config.SetModConfigValue(id, "stats.enabled", false);
            Check((bool)((Dictionary<string, object>)defaults["stats"])["enabled"], "missing section aliased defaults");
            Check(Config.GetModConfigValue<float>(id, "surge.strength") == 2.5f, "override lost");
            Check(Config.GetModConfigValue<string>(id, "custom") == "keep", "unknown setting lost");
        });
        Run("two mods cannot change each other's shared defaults", () =>
        {
            var defaults = Defaults();
            Config.LoadModConfig(Id("one"), defaults);
            Config.LoadModConfig(Id("two"), defaults);
            Config.SetModConfigValue(Id("one"), "surge.enabled", false);
            Check(Config.GetModConfigValue<bool>(Id("two"), "surge.enabled"), "other mod changed");
        });
        Run("nested YAML lists do not alias defaults", () =>
        {
            var entry = new Dictionary<string, object> { ["enabled"] = true };
            var defaults = new Dictionary<string, object> { ["items"] = new List<object> { entry } };
            var id = Id("list");
            Config.LoadModConfig(id, defaults);
            var items = Config.GetModConfigValue<List<object>>(id, "items");
            ((Dictionary<string, object>)items[0])["enabled"] = false;
            Check((bool)entry["enabled"], "list item aliased defaults");
        });
        Run("global defaults stay independent", () =>
        {
            File.Delete(Config.ConfigFile);
            var defaults = Defaults();
            Config.LoadConfig(defaults);
            Config.SetConfigValue("surge.enabled", false);
            Check((bool)((Dictionary<string, object>)defaults["surge"])["enabled"], "global defaults mutated");
        });
        foreach (var badYaml in new[] { "surge: [broken", "plain scalar", "- unexpected\n- list" })
            Run("invalid mod YAML is backed up: " + badYaml, () =>
            {
                var id = Id(Guid.NewGuid().ToString("N"));
                WriteMod(id, badYaml);
                Config.LoadModConfig(id, Defaults());
                Check(Config.GetModConfigValue<bool>(id, "surge.enabled"), "defaults unavailable");
                Config.SetModConfigValue(id, "surge.enabled", false);
                var backups = Directory.GetFiles(Path.GetDirectoryName(Config.GetModConfigPath(id)), id + ".yaml.invalid-*.bak");
                Check(backups.Length == 1 && File.ReadAllText(backups[0]) == badYaml, "original YAML lost");
            });
        Run("malformed global config no longer prevents loader startup", () =>
        {
            const string malformed = "loader: [unfinished";
            File.WriteAllText(Config.ConfigFile, malformed);
            Config.LoadConfig(Defaults());
            Check(Config.GetConfigValue<bool>("surge.enabled"), "no global defaults");
            Check(Directory.GetFiles(Config.ConfigPath, "silk.yaml.invalid-*.bak")
                .Any(path => File.ReadAllText(path) == malformed), "no global backup");
        });
        Run("saved numbers use YAML culture under French locale", () =>
        {
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var id = Id("culture");
                WriteMod(id, "surge:\n  strength: 1.5\n");
                Config.LoadModConfig(id, Defaults());
                Check(Config.GetModConfigValue<float>(id, "surge.strength") == 1.5f, "decimal value lost");
            }
            finally { CultureInfo.CurrentCulture = culture; }
        });
        Run("SpiderSurge section batching still persists all changed values", () =>
        {
            var id = Id("batch");
            Config.LoadModConfig(id, Defaults());
            var section = Config.GetModConfigValue<Dictionary<string, object>>(id, "surge");
            section["enabled"] = false;
            Config.SetModConfigValue(id, "surge.strength", 3.5f);
            Config.LoadModConfig(id, Defaults());
            Check(!Config.GetModConfigValue<bool>(id, "surge.enabled"), "batched value lost");
            Check(Config.GetModConfigValue<float>(id, "surge.strength") == 3.5f, "last value lost");
        });
        Run("failed replacement preserves existing config and cleans temporary file", () =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var id = Id("locked");
            Config.LoadModConfig(id, Defaults());
            var path = Config.GetModConfigPath(id);
            var original = File.ReadAllText(path);
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var failed = false;
                try { Config.SetModConfigValue(id, "surge.enabled", false); }
                catch (IOException) { failed = true; }
                Check(failed, "locked config unexpectedly replaced");
                Check(File.ReadAllText(path) == original, "existing file changed");
            }
            Check(Directory.GetFiles(Path.GetDirectoryName(path), id + "*.tmp").Length == 0, "temporary file leaked");
        });
        Run("mod discovery is rooted at the game, not the working directory", () =>
        {
            var previous = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(Path.GetTempPath());
                Check(Utils.GetModsFolder() == Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Silk", "Mods"), "wrong mods root");
            }
            finally { Directory.SetCurrentDirectory(previous); }
        });
        Run("saving replaces the complete file without truncating existing readers", () =>
        {
            var id = Id("atomic");
            Config.LoadModConfig(id, Defaults());
            var path = Config.GetModConfigPath(id);
            var original = File.ReadAllText(path);
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Config.SetModConfigValue(id, "surge.enabled", false);
            using var reader = new StreamReader(stream);
            Check(reader.ReadToEnd() == original, "existing reader saw the file rewritten in place");
            Check(File.ReadAllText(path) != original, "new content was not saved");
        });
        Run("only exact Disabled directory names are excluded", () =>
        {
            Global("loader:\n  modFilePattern: '*.dll'\n  ignoreVersionMismatch: false\n");
            var root = Utils.GetModsFolder();
            foreach (var file in new[] { "a.dll", "DisabledFeature.dll", "NotDisabled/b.dll", "Disabled/c.dll", "nested/dIsAbLeD/d.dll" })
            {
                var path = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, Array.Empty<byte>());
            }
            var files = (string[])Invoke("DiscoverModFiles");
            Check(files.Length == 3, "unexpected discovery count: " + files.Length);
            Check(files.Any(path => path.EndsWith("DisabledFeature.dll")), "filename wrongly excluded");
            Check(files.SequenceEqual(files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => path, StringComparer.Ordinal)), "discovery order unstable");
        });
        Run("Initialize and Awake use one Unity component", () =>
        {
            ResetMods();
            LifecycleMod.Initialized = null;
            LifecycleMod.Awoken = null;
            Load<LifecycleMod>();
            Check(LifecycleMod.Initialized?.gameObject != null, "Initialize has no native component");
            Check(LifecycleMod.Awoken == null, "Awake ran before all mods were initialized");
            Silk.Mods.Manager.SetupMods();
            Check(ReferenceEquals(LifecycleMod.Initialized, LifecycleMod.Awoken), "Awake got a second instance");
            Check(LifecycleMod.Awoken.State == 42, "initialized state lost");
            Silk.Mods.Manager.SetupMods();
            Check(GameObject.All.SelectMany(go => go.Components).Count() == 1, "repeated setup duplicated components");
        });
        Run("duplicate mod IDs initialize only once", () =>
        {
            ResetMods();
            DuplicateOne.Calls = DuplicateTwo.Calls = 0;
            Load<DuplicateOne>();
            Load<DuplicateTwo>();
            Check(DuplicateOne.Calls == 1 && DuplicateTwo.Calls == 0, "duplicate initializer ran");
            Check(Silk.Mods.Manager.Mods.Count == 1 && Loader.LoadedMods.Count == 1, "duplicate registered");
        });
        Run("failed initializers never receive Unity callbacks or disable leaderboards", () =>
        {
            ResetMods();
            Load<FailingMod>();
            Silk.Mods.Manager.SetupMods();
            Check(Silk.Mods.Manager.Mods.Count == 0, "failed mod registered");
            Check(!GameObject.All.SelectMany(go => go.Components).Any(), "failed component survived");
            Check(!Utils.onlineMods && Loader.LoadedMods.Count == 0 && Loader.FailedMods.Count == 1, "failure classified incorrectly");
        });
        Run("a duplicate cannot rerun a failed initializer", () =>
        {
            ResetMods();
            FailingMod.Calls = 0;
            Load<FailingMod>();
            Load<FailingMod>();
            Check(FailingMod.Calls == 1, "partial initialization ran twice");
        });
        Run("reporting a failed mod before the announcer exists cannot abort startup", () =>
        {
            ResetMods();
            Load<FailingMod>();
            var announcer = Announcer.instance;
            try
            {
                Announcer.instance = null;
                Invoke("LogFailedModsDetails");
            }
            finally { Announcer.instance = announcer; }
        });
        Run("incompatible copies do not reserve the valid mod's ID", () =>
        {
            ResetMods();
            DuplicateOne.Calls = 0;
            Load<IncompatibleMod>();
            Load<DuplicateOne>();
            Check(DuplicateOne.Calls == 1, "compatible copy skipped");
            Check(Loader.LoadedMods.Count == 1 && Loader.FailedMods.Count == 1, "wrong version outcome");
        });
        await RunAsync("update requests yield and Mods UI exists before the response", async () =>
        {
            using var server = new VersionServer(200);
            Global($"updater:\n  checkForUpdates: true\n  latestVersionUrl: '{server.Url}'\n");
            ModsUI.Calls = 0;
            var invocation = Task.Run(() => AddModMenu.Postfix());
            await server.RequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                Check(ModsUI.Calls == 1, "Mods UI waits for network");
                await invocation.WaitAsync(TimeSpan.FromSeconds(2));
            }
            finally { server.Respond.TrySetResult(true); }
            await server.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            await invocation;
        });
        await RunAsync("HTTP failures are contained and reported", async () =>
        {
            using var server = new VersionServer(503);
            Global($"updater:\n  checkForUpdates: true\n  latestVersionUrl: '{server.Url}'\n");
            var update = Task.Run(() => Updater.CheckForUpdates());
            await server.RequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(!update.IsCompleted, "request did not yield");
            server.Respond.TrySetResult(true);
            await update.WaitAsync(TimeSpan.FromSeconds(5));
            Check(Logger.Messages.Any(message => message.Contains("Could not check for Silk updates")), "failure not logged");
        });
        Console.WriteLine($"{total - failures}/{total} regression tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void WriteMod(string id, string yaml)
    {
        var path = Config.GetModConfigPath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, yaml);
    }

    private static void Global(string yaml)
    {
        File.WriteAllText(Config.ConfigFile, yaml);
        Config.LoadConfig(new());
    }

    private static void ResetMods()
    {
        Silk.Mods.Manager.Mods.Clear();
        Loader.LoadedMods.Clear();
        Loader.FailedMods.Clear();
        var attempted = typeof(Loader).GetField("AttemptedModIds", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        (attempted as HashSet<string>)?.Clear();
        GameObject.All.Clear();
        Utils.onlineMods = false;
    }

    private static object Invoke(string name, params object[] args) =>
        typeof(Loader).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);

    private static void Load<T>()
    {
        var path = typeof(T).Assembly.Location;
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        Invoke("LoadSingleMod", assembly.MainModule.Types.Single(type => type.FullName == typeof(T).FullName), path);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Run(string name, Action action) => RunAsync(name, () => { action(); return Task.CompletedTask; }).GetAwaiter().GetResult();
    private static async Task RunAsync(string name, Func<Task> action)
    {
        total++;
        try { await action(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException().Message); }
    }

    private sealed class VersionServer : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        public readonly TaskCompletionSource<bool> RequestReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> Respond = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Url { get; }
        public Task Completion { get; }

        public VersionServer(int status)
        {
            listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/version";
            Completion = Serve(status);
        }

        private async Task Serve(int status)
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
            RequestReceived.TrySetResult(true);
            await Respond.Task;
            var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Test\r\nContent-Length: 5\r\nConnection: close\r\n\r\n0.7.0");
            await stream.WriteAsync(response);
        }

        public void Dispose() { Respond.TrySetResult(true); listener.Stop(); }
    }
}

[SilkMod("Lifecycle", new[] { "Test" }, "1", "0.7.0", "lifecycle", 1)]
public sealed class LifecycleMod : SilkMod
{
    public static LifecycleMod Initialized, Awoken;
    public int State;
    public override void Initialize() { Initialized = this; State = 42; }
    public void Awake() { Awoken = this; }
}
[SilkMod("Duplicate one", new[] { "Test" }, "1", "0.7.0", "duplicate", 1)]
public sealed class DuplicateOne : SilkMod
{
    public static int Calls;
    public override void Initialize() => Calls++;
}
[SilkMod("Duplicate two", new[] { "Test" }, "1", "0.7.0", "duplicate", 1)]
public sealed class DuplicateTwo : SilkMod
{
    public static int Calls;
    public override void Initialize() => Calls++;
}
[SilkMod("Failing", new[] { "Test" }, "1", "0.7.0", "failing", 1)]
public sealed class FailingMod : SilkMod
{
    public static int Calls;
    public override void Initialize() { Calls++; throw new InvalidOperationException("expected test failure"); }
}
[SilkMod("Incompatible", new[] { "Test" }, "1", "0.6.0", "duplicate", 1)]
public sealed class IncompatibleMod : SilkMod
{
    public override void Initialize() => throw new InvalidOperationException("must not run");
}
