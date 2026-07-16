using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;

namespace Mauren.Net.Plugin
{
    public partial class Loader<TPlugin> : ILoader<TPlugin>
    {
        private readonly ILogger<Loader<TPlugin>> _logger;
        private readonly ConcurrentDictionary<LoadContext<TPlugin>, ConcurrentBag<Type>> _registry;
        /// <summary>
        /// The host application's <see cref="IServiceProvider"/>.
        /// </summary>
        private readonly IServiceProvider _serviceProvider;

        public event EventHandler<PluginLoadedEventArgs<TPlugin>>? PluginLoaded;
        protected virtual void OnPluginLoaded(PluginLoadedEventArgs<TPlugin> e)
        {
            PluginLoaded?.Invoke(this, e);
        }

        public event EventHandler<PluginUnloadedEventArgs<TPlugin>>? PluginUnloaded;
        protected virtual void OnPluginUnloaded(PluginUnloadedEventArgs<TPlugin> e)
        {
            PluginUnloaded?.Invoke(this, e);
        }

        public Loader(ILogger<Loader<TPlugin>> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _registry = new();
        }

        public DirectoryInfo GetDirectoryFromConfiguration(IConfiguration configuration, String key = "PluginPath", String defaultValue = "./Plugins")
        {
            // Read the path value from configuration
            String path = configuration.GetValue<String>(key, defaultValue);
            // Initialize directory info
            DirectoryInfo directoryInfo = new(path);
            // Return directory info
            return directoryInfo;
        }

        public async Task ScanAsync(DirectoryInfo directory, CancellationToken cancellationToken = default)
        {
            // Ensure the provided directory exists
            EnsureDirectoryExists(directory, createIfNotExists: true);

            // Ensure the log level is enabled
            if (_logger.IsEnabled(LogLevel.Information))
                // Log information
                _logger.Log(LogLevel.Information, "Directory scan starting for '{path}': Discovering {type}", directory.FullName, typeof(TPlugin).Name);

            // Enumerate files in directory
            IEnumerable<FileInfo> files = directory.EnumerateFiles("*.dll", SearchOption.AllDirectories);

            // Iterate through the plugin assembly files and load them into their own LoadContext
            foreach (FileInfo file in files)
            {
                // Initialize an assembly load context
                AssemblyLoadContext? assemblyLoadContext = null;

                try
                {
                    // If the file's directory name is null
                    if (file.DirectoryName is not String directoryName)
                        // Throw exception
                        throw new DirectoryNotFoundException("Could not determine directory name");

                    // Initialize the assembly load context
                    assemblyLoadContext = new LoadContext<TPlugin>(directoryName);

                    // Load the assembly via the load context
                    Assembly assembly = assemblyLoadContext.LoadFromAssemblyPath(file.FullName);

                    // Discover types exported from the assembly
                    IEnumerable<Type> discoveredTypes = assembly.GetExportedTypes()
                        // Filter to types implementing the interface
                        .Where((Type type) => typeof(TPlugin).IsAssignableFrom(type))
                        // Filter to non-abstract classes
                        .Where((Type type) => type.IsClass && !type.IsAbstract);

                    // Ensure the assembly load context is a LoadContext<T> instance
                    if (assemblyLoadContext is not LoadContext<TPlugin> context)
                        continue;

                    // Iterate over the discovered types
                    foreach (Type discoveredType in discoveredTypes)
                    {
                        // Add the discovered type
                        await AddAsync(context, discoveredType, cancellationToken);
                    }

                    // Ensure the log level is enabled
                    if (_logger.IsEnabled(LogLevel.Debug))
                        // Log result
                        _logger.Log(LogLevel.Debug, "Assembly load succeeded for '{file}'", file.FullName);
                }
                catch (Exception exception)
                {
                    // Ensure the log level is enabled
                    if (_logger.IsEnabled(LogLevel.Debug))
                        // Log exception
                        _logger.Log(LogLevel.Debug, exception, "Assembly load failed for '{file}'", file.FullName);
                    
                    // Unload the assembly load context, if initialized
                    assemblyLoadContext?.Unload();

                    // Skip the file
                    continue;
                }
            }

            // Ensure the log level is enabled
            if (_logger.IsEnabled(LogLevel.Information))
                // Log result
                _logger.Log(LogLevel.Information, "Directory scan complete for '{path}': Discovered {type} ({count})", directory.FullName, typeof(TPlugin).Name, _registry.Count);
        }

