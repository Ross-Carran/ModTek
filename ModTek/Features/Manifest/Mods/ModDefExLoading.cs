using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Harmony;
using ModTek.Features.CustomResources;
using ModTek.Util;
using Newtonsoft.Json;

namespace ModTek.Features.Manifest.Mods;

internal static class ModDefExLoading
{
    internal static bool LoadMod(ModDefEx modDef, out string reason)
    {
        Log.Main.Info?.Log($"{modDef.QuotedName} {modDef.Version}");

        // although manifest related, CR listings are mostly relevant for the FinishedLoading call
        CustomResourcesFeature.ProcessModDef(modDef);

        // load the mod assembly
        if (modDef.DLL != null && !LoadAssemblyAndCallInit(modDef))
        {
            reason = "Fail to call init method";
            return false;
        }

        reason = "Success";
        return true;
    }

    private static bool LoadAssemblyAndCallInit(ModDefEx modDef)
    {
        var dllPath = Path.Combine(modDef.Directory, modDef.DLL);
        string typeName = null;
        var methodName = "Init";

        if (!File.Exists(dllPath))
        {
            Log.Main.Warning?.Log($"\tDLL specified ({dllPath}), but it's missing! Aborting load.");
            return false;
        }

        if (modDef.DLLEntryPoint != null)
        {
            GetTypeAndMethodNames(modDef.DLLEntryPoint, out typeName, out methodName);
        }

        var assembly = AssemblyUtil.LoadDLL(dllPath);
        if (assembly == null)
        {
            Log.Main.Warning?.Log($"\tFailed to load mod assembly at path {dllPath}.");
            return false;
        }

        var methods = AssemblyUtil.FindMethods(assembly, methodName, typeName);
        if (methods == null || methods.Length == 0)
        {
            Log.Main.Warning?.Log($"\t\tCould not find any methods in assembly with name '{methodName}' and with type '{typeName ?? "not specified"}'");
            return false;
        }

        foreach (var method in methods)
        {
            var directory = modDef.Directory;
            var settings = modDef.Settings.ToString(Formatting.None);

            var parameterDictionary = new Dictionary<string, object>
            {
                { "modDir", directory },
                { "modDirectory", directory },
                { "directory", directory },
                { "modSettings", settings },
                { "settings", settings },
                { "settingsJson", settings },
                { "settingsJSON", settings },
                { "JSON", settings },
                { "json", settings }
            };

            try
            {
                if (AssemblyUtil.InvokeMethodByParameterNames(method, parameterDictionary))
                {
                    continue;
                }

                if (AssemblyUtil.InvokeMethodByParameterTypes(
                        method,
                        new object[]
                        {
                            directory,
                            settings
                        }
                    ))
                {
                    continue;
                }
            }
            catch (Exception e)
            {
                Log.Main.Warning?.Log($"\tWhile invoking '{method.DeclaringType?.Name}.{method.Name}', an exception occured", e);
                return false;
            }

            Log.Main.Warning?.Log($"\tCould not invoke method with name '{method.DeclaringType?.Name}.{method.Name}'");
            return false;
        }

        modDef.Assembly = assembly;

        if (!modDef.EnableAssemblyVersionCheck)
        {
            TryResolveAssemblies.Add(assembly.GetName().Name, assembly);
        }

        return true;
    }

    internal static void FinishedLoading(ModDefEx modDef, List<string> ModLoadOrder)
    {
        const string methodName = "FinishedLoading";
        MethodInfo[] methods = null;
        if (modDef.DLLEntryPoint != null)
        {
            if (GetTypeAndMethodNames(modDef.DLLEntryPoint, out var typeName, out _))
            {
                methods = AssemblyUtil.FindMethods(modDef.Assembly, methodName, typeName);
            }
        }

        if (methods == null || methods.Length == 0)
        {
            methods = AssemblyUtil.FindMethods(modDef.Assembly, methodName);
        }

        if (methods == null || methods.Length == 0)
        {
            return;
        }
        var paramsDictionary = new Dictionary<string, object> { { "loadOrder", new List<string>(ModLoadOrder) } };

        if (modDef.CustomResourceTypes.Count > 0)
        {
            var customResources = CustomResourcesFeature.GetResourceDictionariesForTypes(modDef.CustomResourceTypes);
            paramsDictionary.Add("customResources", customResources);
        }

        foreach (var method in methods)
        {
            try
            {
                Log.Main.Info?.Log($"\tFinishedLoading {modDef.QuotedName}");
                if (!AssemblyUtil.InvokeMethodByParameterNames(method, paramsDictionary))
                {
                    Log.Main.Warning?.Log($"\t{modDef.QuotedName}: Failed to invoke '{method.DeclaringType?.Name}.{method.Name}', parameter mismatch");
                }
            }
            catch (Exception e)
            {
                Log.Main.Warning?.Log($"\t{modDef.QuotedName}: Failed to invoke '{method.DeclaringType?.Name}.{method.Name}', exception", e);
            }
        }
    }

    private static bool GetTypeAndMethodNames(string dotNotation, out string typeName, out string methodName)
    {
        var pos = dotNotation.LastIndexOf('.');
        if (pos == -1)
        {
            typeName = null;
            methodName = dotNotation;
            return false;
        }

        typeName = dotNotation.Substring(0, pos);
        methodName = dotNotation.Substring(pos + 1);
        return true;
    }

    private static readonly Dictionary<string, Assembly> TryResolveAssemblies = new();

    internal static void Setup()
    {
        // setup assembly resolver
        TryResolveAssemblies.Add("0Harmony", Assembly.GetAssembly(typeof(HarmonyInstance)));
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var resolvingName = new AssemblyName(args.Name);
            return !TryResolveAssemblies.TryGetValue(resolvingName.Name, out var assembly) ? null : assembly;
        };
    }
}