﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;
using ModTek.Features.Logging;
using ModTek.Util;

namespace ModTek.Features.Profiling
{
    // checks if a method is safe for patching
    internal class PatchableChecker
    {
        internal bool DebugLogging = false;

        private readonly Assembly[] BlacklistedAssemblies;
        private readonly Type[] BlacklistedTypes;
        internal PatchableChecker()
        {
            BlacklistedAssemblies =
                AssemblyUtil.GetAssembliesByPattern(ModTek.Config.Profiling.BlacklistedAssemblyNamePattern)
                    .ToArray();
            MTLogger.Info.Log("\tblacklisted assemblies:" + BlacklistedAssemblies.Select(x => x.FullName).AsTextList());

            BlacklistedTypes =
                AssemblyUtil.GetTypesByPattern(ModTek.Config.Profiling.BlacklistedTypeNamePattern, BlacklistedAssemblies)
                    .ToArray();
            MTLogger.Info.Log("\tblacklisted types:" + BlacklistedTypes.Select(x => x.FullName).AsTextList());
        }

        internal bool IsAssemblyPatchable(Assembly assembly)
        {
            if (BlacklistedAssemblies.Contains(assembly))
            {
                if (DebugLogging)
                {
                    MTLogger.Info.Log("IsAssemblyPatchable BlacklistedAssemblies assembly=" + assembly.FullName);
                }
                return false;
            }
            return true;
        }

        internal bool IsTypePatchable(Type type)
        {
            if (!type.IsClass)
            {
                if (DebugLogging)
                {
                    MTLogger.Info.Log("IsTypePatchable !IsClass type=" + type.FullName);
                }
                return false;
            }
            // if (type.IsAbstract)
            // {
            //     if (DebugLogging)
            //     {
            //         MTLogger.Info.Log("IsTypePatchable IsAbstract type=" + type.FullName);
            //     }
            //     return false;
            // }
            // generic patching with harmony is not fool proof
            if (type.IsGenericType || type.IsConstructedGenericType || type.ContainsGenericParameters)
            {
                if (DebugLogging)
                {
                    MTLogger.Info.Log("IsTypePatchable IsGeneric type=" + type.FullName);
                }
                return false;
            }
            if (BlacklistedTypes.Contains(type))
            {
                if (DebugLogging)
                {
                    MTLogger.Info.Log("IsTypePatchable BlacklistedTypes type=" + type.FullName);
                }
                return false;
            }
            if (type.BaseType != null && !IsTypePatchable(type.BaseType))
            {
                if (DebugLogging)
                {
                    MTLogger.Info.Log("IsTypePatchable !IsTypePatchable(type.BaseType) type=" + type.FullName);
                }
                return false;
            }
            return true;
        }

        internal bool IsMethodPatchable(MethodInfo method)
        {
            if (method.IsConstructor)
            {
                if (DebugLogging)
                {
                    MTLogger.Info.Log("IsMethodPatchable IsConstructor method=" + method.FullDescription());
                }
                return false;
            }
            if (method.IsAbstract)
            {
                if (DebugLogging)
                {
                    MTLogger.Info.Log("IsMethodPatchable IsAbstract method=" + method.FullDescription());
                }
                return false;
            }
            // generic patching with harmony is not fool proof
            if (method.IsGenericMethod || method.ContainsGenericParameters)
            {
                if (DebugLogging)
                {
                    MTLogger.Info.Log("IsMethodPatchable IsGeneric method=" + method.FullDescription());
                }
                return false;
            }

            // this can be a harmful check, crashing the game
            // therefore keep it as the last check and do log any exceptions
            try
            {
                if (method.GetMethodBody() == null)
                {
                    if (DebugLogging)
                    {
                        MTLogger.Info.Log("IsMethodPatchable GetMethodBody=null");
                    }
                    return false;
                }
            }
            catch (Exception e)
            {
                MTLogger.Info.Log($"Error checking for body in {method.FullDescription()}", e);
                return false;
            }

            return true;
        }

        internal IEnumerable<MethodBase> FindPatchableMethodsCalledFromMethod(MethodBase containerMethod, int depth)
        {
            try
            {
                if (depth > 0)
                {
                    return FindMethodsCalledByMethodToBeWrapped(containerMethod, depth);
                }
            }
            catch (Exception e)
            {
                MTLogger.Info.Log($"Issue finding methods in {containerMethod}", e);
            }
            return Array.Empty<MethodBase>();
        }

        private IEnumerable<MethodBase> FindMethodsCalledByMethodToBeWrapped(MethodBase containerMethod, int depth)
        {
            var furtherDepth = depth - 1;
            foreach (var method in FindMethodsCalledByMethod(containerMethod))
            {
                yield return method;
                if (furtherDepth <= 0)
                {
                    continue;
                }
                foreach (var callee in FindPatchableMethodsCalledFromMethod(method, furtherDepth))
                {
                    yield return callee;
                }
            }
        }

        internal bool IsPatchable(MethodInfo method)
        {
            var type = method.DeclaringType;
            if (type == null)
            {
                if (DebugLogging)
                {
                    MTLogger.Info.Log("IsPatchable type=null method=" + method.FullDescription());
                }
                return false;
            }
            if (!IsAssemblyPatchable(type.Assembly))
            {
                if (DebugLogging)
                {
                    MTLogger.Info.Log("IsPatchable !IsAssemblyPatchable method=" + method.FullDescription());
                }
                return false;
            }
            if (!IsTypePatchable(type))
            {
                if (DebugLogging)
                {
                    MTLogger.Info.Log("IsPatchable !IsTypePatchable method=" + method.FullDescription());
                }
                return false;
            }
            if (!IsMethodPatchable(method))
            {
                if (DebugLogging)
                {
                    MTLogger.Info.Log("IsPatchable !IsMethodPatchable method=" + method.FullDescription());
                }
                return false;
            }
            return true;
        }

        private int counter;
        private IEnumerable<MethodBase> FindMethodsCalledByMethod(MethodBase containerMethod)
        {
            var dynamicMethod = DynamicTools.CreateDynamicMethod(containerMethod, "_Profiler" + counter++).GetILGenerator();

            var instructions = MethodBodyReader.GetInstructions(dynamicMethod, containerMethod);
            var callees = instructions
                .Where(i => i.opcode == OpCodes.Call || i.opcode == OpCodes.Callvirt)
                .Select(i => i.operand);

            foreach (var callee in callees)
            {
                if (!(callee is MethodInfo method))
                {
                    // all the time a constructor, maybe we should also profile those? => can only do via transpilers ( so no )
                    continue;
                }
                if (!IsPatchable(method))
                {
                    continue;
                }
                yield return method;
            }
        }
    }
}
