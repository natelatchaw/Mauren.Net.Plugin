using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mauren.Net.Plugin
{
    internal class PluginHostedServiceManager<TContract> : BackgroundService
    {
        private readonly ILogger<PluginHostedServiceManager<TContract>> _logger;
        private readonly IServiceProvider _hostProvider;
        private readonly ILoader<TContract> _loader;

        private readonly ConcurrentDictionary<String, IEnumerable<IHostedService>> _runningHostedServices;
        private readonly TaskCompletionSource<Object> _taskCompletionSource;

        public PluginHostedServiceManager(ILogger<PluginHostedServiceManager<TContract>> logger, IServiceProvider hostProvider, ILoader<TContract> loader)
        {
            _logger = logger;
            _hostProvider = hostProvider;
            _loader = loader;

            _runningHostedServices = new();
            _taskCompletionSource = new();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _loader.PluginLoaded += OnPluginLoaded;
            _loader.PluginUnloaded += OnPluginUnloaded;

            // Get the host application lifetime from the host provider
            IHostApplicationLifetime? hostLifetime = _hostProvider.GetService<IHostApplicationLifetime>();
            // Register the StopAll method on application stop
            hostLifetime?.ApplicationStopping.Register(StopAll);

            // Register a cleanup callback for on cancellation
            using CancellationTokenRegistration register = stoppingToken.Register(() => _taskCompletionSource.TrySetResult(true));

            // Await the task completion source
            await _taskCompletionSource.Task;

            _loader.PluginLoaded -= OnPluginLoaded;
            _loader.PluginUnloaded -= OnPluginUnloaded;
        }

        private void OnPluginLoaded(Object? sender, IPluginBundle<TContract> e)
        {
            Run(e);
        }

        private void OnPluginUnloaded(Object? sender, IPluginBundle<TContract> e)
        {
            Stop(e);
        }

        public void Run(IPluginBundle<TContract> bundle)
        {
            // Iterate over the IHostedService types contained by the bundle
            try
            {
                // Get all hosted service instances from the bundle's service provider
                IEnumerable<IHostedService> hostedServiceInstances = bundle.Provider.GetServices<IHostedService>();

                // Iterate over the hosted service instances
                foreach (IHostedService hostedServiceInstance in hostedServiceInstances)
                {
                    // Run on the thread pool and immediately return
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Start the hosted service instance
                            await hostedServiceInstance.StartAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            // Ensure log level is enabled
                            if (!_logger.IsEnabled(LogLevel.Error))
                                // Log exception
                                _logger.Log(LogLevel.Error, exception, "Plugin hosted service {type} failed to start", hostedServiceInstance.GetType().Name);
                        }
                    });
                }

                // Store running hosted services with the bundle Id
                _runningHostedServices.TryAdd(bundle.Id, hostedServiceInstances);
            }
            catch (Exception exception)
            {
                // Ensure log level is enabled
                if (_logger.IsEnabled(LogLevel.Debug))
                    // Log exception
                    _logger.Log(LogLevel.Debug, exception, "Could not resolve/start hosted services from plugin {name}", bundle.Id);

            }
        }

        public void Stop(IPluginBundle<TContract> bundle)
        {
            // If no hosted service instances were found for the provided bundle
            if (_runningHostedServices.TryRemove(bundle.Id, out IEnumerable<IHostedService>? hostedServiceInstances) is false)
            {
                // Ensure log level is enabled
                if (_logger.IsEnabled(LogLevel.Debug))
                    // Log status
                    _logger.Log(LogLevel.Debug, "No hosted services running for plugin {name}", bundle.Id);

                // Short-circuit
                return;
            }

            // Iterate over the found hosted service instances
            foreach (IHostedService hostedServiceInstance in hostedServiceInstances)
            {
                try
                {
                    // Stop the hosted service instance
                    hostedServiceInstance.StopAsync(CancellationToken.None).GetAwaiter().GetResult();

                    // If the hosted service instance is disposable
                    if (hostedServiceInstance is IDisposable disposableServiceInstance)
                        // Dispose the hosted service instance
                        disposableServiceInstance.Dispose();
                }
                catch (Exception exception)
                {
                    // Ensure log level is enabled
                    if (_logger.IsEnabled(LogLevel.Debug))
                        // Log exception
                        _logger.Log(LogLevel.Debug, exception, "Could not resolve/stop hosted service {type} from plugin {name}", hostedServiceInstance.GetType().Name, bundle.Id);
                }
            }
        }

        public void StopAll()
        {
            // Iterate over all running hosted service groups
            foreach ((String id, IEnumerable<IHostedService> hostedServiceInstances) in _runningHostedServices)
            {
                // Iterate over all running hosted service instances
                foreach (IHostedService hostedServiceInstance in hostedServiceInstances)
                {
                    try
                    {
                        // Stop the hosted service instance
                        hostedServiceInstance.StopAsync(CancellationToken.None).GetAwaiter().GetResult();

                        // If the hosted service instance is disposable
                        if (hostedServiceInstance is IDisposable disposableServiceInstance)
                            // Dispose the hosted service instance
                            disposableServiceInstance.Dispose();
                    }
                    catch (Exception exception)
                    {
                        // Ensure log level is enabled
                        if (_logger.IsEnabled(LogLevel.Debug))
                            // Log exception
                            _logger.Log(LogLevel.Debug, exception, "Could not resolve/stop hosted service {type} from plugin {name}", hostedServiceInstance.GetType().Name, id);
                    }
                }
            }
        }
    }
}
