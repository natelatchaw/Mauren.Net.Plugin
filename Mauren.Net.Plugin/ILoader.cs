using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Mauren.Net.Plugin
{
    /// <summary>
    /// Defines a loader that discovers, loads, and initializes plugin types 
    /// implementing the specified contract.
    /// </summary>
    /// 
    /// <typeparam name="TContract">
    /// The contract type used to identify plugin classes. All types implementing
    /// this contract will be discovered and loaded.
    /// </typeparam>
    public interface ILoader<TContract>
    {
        /// <summary>
        /// Fired when a <typeparamref name="TContract"/> implementation has been loaded.
        /// </summary>
        public event EventHandler<IPluginBundle<TContract>>? PluginLoaded;

        /// <summary>
        /// Fired when a <typeparamref name="TContract"/> implementation has been unloaded.
        /// </summary>
        public event EventHandler<IPluginBundle<TContract>>? PluginUnloaded;

        /// <summary>
        /// Scans the <see cref="Directory"/> for <typeparamref name="TContract"/> implementations.
        /// </summary>
        /// 
        /// <param name="directory">
        /// The directory to scan for <typeparamref name="TContract"/> implementations.
        /// </param>
        /// 
        /// <param name="cancellationToken">
        /// </param>
        /// 
        /// <returns>
        /// </returns>
        public Task ScanAsync(DirectoryInfo directory, CancellationToken cancellationToken = default);
    }
}
