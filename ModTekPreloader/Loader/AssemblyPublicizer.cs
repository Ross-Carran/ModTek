﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModTekPreloader.Logging;
using Mono.Cecil;

namespace ModTekPreloader.Loader
{
    internal static class AssemblyPublicizer
    {
        internal static void MakePublic(IAssemblyResolver resolver)
        {
            Logger.Log($"Publicizing assemblies to `{Paths.GetRelativePath(Paths.AssembliesPublicizedDirectory)}`:");
            Directory.CreateDirectory(Paths.AssembliesPublicizedDirectory);
            foreach (var name in Config.Instance.AssembliesToMakePublic)
            {
                var assembly = resolver.Resolve(new AssemblyNameReference(name, null));
                MakeAssemblyPublic(assembly);
                var path = Path.Combine(Paths.AssembliesPublicizedDirectory, $"{assembly.Name.Name}.dll");
                Logger.Log($"\t{Path.GetFileName(path)}");
                assembly.Write(path);
            }
        }

        private static void MakeAssemblyPublic(AssemblyDefinition assembly)
        {
            foreach (var type in GetAllTypes(assembly))
            {
                MakeTypePublic(type);
            }
        }

        private static void MakeTypePublic(TypeDefinition type)
        {
            if (IsCompiledGenerated(type))
            {
                return;
            }

            if (type.IsNested)
            {
                type.IsNestedPublic = true;
            }
            else
            {
                type.IsPublic = true;
            }

            foreach (var method in type.Methods)
            {
                MakeMethodPublic(method);
            }

            // property methods are made by the compiler and therefore skipped during generic method publicizing
            foreach (var property in type.Properties)
            {
                MakePropertyPublic(property);
            }

            foreach (var field in type.Fields)
            {
                MakeFieldPublic(field);
            }
        }

        private static void MakeMethodPublic(MethodDefinition method)
        {
            if (method.IsCompilerControlled || IsCompiledGenerated(method))
            {
                return;
            }
            if (method.IsStatic && method.IsConstructor)
            {
                return;
            }
            method.IsCheckAccessOnOverride = false;
            method.IsPublic = true;
        }

        private static void MakePropertyPublic(PropertyDefinition property)
        {
            if (property.GetMethod != null)
            {
                property.GetMethod.IsPublic = true;
            }
            if (property.SetMethod != null)
            {
                property.SetMethod.IsPublic = true;
            }
        }

        private static void MakeFieldPublic(FieldDefinition field)
        {
            if (field.IsCompilerControlled || IsCompiledGenerated(field))
            {
                return;
            }
            field.IsPublic = true;
            field.IsInitOnly = false;
        }

        private static bool IsCompiledGenerated(ICustomAttributeProvider member)
        {
            return member.CustomAttributes.Any(x => x.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");
        }

        private static IEnumerable<TypeDefinition> GetAllTypes(AssemblyDefinition assembly)
        {
            var typeQueue = new Stack<TypeDefinition>(assembly.MainModule.Types);

            while (typeQueue.TryPop(out var type))
            {
                if (!Config.Instance.TypesToNotMakePublic.Contains(type.FullName))
                {
                    yield return type;
                }

                foreach (var nestedType in type.NestedTypes)
                {
                    typeQueue.Push(nestedType);
                }
            }
        }
    }
}
