using Mauren.Net.Plugin;
using System.Collections.Generic;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPluginLoader<TContract, TStrategy>(this IServiceCollection services)
            where TStrategy : class, IServiceRegistrationStrategy
        {
            // Register the plugin loader as a singleton
            services.AddSingleton<ILoader<TContract>, Loader<TContract>>();

            // Register the service registration strategy
            services.AddTransient<IServiceRegistrationStrategy, TStrategy>();

            // Register enumerable of host container service descriptors
            services.AddSingleton<IEnumerable<ServiceDescriptor>>(services);

            // Return the service collection for chaining
            return services;
        }
    }
}
