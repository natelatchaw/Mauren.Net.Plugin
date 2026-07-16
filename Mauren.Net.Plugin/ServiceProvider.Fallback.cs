using Microsoft.Extensions.DependencyInjection;

namespace Mauren.Net.Plugin
{
    internal class FallbackServiceProvider : IServiceProvider, ISupportRequiredService
    {
        private readonly IServiceProvider _primaryProvider;
        private readonly IServiceProvider _fallbackProvider;

        internal FallbackServiceProvider(IServiceProvider primaryProvider, IServiceProvider fallbackProvider)
        {
            ArgumentNullException.ThrowIfNull(primaryProvider);
            _primaryProvider = primaryProvider;

            ArgumentNullException.ThrowIfNull(fallbackProvider);
            _fallbackProvider = fallbackProvider;
        }

        Object? IServiceProvider.GetService(Type serviceType)
        {
            // Intercept scope factory requests
            if (serviceType == typeof(IServiceScopeFactory))
                return new FallbackServiceScopeFactory(_primaryProvider, _fallbackProvider);

            // Try to resolve with the primary provider
            if (_primaryProvider.GetService(serviceType) is Object primary)
                return primary;

            // Try to resolve with the fallback provider
            if (_fallbackProvider.GetService(serviceType) is Object fallback)
                return fallback;

            // Not found, return null
            return null;
        }

        Object ISupportRequiredService.GetRequiredService(Type serviceType)
        {
            // Get this instance as an IServiceProvider (explicit GetService implementation)
            IServiceProvider provider = this as IServiceProvider;

            // Try to resolve with the provider
            if (provider.GetService(serviceType) is Object service)
                return service;

            // Not found, throw exception
            throw new InvalidOperationException($"No service for type '{serviceType}' has been registered.");
        }
    }

    internal class FallbackServiceScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _primary;
        private readonly IServiceProvider _fallback;

        internal FallbackServiceScopeFactory(IServiceProvider primary, IServiceProvider fallback)
        {
            _primary = primary;
            _fallback = fallback;
        }

        IServiceScope IServiceScopeFactory.CreateScope()
        {
            // Get parent instance as an IServiceProvider (explicit GetService implementation)
            IServiceProvider primary = _primary as IServiceProvider;
            // Get a service scope factory from the primary provider
            if (primary.GetService(typeof(IServiceScopeFactory)) is not IServiceScopeFactory primaryScopeFactory)
                throw new InvalidOperationException("Primary provider missing IServiceScopeFactory");
            // Create a primary scope from the factory
            IServiceScope primaryScope = primaryScopeFactory.CreateScope();


            // Get fallback instance as an IServiceProvider (explicit GetService implementation)
            IServiceProvider fallback = _fallback as IServiceProvider;
            // Get a service scope factory from the fallback provider
            if (fallback.GetService(typeof(IServiceScopeFactory)) is not IServiceScopeFactory fallbackScopeFactory)
                throw new InvalidOperationException("Fallback provider missing IServiceScopeFactory");
            // Create a fallback scope from the factory
            IServiceScope fallbackScope = fallbackScopeFactory.CreateScope();

            // Create a new combined provider tied to the scoped primary provider
            FallbackServiceProvider combinedScopedProvider = new(primaryScope.ServiceProvider, fallbackScope.ServiceProvider);

            return new FallbackServiceScope(primaryScope, combinedScopedProvider);
        }
    }

    internal class FallbackServiceScope : IServiceScope
    {
        private readonly IServiceScope _primaryScope;
        public IServiceProvider ServiceProvider { get; }

        public FallbackServiceScope(IServiceScope primaryScope, IServiceProvider combinedProvider)
        {
            _primaryScope = primaryScope;
            ServiceProvider = combinedProvider;
        }

        void IDisposable.Dispose()
        {
            // Dispose the primary scope
            _primaryScope.Dispose();
        }
    }
}
