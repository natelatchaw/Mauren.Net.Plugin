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
    public partial class Loader<TContract> : ILoader<TContract>
    {
        private readonly ILogger<Loader<TContract>> _logger;
        private readonly IServiceRegistrationStrategy _strategy;
        private readonly IEnumerable<ServiceDescriptor> _hostDescriptors;

        private readonly ConcurrentDictionary<String, IPluginBundle<TContract>> _registry;

        public event EventHandler<IPluginBundle<TContract>>? PluginLoaded;
        public event EventHandler<IPluginBundle<TContract>>? PluginUnloaded;

        public Loader(ILogger<Loader<TContract>> logger, IServiceRegistrationStrategy strategy, IEnumerable<ServiceDescriptor> hostDescriptors)
        {
            _logger = logger;
            _strategy = strategy;
            _hostDescriptors = hostDescriptors;

            _registry = new();
        }

        public async Task ScanAsync(DirectoryInfo directory, CancellationToken cancellationToken = default)
        {
            // Ensure the provided directory exists
            EnsureDirectoryExists(directory, createIfNotExists: true);

            // Ensure the log level is enabled
            if (_logger.IsEnabled(LogLevel.Information))
                // Log information
                _logger.Log(LogLevel.Information, "Directory scan starting for '{path}': Discovering {type}", directory.FullName, typeof(TContract).Name);

            // Enumerate files in directory
            IEnumerable<FileInfo> files = directory.EnumerateFiles("*.dll", SearchOption.AllDirectories);

            // Iterate through the plugin assembly files and load them into their own LoadContext
            foreach (FileInfo file in files)
            {
                try
                {
                    // Bundle the assembly referenced by the file info
                    IPluginBundle<TContract> bundle = Bundle(file, _hostDescriptors);

                    // Register the bundle
                    Register(bundle.Id, bundle);

                    // Ensure log level is enabled
                    if (_logger.IsEnabled(LogLevel.Debug))
                        // Log message
                        _logger.Log(LogLevel.Debug, "Bundled {file} to bundle {id}", file.Name, bundle.Id ?? "NO_BUNDLE_ID");
                }
                catch (Exception exception)
                {
                    // Ensure log level is enabled
                    if (_logger.IsEnabled(LogLevel.Warning))
                        // Log exception
                        _logger.Log(LogLevel.Warning,/*exception,*/"{message}", exception.Message);
                }
            }

            // Ensure the log level is enabled
            if (_logger.IsEnabled(LogLevel.Information))
                // Log result
                _logger.Log(LogLevel.Information, "Directory scan complete for '{path}': Discovered {type} ({count})", directory.FullName, typeof(TContract).Name, _registry.Count);
        }

        void EnsureDirectoryExists(DirectoryInfo directoryInfo, Boolean createIfNotExists = true)
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

        /// <summary>
        /// Bundles the provided <paramref name="fileInfo">file</paramref>'s <see cref="Assembly"/>
        /// as a plugin bundle.
        /// </summary>
        /// 
        /// <param name="fileInfo">
        /// Metadata representing the file to load.
        /// </param>
        /// 
        /// <param name="serviceDescriptors">
        /// An <see cref="IEnumerable{T}"/> of <see cref="ServiceDescriptor"/>s to be included in
        /// the <see cref="IPluginBundle{TContract}"/> instance's service provider.
        /// </param>
        /// 
        /// <returns></returns>
        IPluginBundle<TContract> Bundle(FileInfo fileInfo, IEnumerable<ServiceDescriptor>? serviceDescriptors = default)
        {
            // Initialize an assembly load context
            AssemblyLoadContext? assemblyLoadContext = null;

            try
            {
                // Load the file into an assembly load context
                assemblyLoadContext = Load(fileInfo);

                // Create a service collection or the assembly load context
                IServiceCollection services = GetServices(assemblyLoadContext, _strategy);

                // Discover all types loaded by the assembly load context
                IEnumerable<Type> types = GetTypes(assemblyLoadContext);

                // Iterate over discovered types
                foreach (Type type in types)
                {
                    // Add the discovered type to the service collection
                    services.AddTransient(type);
                }

                // Iterate over provided service descriptors (if provided)
                foreach (ServiceDescriptor serviceDescriptor in serviceDescriptors ?? [])
                {
                    // Add the service descriptor to the service collection
                    services.Add(serviceDescriptor);
                }

                // Build the service provider
                ServiceProvider serviceProvider = services.BuildServiceProvider();

                // Initialize the plugin bundle
                IPluginBundle<TContract> bundle = new PluginBundle<TContract>(assemblyLoadContext, serviceProvider, types);

                // Return the bundle
                return bundle;
            }
            catch (Exception exception)
            {
                // Unload the assembly load context, if initialized
                assemblyLoadContext?.Unload();

                // Rethrow exception
                throw new Exception($"Plugin bundling failed for {fileInfo.Name}", exception);
            }
        }

        /// <summary>
        /// Loads an <see cref="Assembly"/> file into an <see cref="AssemblyLoadContext"/>.
        /// </summary>
        /// 
        /// <param name="fileInfo">
        /// Metadata representing the file to load.
        /// </param>
        /// 
        /// <returns>
        /// An <see cref="AssemblyLoadContext"/> containing the <see cref="Assembly"/>
        /// loaded from the provided <paramref name="fileInfo"/>.
        /// </returns>
        /// 
        /// <exception cref="Exception"></exception>
        AssemblyLoadContext Load(FileInfo fileInfo)
        {
            // Initialize variable for the assembly load context
            AssemblyLoadContext? assemblyLoadContext = null;

            try
            {
                // If the file's directory is null
                if (fileInfo.Directory is not DirectoryInfo directoryInfo)
                    // Throw exception
                    throw new DirectoryNotFoundException($"Could not determine directory for {fileInfo.Name}");

                // Initialize an assembly load context for the file's directory
                assemblyLoadContext = new LoadContext<TContract>(directoryInfo.FullName);

                // Load the file as an assembly
                assemblyLoadContext.LoadFromAssemblyPath(fileInfo.FullName);

                // Return the assembly load context
                return assemblyLoadContext;
            }
            catch (Exception exception)
            {
                // Unload the assembly load context
                assemblyLoadContext?.Unload();

                // Encapsulate exception and throw
                throw new Exception($"{nameof(Assembly)} load failed for {fileInfo.Name}", exception);
            }
        }

        /// <summary>
        /// Discovers all <see cref="Type"/>s assignable to <typeparamref name="TContract"/>
        /// in the provided <paramref name="assemblyLoadContext"/>.
        /// </summary>
        /// 
        /// <param name="assemblyLoadContext">
        /// The <see cref="AssemblyLoadContext"/> from which to discover types.
        /// </param>
        /// 
        /// <returns>
        /// An <see cref="IEnumerable{T}"/> of <see cref="Type"/>s assignable to
        /// <typeparamref name="TContract"/> contained by the <paramref name="assemblyLoadContext"/>.
        /// </returns>
        /// 
        /// <exception cref="Exception"></exception>
        IEnumerable<Type> GetTypes(AssemblyLoadContext assemblyLoadContext)
        {
            try
            {
                // Get all types visible from outside the assemblies
                IEnumerable<Type> exportedTypes = assemblyLoadContext.Assemblies
                    // Select exported types from each assembly
                    .SelectMany((Assembly assembly) => assembly.GetExportedTypes());

                // Filter the exported types to types relevant to the loader
                IEnumerable<Type> filteredTypes = exportedTypes
                    // Filter to types implementing the generic type parameter
                    .Where((Type type) => typeof(TContract).IsAssignableFrom(type))
                    // Filter to non-abstract classes
                    .Where((Type type) => type.IsClass && !type.IsAbstract);

                // If no relevant types were discovered
                if (filteredTypes.Any() is false)
                    // Throw exception
                    throw new InvalidOperationException($"No {typeof(Type).Name}s assignable to {typeof(TContract).Name} were discovered");

                // Return the discovered types
                return filteredTypes;
            }
            catch (Exception exception)
            {
                // Encapsulate exception and throw
                throw new Exception($"{typeof(TContract).Name} discovery failed for {assemblyLoadContext.Name}", exception);
            }
        }

        /// <summary>
        /// Discovers services defined in the <paramref name="assemblyLoadContext"/>
        /// via the provided <typeparamref name="TStrategy"/> <paramref name="strategy"/>.
        /// </summary>
        /// 
        /// <typeparam name="TStrategy">
        /// A type implementing a strategy for discovering services.
        /// </typeparam>
        /// 
        /// <param name="assemblyLoadContext">
        /// An assembly load context in which to discover services.
        /// </param>
        /// 
        /// <param name="strategy">
        /// A <typeparamref name="TStrategy"/> instance to discover services with.
        /// </param>
        /// 
        /// <returns>
        /// An <see cref="IServiceCollection"/> containing discovered services.
        /// </returns>
        /// 
        /// <exception cref="Exception"></exception>
        IServiceCollection GetServices<TStrategy>(AssemblyLoadContext assemblyLoadContext, TStrategy strategy) where TStrategy : IServiceRegistrationStrategy
        {
            try
            {
                // Initialize a service collection
                IServiceCollection services = new ServiceCollection();

                // Get all types visible from outside the assemblies
                IEnumerable<Type> exportedTypes = assemblyLoadContext.Assemblies
                    // Select exported types from each assembly
                    .SelectMany((Assembly assembly) => assembly.GetExportedTypes());

                // Filter the exported types to types relevant to the registration strategy
                IEnumerable<Type> filteredTypes = exportedTypes
                    // Filter to types meeting the strategy's criteria predicate
                    .Where(strategy.ProvidesServices.Invoke);

                // Iterate over each type containing registration logic
                foreach (Type type in filteredTypes)
                {
                    // Guard against exceptions thrown during registration
                    try
                    {
                        // Apply the type's registration logic
                        strategy.Apply(type, services);
                    }
                    catch (Exception exception)
                    {
                        // Ensure the log level is enabled
                        if (_logger.IsEnabled(LogLevel.Warning))
                            // Log exception
                            _logger.Log(LogLevel.Warning, exception, "An exception occurred in {type} during service registration", type.FullName);
                    }
                }

                // Return the service collection
                return services;
            }
            catch (Exception exception)
            {
                // Encapsulate exception and throw
                throw new Exception($"Service registration failed for {assemblyLoadContext.Name}", exception);
            }
        }

        void Register(String id, IPluginBundle<TContract> bundle)
        {
            // Add or update the bundle in the registry
            _registry.AddOrUpdate(id, bundle, (String _, IPluginBundle<TContract> _) => bundle);

            // Invoke event handler
            PluginLoaded?.Invoke(this, bundle);
        }

        void Unregister(String id)
        {
            // Remove the bundle by id from the registry
            if (_registry.TryRemove(id, out IPluginBundle<TContract>? bundle) is false)
                throw new InvalidOperationException($"Could not find bundle {id} in registry");

            // Invoke event handler
            PluginUnloaded?.Invoke(this, bundle);

            // If the bundle's service provider is disposable
            if (bundle.Provider is IDisposable disposable)
                // Dispose the bundle's service provider
                disposable.Dispose();

            // Unload the bundle's context
            bundle.Context.Unload();
        }
    }
}
