namespace Mauren.Net.Plugin
{
    /// <summary>
    /// Scans the specified <see cref="Directory"/> for <typeparamref name="TPlugin"/> implementations.
    /// </summary>
    /// 
    /// <typeparam name="TPlugin">
    /// The type of plugin to load.
    /// </typeparam>
    public interface ILoader<TPlugin>
    {
        /// <summary>
        /// Fired when a <typeparamref name="TPlugin"/> implementation has been loaded.
        /// </summary>
        public event EventHandler<PluginLoadedEventArgs<TPlugin>>? PluginLoaded;

        /// <summary>
        /// Fired when a <typeparamref name="TPlugin"/> implementation has been unloaded.
        /// </summary>
        public event EventHandler<PluginUnloadedEventArgs<TPlugin>>? PluginUnloaded;

        /// <summary>
        /// Scans the <see cref="Directory"/> for <typeparamref name="TPlugin"/> implementations.
        /// </summary>
        /// 
        /// <param name="directory">
        /// The directory to scan for <typeparamref name="TPlugin"/> implementations.
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
