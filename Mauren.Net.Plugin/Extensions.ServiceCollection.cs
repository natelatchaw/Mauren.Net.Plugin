using Mauren.Net.Plugin;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPluginLoader<TPlugin>(this IServiceCollection services)
        {
            // Register the plugin loader as a singleton
            services.AddSingleton<ILoader<TPlugin>, Loader<TPlugin>>();

            // Return the service collection for chaining
            return services;
        }
    }
}
