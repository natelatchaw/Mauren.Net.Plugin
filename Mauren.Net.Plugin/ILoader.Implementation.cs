using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

namespace Mauren.Net.Plugin
{
    public class Loader<TPlugin> : ILoader<TPlugin>
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

        internal async Task<IServiceProvider> RegisterAsync(Type implementationType, CancellationToken cancellationToken = default)
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
                    // Get a matching dependency from the host application's service provider
                    Object dependency = _serviceProvider.GetRequiredService(parameter.ParameterType);

                    // Add the dependency as a singleton implementation of the parameter type
                    services.AddSingleton(parameter.ParameterType, dependency);
                }
            }

            // Register the plugin
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
            IServiceProvider serviceProvider = await RegisterAsync(implementationType, cancellationToken);
            // Initialize event arguments
            PluginLoadedEventArgs<TPlugin> eventArgs = new()
            {
                ImplementationType = implementationType,
                ServiceProvider = serviceProvider,
            };
            // Invoke plugin load event
            OnPluginLoaded(eventArgs);
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
            IServiceProvider serviceProvider = await RegisterAsync(implementationType, cancellationToken);
            // Initialize event arguments
            PluginUnloadedEventArgs<TPlugin> eventArgs = new()
            {
                ImplementationType = implementationType,
                ServiceProvider = serviceProvider,
            };
            // Invoke plugin unload event
            OnPluginUnloaded(eventArgs);

            // Unload the load context
            entry.Key.Unload();

            // Return the unloaded load context
            return entry.Key;
        }
    }
}
