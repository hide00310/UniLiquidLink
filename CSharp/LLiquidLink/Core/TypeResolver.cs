using LLiquidLink.Logger;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace LLiquidLink
{
    /// <summary>Resolves .NET type names to <see cref="System.Type"/> objects within a curated set of assemblies.</summary>
    public class TypeResolver
    {
        readonly HashSet<string> _allowedAssemblySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Type> _typesByFullName = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        readonly Func<ILogger> _getLogger;

        /// <summary>Initialize the resolver with a logger factory.</summary>
        /// <param name="getLogger">Factory that returns the current logger instance.</param>
        public TypeResolver(Func<ILogger> getLogger)
        {
            _getLogger = getLogger;
        }

        /// <summary>
        /// Register the assembly of the direct caller and all assemblies it references.
        /// Must not be inlined so the calling assembly is detected correctly.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void RegisterCallerAssembly()
        {
            RegisterAssembly(Assembly.GetCallingAssembly());
        }

        /// <summary>Register <paramref name="asm"/> and all assemblies it references.</summary>
        /// <param name="asm">Assembly to allow for type resolution.</param>
        public void RegisterAssembly(Assembly asm)
        {
            AddAssembly(asm);
            foreach (var refName in asm.GetReferencedAssemblies())
            {
                AddAssembly(Assembly.Load(refName));
            }
        }

        /// <summary>Add a single assembly to the allowed set and index all its exported types.</summary>
        /// <param name="asm">Assembly to add.</param>
        void AddAssembly(Assembly asm)
        {
            if (!_allowedAssemblySet.Add(asm.GetName().Name))
            {
                return;
            }
            try
            {
                foreach (var t in asm.GetExportedTypes())
                {
                    if (t.FullName != null && !_typesByFullName.ContainsKey(t.FullName))
                    {
                        _typesByFullName[t.FullName] = t;
                    }
                }
            }
            catch (ReflectionTypeLoadException) { }
        }

        /// <summary>
        /// Write the full names of all indexed types to a CSV file.
        /// The Python gateway reads this file to resolve short type names sent by clients.
        /// </summary>
        /// <param name="path">Absolute path of the CSV file to write.</param>
        public void SaveAllowedTypesCsv(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("full_name");
            foreach (var fullName in _typesByFullName.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                sb.AppendLine(fullName);
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// Resolve a fully qualified type name to a <see cref="System.Type"/> within the registered assemblies.
        /// The Python gateway is responsible for expanding short names to full names before calling this method.
        /// </summary>
        /// <param name="typeName">Fully qualified type name (assembly-qualified names are rejected).</param>
        /// <returns>The matching <see cref="System.Type"/>.</returns>
        /// <exception cref="TypeLoadException">Thrown if the type is not in the allowed set or the name is assembly-qualified.</exception>
        public Type Resolve(string typeName)
        {
            if (typeName.Contains(','))
            {
                throw new TypeLoadException($"Assembly-qualified type names are not allowed: '{typeName}'");
            }

            if (_typesByFullName.TryGetValue(typeName, out Type type))
            {
                return type;
            }

            _getLogger().DebugFormat("allowed types: {0}", _typesByFullName.Count);
            throw new TypeLoadException($"Type '{typeName}' not found or not in allowed assemblies");
        }
    }
}
