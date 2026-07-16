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
        private readonly IConfiguration _configuration;
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

        public DirectoryInfo Directory
        {
            get
            {
                String path = _configuration.GetValue<String>("PluginPath", "./Plugins");
                DirectoryInfo directory = new(path);
                if (directory.Exists is false)
                {
                    _logger.LogWarning($"Specified plugin directory '{path}' does not exist");
                    directory.Create();
                    _logger.LogInformation($"Created plugin directory '{path}'");
                }
                return directory;
            }
        }

        public Loader(ILogger<Loader<TPlugin>> logger, IConfiguration configuration, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _configuration = configuration;
            _serviceProvider = serviceProvider;
            _registry = new();
        }

        public async Task ScanAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Beginning scan of plugin directory '{directory}'", Directory.FullName);
            IEnumerable<FileInfo> files = Directory.EnumerateFiles("*.dll", SearchOption.AllDirectories);

            // Iterate through the plugin assembly files and load them into their own LoadContext
            foreach (FileInfo file in files)
            {
                // If the file's directory name is null, throw an exception
                if (file.DirectoryName is not String directoryName)
                {
                    // This should never happen, but just in case
                    _logger.LogWarning("Directory name for file '{path}' is null", file.FullName);
                    // Skip this file and continue
                    continue;
                }

                // Create a new LoadContext for the plugin assembly
                LoadContext<TPlugin> loadContext = new(directoryName);

                // Initialize assembly variable
                Assembly assembly;
                try
                {
                    // Load the assembly from the file path
                    assembly = loadContext.LoadFromAssemblyPath(file.FullName);
                }
                catch (BadImageFormatException exception)
                {
                    // Unload the LoadContext
                    loadContext?.Unload();
                    // Log the exception and continue with the next file
                    _logger.LogError(exception, "Failed to load plugin assembly '{path}'", file.FullName);
                    // Skip this file and continue
                    continue;
                }

                // Initialize implementations variable
                IEnumerable<Type> implementationTypes;
                try
                {
                    // Determine implementation types
                    implementationTypes = assembly.GetTypes()
                        // Filter to types implementing the plugin interface
                        .Where((Type type) => typeof(TPlugin).IsAssignableFrom(type))
                        // Filter to non-abstract classes
                        .Where((Type type) => type.IsClass && !type.IsAbstract);
                }
                catch (ReflectionTypeLoadException exception)
                {
                    // Unload the LoadContext
                    loadContext?.Unload();
                    // Log the exception and continue with the next file
                    _logger.LogError(exception, "Failed to load types from assembly '{path}'", assembly.FullName);
                    // Skip this file and continue
                    continue;
                }

                // Iterate over found implementation types
                foreach (Type implementationType in implementationTypes)
                {
                    // Add the implementation type
                    await AddAsync(loadContext, implementationType, cancellationToken);
                }
            }

            _logger.LogInformation("Completed scan of plugin directory '{directory}': Found {count}", Directory.FullName, _registry.Count);
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
    }
}
