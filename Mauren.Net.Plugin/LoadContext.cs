using System;
using System.Reflection;
using System.Runtime.Loader;

namespace Mauren.Net.Plugin
{
    /// <summary>
    /// Represents a custom assembly load context for loading plugin assemblies and unmanaged DLLs.
    /// </summary>
    internal class LoadContext<TPlugin> : AssemblyLoadContext
    {
        // A resolver to help locate assemblies and unmanaged DLLs
        private readonly AssemblyDependencyResolver _resolver;

        /// <summary>
        /// Initializes a new instance of the <see cref="LoadContext{TPlugin}"/> class with the specified plugin path.
        /// </summary>
        /// 
        /// <param name="pluginPath">
        /// A string representing the path to the plugin directory.
        /// This path is used to initialize the AssemblyDependencyResolver, which helps locate assemblies and unmanaged DLLs for loading.
        /// </param>
        public LoadContext(String pluginPath, String? name = default, Boolean isCollectible = true) : base(name ?? pluginPath, isCollectible)
        {
            // Initialize the resolver with the path to the plugin
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        /// <inheritdoc/>
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Use the resolver to find the path to the assembly
            String? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);

            // If the assembly path is found
            if (assemblyPath != null)
            {
                // Load the assembly from the resolved path
                return LoadFromAssemblyPath(assemblyPath);
            }

            // Return null to indicate that the assembly could not be loaded
            return null;
        }

        /// <inheritdoc/>
        protected override IntPtr LoadUnmanagedDll(String unmanagedDllName)
        {
            // Use the resolver to find the path to the unmanaged DLL
            String? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);

            // If the library path is found
            if (libraryPath != null)
            {
                // Load the unmanaged DLL from the resolved path
                return LoadUnmanagedDllFromPath(libraryPath);
            }

            // Return IntPtr.Zero to indicate that the unmanaged DLL could not be loaded
            return IntPtr.Zero;
        }
    }
}