        private Boolean MatchesStartupConvention(Type type)
        {
            // If the type's name does not end in 'Startup'
            if (type.Name.EndsWith("Startup", StringComparison.OrdinalIgnoreCase) is false)
                return false;

            String name = "ConfigureServices";
            BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
            Binder? binder = null;
            Type[] types = [typeof(IServiceCollection)];
            ParameterModifier[]? modifiers = null;
            // If the type does not have a 'void ConfigureServices(IServiceCollection _)' method
            if (type.GetMethod(name, bindingFlags, binder, types, modifiers) is not MethodInfo methodInfo || methodInfo.ReturnType != typeof(void))
                return false;

            return true;
        }

        internal IServiceProvider GetProvider(Type implementationType)
        {
            // Construct a new service collection for the type
            IServiceCollection services = new ServiceCollection();

            // Get the assembly of the type
            Assembly assembly = implementationType.Assembly;

            // Get all exported types that implement a manifest
            IEnumerable<Type> manifestTypes = assembly.GetExportedTypes()
                // Filter to types that implement a manifest
                .Where((Type type) => typeof(IServiceManifest).IsAssignableFrom(type))
                // Filter to non-abstract class types
                .Where((Type type) => type.IsClass && !type.IsAbstract);

            // Iterate over exported manifest types
            foreach (Type manifestType in manifestTypes)
            {
                // Create an instance of the manifest type
                Object? instance = Activator.CreateInstance(manifestType);
                // If the instance is not a manifest, skip
                if (instance is not IServiceManifest manifest) continue;
                // Register the manifest's services into the service collection
                manifest.RegisterServices(services);
            }

            // Get all exported types that implement the Startup convention
            IEnumerable<Type> startupTypes = assembly.GetExportedTypes()
                // Filter to types that match the Startup class convention
                .Where(MatchesStartupConvention)
                // Filter to non-abstract class types
                .Where((Type type) => type.IsClass && !type.IsAbstract);

            // Iterate over exported manifest types
            foreach (Type startupType in startupTypes)
            {
                // Create an instance of the manifest type
                Object? instance = Activator.CreateInstance(startupType);
                // If the instance is not a manifest, skip
                if (instance is not Object startup) continue;

                String name = "ConfigureServices";
                BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
                Binder? binder = null;
                Type[] types = [typeof(IServiceCollection)];
                ParameterModifier[]? modifiers = null;
                // If the instance does not have a ConfigureServices method
                if (startupType.GetMethod(name, bindingFlags, binder, types, modifiers) is not MethodInfo methodInfo) continue;
                // Invoke the ConfigureServices method
                Object? _ = methodInfo.Invoke(instance, [services]);
            }

            // Register the plugin
            services.AddTransient(implementationType);
            services.AddTransient(typeof(TPlugin), implementationType);

            // Build the plugin's service collection as the primary provider
            IServiceProvider primaryProvider = services.BuildServiceProvider();

            // Initialize a fallback service provider from the primary and host providers
            IServiceProvider fallbackProvider = new FallbackServiceProvider(primaryProvider, _serviceProvider);

            // Return the built provider
            return fallbackProvider;
        }

        [Obsolete]
        internal async Task<IServiceProvider> RegisterAsync_OLD(Type implementationType, CancellationToken cancellationToken = default)
        {
            // Construct a new service collection for the type
            IServiceCollection services = new ServiceCollection();

            // Determine the constructor to use for the implementation type
            ConstructorInfo constructor = implementationType.GetConstructors().First();
            // Get the constructor's parameters
            ParameterInfo[] parameters = constructor.GetParameters();

            // Iterate over the found parameters
            foreach (ParameterInfo parameter in parameters)
            {
                // Get the load context of the implementation type's assembly
                AssemblyLoadContext? implementationLoadContext = AssemblyLoadContext.GetLoadContext(implementationType.Assembly);
                // Get the load context of the dependency type's assembly
                AssemblyLoadContext? dependencyLoadContext = AssemblyLoadContext.GetLoadContext(parameter.ParameterType.Assembly);

                // If the dependency's load context is the same as the implementation type's load context
                if (dependencyLoadContext == implementationLoadContext)
                {
                    _logger.LogDebug("Looking for {type} dependency {dependencyType} in assembly {name}", implementationType.Name, parameter.ParameterType.Name, implementationType.Assembly.FullName);
                    // Get the matching dependency from the implementation type's assembly
                    Type dependency = implementationType.Assembly.GetTypes()
                        // Filter to types that the parameter's type is assignable from
                        .Where((Type type) => parameter.ParameterType.IsAssignableFrom(type))
                        // Filter to non-abstract class types
                        .Where((Type type) => type.IsClass && !type.IsAbstract)
                        // Get the only option
                        .Single();

                    // Add the dependency as a transient implementation of the parameter type
                    services.AddTransient(parameter.ParameterType, dependency);
                }
                // If the dependency's load context is not the same as the implementation type's load context
                else
                {
                    _logger.LogDebug("Looking for {type} dependency {dependencyType} in host service provider", implementationType.Name, parameter.ParameterType.Name);
                    // Get a matching dependency from the host application's service provider
                    Object dependency = _serviceProvider.GetRequiredService(parameter.ParameterType) switch
                    {
                        Object value => value,
                        _ => throw new InvalidOperationException($"Failed to resolve dependency '{parameter.ParameterType.FullName}'"),
                    };

                    // Add the dependency as a singleton implementation of the parameter type
                    services.AddSingleton(parameter.ParameterType, dependency);
                }
            }

            // Register the plugin
            services.AddTransient(implementationType);
            services.AddTransient(typeof(TPlugin), implementationType);

            // Build the service provider
            ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = false,
                ValidateScopes = false,
            });

