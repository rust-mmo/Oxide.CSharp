﻿using Oxide.Core;
using System;
using System.Linq;
using System.Reflection;

namespace Oxide.Plugins
{
    public class CompilablePlugin : CompilableFile
    {
        private static object compileLock = new object();

        public CompiledAssembly LastGoodAssembly;
        public bool IsLoading;

        public CompilablePlugin(CSharpExtension extension, CSharpPluginLoader loader, string directory, string name, string compiledAssemblyName = null) : base(extension, loader, directory, name, compiledAssemblyName)
        {
        }

        protected override void OnLoadingStarted() => Loader.PluginLoadingStarted(this);

        protected override void OnCompilationRequested() => Loader.CompilationRequested(this);

        internal void LoadPlugin(Action<CSharpPlugin> callback = null)
        {
            if (CompiledAssembly == null)
            {
                Interface.Oxide.LogError("Load called before a compiled assembly exists: {0}", Name);
                //RemoteLogger.Error($"Load called before a compiled assembly exists: {Name}");
                return;
            }

            LoadCallback = callback;

            CompiledAssembly.LoadAssembly(loaded =>
            {
                if (!loaded)
                {
                    callback?.Invoke(null);
                    return;
                }

                if (CompilerErrors != null)
                {
                    InitFailed($"Unable to load {ScriptName}. {CompilerErrors}");
                    return;
                }

                foreach (var type in CompiledAssembly.LoadedAssembly.GetTypes().Where(x => x.Name == Name))
                {
                    if (type.GetCustomAttributes(typeof(InfoAttribute), true).Length <= 0)
                    {
                        Interface.Oxide.LogWarning($"Plugin '{Name}' (type {type.Name}) is missing an {nameof(InfoAttribute)} and will not be loaded");
                        continue;
                    }

                    CSharpPlugin plugin;
                    try
                    {
                        plugin = Activator.CreateInstance(type) as CSharpPlugin;
                    }
                    catch (MissingMethodException)
                    {
                        InitFailed($"Main plugin class should not have a constructor defined: {Name}");
                        return;
                    }
                    catch (TargetInvocationException invocationException)
                    {
                        Exception ex = invocationException.InnerException;
                        InitFailed($"Unable to load {ScriptName}. {ex.ToString()}");
                        return;
                    }
                    catch (Exception ex)
                    {
                        InitFailed($"Unable to load {ScriptName}. {ex.ToString()}");
                        return;
                    }

                    if (plugin == null)
                    {
                        //RemoteLogger.Error($"Plugin assembly failed to load: {ScriptName}");
                        InitFailed($"Plugin assembly failed to load: {ScriptName}");
                        return;
                    }

                    if (!plugin.SetPluginInfo(ScriptName, ScriptPath, CompiledAssemblyName))
                    {
                        InitFailed();
                        return;
                    }

                    if (CompiledAssemblyName == null)
                    {
                        plugin.Watcher = Extension.Watcher;
                        plugin.Loader = Loader;
                    }
                    else
                    {
                        plugin.Watcher = Extension.CompiledWatcher;
                        plugin.Loader = Extension.CompiledLoader;
                    }

                    if (!Interface.Oxide.PluginLoaded(plugin))
                    {
                        InitFailed();
                        return;
                    }

                    if (!CompiledAssembly.IsBatch)
                    {
                        LastGoodAssembly = CompiledAssembly;
                    }

                    callback?.Invoke(plugin);
                }
            });
        }

        internal override void OnCompilationStarted()
        {
            base.OnCompilationStarted();

            // Enqueue compilation of any plugins which depend on this plugin
            foreach (Core.Plugins.Plugin plugin in Interface.Oxide.RootPluginManager.GetPlugins())
            {
                if (!(plugin is CSharpPlugin))
                {
                    continue;
                }

                CompilablePlugin compilablePlugin = CSharpPluginLoader.GetCompilablePlugin(Directory, plugin.Name);
                if (!compilablePlugin.Requires.Contains(Name))
                {
                    continue;
                }

                compilablePlugin.CompiledAssembly = null;
                Loader.Load(compilablePlugin);
            }
        }

        protected override void InitFailed(string message = null)
        {
            base.InitFailed(message);
            if (LastGoodAssembly == null)
            {
                Interface.Oxide.LogInfo("No previous version to rollback plugin: {0}", ScriptName);
                return;
            }
            if (CompiledAssembly == LastGoodAssembly)
            {
                Interface.Oxide.LogInfo("Previous version of plugin failed to load: {0}", ScriptName);
                return;
            }
            Interface.Oxide.LogInfo("Rolling back plugin to last good version: {0}", ScriptName);
            CompiledAssembly = LastGoodAssembly;
            CompilerErrors = null;
            LoadPlugin();
        }
    }
}
