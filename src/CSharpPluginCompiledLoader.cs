using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Random = System.Random;

namespace Oxide.CSharp
{
    public class CSharpPluginCompiledLoader : PluginLoader
    {
        private static CSharpExtension extension;
        private static Dictionary<string, List<string>> pluginsByAssembly = new Dictionary<string, List<string>>();

        public override string FileExtension => ".dll";

        public CSharpPluginCompiledLoader(CSharpExtension extension)
        {
            CSharpPluginCompiledLoader.extension = extension;
        }

        /// <summary>
        /// Attempt to synchronously load a precompiled assembly containing plugins
        /// </summary>
        /// <param name="directory"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public override Plugin Load(string directory, string name)
        {
            var rawAssembly = GetPatchedAssembly(File.ReadAllBytes($"{directory}/{name}.dll"));

            var pluginNames = GetPlugins(rawAssembly);
            if (pluginNames.Length <= 0)
            {
                Interface.Oxide.LogWarning($"Assembly {name} contains no plugins and will not be loaded");
                return null;
            }

            if (!pluginsByAssembly.ContainsKey(name))
            {
                pluginsByAssembly.Add(name, new List<string>());
            }

            var plugins = pluginNames
                .Select(pluginName => new CompilablePlugin(extension, null, directory, pluginName, name))
                .ToList();

            var assembly = new CompiledAssembly(name, plugins.ToArray(), rawAssembly, 0.0f);

            foreach (var plugin in plugins)
            {
                plugin.CompiledAssembly = assembly;

                // finally load the plugin.
                plugin.LoadPlugin(pl =>
                {
                    if (pl != null)
                    {
                        LoadedPlugins[pl.Name] = pl;
                        pluginsByAssembly[name].Add(pl.Name);
                    }
                });
            }

            return null;
        }

        /// <summary>
        /// Attempt to synchronously unload all plugins of a loaded assembly and reloads the new version of the assembly
        /// </summary>
        /// <param name="directory"></param>
        /// <param name="name"></param>
        public override void Reload(string directory, string name)
        {
            if (!pluginsByAssembly.TryGetValue(name, out var plugins))
            {
                return;
            }

            foreach (var plugin in plugins)
            {
                Interface.Oxide.UnloadPlugin(plugin);
            }

            pluginsByAssembly[name].Clear();

            Load(directory, name);
        }

        /// <summary>
        /// Called when the plugin manager is unloading a plugin that was loaded by this plugin loader
        /// </summary>
        /// <param name="pluginBase"></param>
        public override void Unloading(Plugin pluginBase)
        {
            LoadedPlugins.Remove(pluginBase.Name);
        }

        private static byte[] GetPatchedAssembly(byte[] rawAssembly)
        {
            var stream = new MemoryStream(rawAssembly);
            var definition = AssemblyDefinition.ReadAssembly(stream);
            
            var random = new Random(Guid.NewGuid().GetHashCode());
            var randomId = random.Next(int.MaxValue).ToString();
            
            definition.Name.Name += randomId;
            definition.MainModule.Name += randomId;
            
            using (var ms = new MemoryStream())
            {
                definition.Write(ms, new WriterParameters());
                return ms.ToArray();
            }
        }

        private static string[] GetPlugins(byte[] rawAssembly)
        {
            var stream = new MemoryStream(rawAssembly);
            var definition = AssemblyDefinition.ReadAssembly(stream);

            return definition.MainModule.Types
                .Where(IsPluginType)
                .Select(type => type.Name)
                .ToArray();
        }

        private static bool IsPluginType(TypeDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return definition.Name == nameof(CSharpPlugin) ||
                   definition.Name == nameof(CovalencePlugin) ||
                   IsPluginType(definition.BaseType?.Resolve());
        }
    }
}