            // Return the built provider
            return provider;
        }

        internal async Task AddAsync(LoadContext<TPlugin> loadContext, Type implementationType, CancellationToken cancellationToken = default)
        {
            // Define behavior for adding a new load context entry
            Func<LoadContext<TPlugin>, ConcurrentBag<Type>> addValueFactory = (LoadContext<TPlugin> _) =>
            {
                // Create a new bag from the added implementation type
                ConcurrentBag<Type> implementationTypes = new() { implementationType };
                // Return the created bag
                return implementationTypes;

            };

            // Define behavior for updating an existing load context entry
            Func<LoadContext<TPlugin>, ConcurrentBag<Type>, ConcurrentBag<Type>> updateValueFactory = (LoadContext<TPlugin> _, ConcurrentBag<Type> implementationTypes) =>
            {
                // Update the existing bag with the added implementation type
                implementationTypes.Add(implementationType);
                // Return the updated bag
                return implementationTypes;
            };
            // Update the registry
            _registry.AddOrUpdate(loadContext, addValueFactory, updateValueFactory);

            // Get the service provider for the implementation type
            IServiceProvider serviceProvider = GetProvider(implementationType);

            // Invoke plugin load event
            OnPluginLoaded(new PluginLoadedEventArgs<TPlugin>
            {
                ImplementationType = implementationType,
                ServiceProvider = serviceProvider,
            });
        }

        internal async Task<LoadContext<TPlugin>> RemoveAsync(Type implementationType, CancellationToken cancellationToken = default)
        {
            KeyValuePair<LoadContext<TPlugin>, ConcurrentBag<Type>> entry = _registry
                // Filter to entries that contain the implementation type
                .Where((KeyValuePair<LoadContext<TPlugin>, ConcurrentBag<Type>> entry) => entry.Value.Contains(implementationType))
                // Get the only entry or default
                .SingleOrDefault();

            // Remove the load context from the registry
            _registry.Remove(entry.Key, out ConcurrentBag<Type>? removedTypes);

            // Get the service provider for the implementation type
            IServiceProvider serviceProvider = GetProvider(implementationType);

            // Invoke plugin unload event
            OnPluginUnloaded(new PluginUnloadedEventArgs<TPlugin>
            {
                ImplementationType = implementationType,
                ServiceProvider = serviceProvider,
            });

            // Unload the load context
            entry.Key.Unload();

            // Return the unloaded load context
            return entry.Key;
        }

        private void EnsureDirectoryExists(DirectoryInfo directoryInfo, Boolean createIfNotExists = true)
        {
            // If the directory does not exist
            if (directoryInfo.Exists is false)
            {
                // Ensure the log level is enabled
                if (_logger.IsEnabled(LogLevel.Debug))
                    // Log
                    _logger.Log(LogLevel.Debug, "Directory does not exist: {path}", directoryInfo.FullName);

                // If the signal to create the directory was provided
                if (createIfNotExists)
                {
                    // Create the directory
                    directoryInfo.Create();

                    // Ensure the log level is enabled
                    if (_logger.IsEnabled(LogLevel.Debug))
                        // Log
                        _logger.Log(LogLevel.Debug, "Directory has been created: {path}", directoryInfo.FullName);
                }

                // Otherwise
                else throw new DirectoryNotFoundException();
            }
        }
    }
}
