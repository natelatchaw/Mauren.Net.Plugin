using Microsoft.Extensions.DependencyInjection;

namespace Mauren.Net.Plugin
{
    /// <summary>
    /// Represents a contract for registering services defined in a plugin assembly.
    /// </summary>
    public interface IServiceManifest
    {
        /// <summary>
        /// Registers applicable services to the provided <paramref name="services"/> value.
        /// </summary>
        /// 
        /// <param name="services">
        /// An <see cref="IServiceCollection"/> in which to register services.
        /// </param>
        void RegisterServices(IServiceCollection services);
    }
}
