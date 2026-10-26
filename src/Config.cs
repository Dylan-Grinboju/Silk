using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using UnityEngine;

namespace Silk
{
    public static class Config
    {
        private static Dictionary<string, object> _config = new();
        private static Dictionary<string, object> _defaultConfig = new();
        private static readonly Dictionary<string, Dictionary<string, object>> _modConfigs = new();
        private static readonly Dictionary<string, Dictionary<string, object>> _defaultModConfigs = new();

        public static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Silk", "Config");
        public static string ConfigFile => Path.Combine(ConfigPath, "silk.yaml");

        public static void LoadConfig(Dictionary<string, object> defaultConfig)
        {
            _defaultConfig = CloneConfig(defaultConfig);
            Logger.LogInfo("Loading config...");
            _config = ReadConfig(ConfigFile, _defaultConfig);
            SaveConfig();
        }

        private static Dictionary<string, object> MergeConfigs(Dictionary<string, object> defaultConfig, Dictionary<string, object> userConfig)
        {
            var mergedConfig = CloneConfig(defaultConfig);

            foreach (var item in userConfig)
            {
                if (mergedConfig.TryGetValue(item.Key, out var defaultValue) && defaultValue is Dictionary<string, object> defaultDict && item.Value is Dictionary<string, object> userDict)
                {
                    mergedConfig[item.Key] = MergeConfigs(defaultDict, userDict);
                }
                else
                {
                    mergedConfig[item.Key] = item.Value;
                }
            }

            return mergedConfig;
        }

        private static Dictionary<string, object> CloneConfig(Dictionary<string, object> config)
        {
            return config.ToDictionary(pair => pair.Key, pair => CloneValue(pair.Value));
        }

        private static object CloneValue(object value)
        {
            if (value is Dictionary<string, object> dictionary)
                return CloneConfig(dictionary);
            if (value is IList<object> list)
                return list.Select(CloneValue).ToList();
            return value;
        }

        private static Dictionary<string, object> ReadConfig(string path, Dictionary<string, object> defaults)
        {
            if (!File.Exists(path))
                return CloneConfig(defaults);

            var yaml = File.ReadAllText(path);
            try
            {
                var raw = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build().Deserialize<object>(yaml);
                var normalized = NormalizeYamlObjects(raw);
                if (normalized != null && !(normalized is Dictionary<string, object>))
                    throw new InvalidDataException("The config root must be a YAML mapping.");
                return MergeConfigs(defaults, normalized as Dictionary<string, object> ?? new());
            }
            catch (Exception ex) when (ex is YamlException || ex is InvalidDataException)
            {
                var backup = path + ".invalid-" + Guid.NewGuid().ToString("N") + ".bak";
                File.Copy(path, backup);
                Logger.LogError($"Invalid config '{path}': {ex.Message}. Original saved to '{backup}'. Using defaults.");
                return CloneConfig(defaults);
            }
        }

        private static T ConvertValue<T>(object value)
        {
            if (value is T typed)
                return typed;
            return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }

        // Replace only after serialization and the complete temporary write succeed.
        private static void WriteConfig(string path, Dictionary<string, object> config)
        {
            var yaml = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build().Serialize(config);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, yaml);
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                    File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        public static T GetConfigValue<T>(string key, T? defaultValue = default)
        {
            try
            {
                var keys = key.Split('.');
                object currentNode = _config;

                foreach (var k in keys)
                {
                    if (currentNode is Dictionary<string, object> dict && dict.TryGetValue(k, out var value))
                    {
                        currentNode = value;
                    }
                    else
                    {
                        return defaultValue ?? throw new InvalidOperationException($"Failed to get config value for key '{key}'");
                    }
                }

                return ConvertValue<T>(currentNode);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting config value for key '{key}': {ex.Message}");
                return defaultValue ?? throw new InvalidOperationException($"Failed to get config value for key '{key}'");
            }
        }

        public static void SetConfigValue(string key, object value)
        {
            var keys = key.Split('.');
            Dictionary<string, object> currentNode = _config;

            for (int i = 0; i < keys.Length - 1; i++)
            {
                var k = keys[i];
                if (!currentNode.TryGetValue(k, out var nextNode) || !(nextNode is Dictionary<string, object>))
                {
                    nextNode = new Dictionary<string, object>();
                    currentNode[k] = nextNode;
                }
                currentNode = (Dictionary<string, object>)nextNode;
            }

            currentNode[keys.Last()] = value;
            SaveConfig();
        }

        public static void SaveConfig()
        {
            WriteConfig(ConfigFile, _config);
        }

        private static object NormalizeYamlObjects(object obj)
        {
            if (obj is IDictionary<object, object> dict)
                return dict.ToDictionary(
                    kv => kv.Key.ToString(),
                    kv => NormalizeYamlObjects(kv.Value)
                );
            if (obj is IList<object> list)
                return list.Select(NormalizeYamlObjects).ToList();
            return obj;
        }

        // Mod Config Methods

        public static string GetModConfigPath(string modId)
        {
            return Path.Combine(ConfigPath, "Mods", $"{modId}.yaml");
        }

        public static void LoadModConfig(string modId, Dictionary<string, object> defaultConfig)
        {
            if (string.IsNullOrEmpty(modId))
                throw new ArgumentException("Mod ID cannot be null or empty", nameof(modId));

            _defaultModConfigs[modId] = CloneConfig(defaultConfig ?? new Dictionary<string, object>());
            var configPath = GetModConfigPath(modId);
            _modConfigs[modId] = ReadConfig(configPath, _defaultModConfigs[modId]);
            SaveModConfig(modId);
        }

        public static T GetModConfigValue<T>(string modId, string key, T defaultValue = default)
        {
            try
            {
                if (!_modConfigs.TryGetValue(modId, out var modConfig))
                    return defaultValue;

                var keys = key.Split('.');
                object currentNode = modConfig;

                foreach (var k in keys)
                {
                    if (currentNode is Dictionary<string, object> dict && dict.TryGetValue(k, out var value))
                    {
                        currentNode = value;
                    }
                    else
                    {
                        return defaultValue;
                    }
                }

                return ConvertValue<T>(currentNode);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting config value for mod {modId}, key '{key}': {ex.Message}");
                return defaultValue;
            }
        }

        public static void SetModConfigValue(string modId, string key, object value)
        {
            if (!_modConfigs.TryGetValue(modId, out var modConfig))
            {
                modConfig = new Dictionary<string, object>();
                _modConfigs[modId] = modConfig;
            }

            var keys = key.Split('.');
            Dictionary<string, object> currentNode = modConfig;

            for (int i = 0; i < keys.Length - 1; i++)
            {
                var k = keys[i];
                if (!currentNode.TryGetValue(k, out var nextNode) || !(nextNode is Dictionary<string, object>))
                {
                    nextNode = new Dictionary<string, object>();
                    currentNode[k] = nextNode;
                }
                currentNode = (Dictionary<string, object>)nextNode;
            }

            currentNode[keys.Last()] = value;
            SaveModConfig(modId);
        }

        private static void SaveModConfig(string modId)
        {
            if (!_modConfigs.TryGetValue(modId, out var config))
                return;

            var configPath = GetModConfigPath(modId);
            WriteConfig(configPath, config);
        }
    }
}
