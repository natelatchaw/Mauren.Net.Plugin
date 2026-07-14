using System;

namespace Mauren.Net.Plugin
{
    public class PluginUnloadedEventArgs<TPlugin> : EventArgs
    {
        public required Type ImplementationType { get; set; }
        public required IServiceProvider ServiceProvider { get; set; }
    }
}
