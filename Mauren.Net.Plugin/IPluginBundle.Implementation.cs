using System;
using System.Collections.Generic;
using System.Runtime.Loader;

namespace Mauren.Net.Plugin
{
    public class PluginBundle<TPlugin> : IPluginBundle<TPlugin>
    {
        /// <inheritdoc/>
        public String Id { get; set; }

        /// <inheritdoc/>
        public IEnumerable<Type> Types { get; set; }

        /// <inheritdoc/>
        public AssemblyLoadContext Context { get; set; }

        /// <inheritdoc/>
        public IServiceProvider Provider { get; set; }

        public PluginBundle(String id, AssemblyLoadContext context, IServiceProvider provider, IEnumerable<Type> types)
        {
            Id = id;
            Context = context;
            Provider = provider;
            Types = types;
        }

        public PluginBundle(AssemblyLoadContext context, IServiceProvider provider, IEnumerable<Type> types)
        {
            Id = context.Name ?? throw new ArgumentException($"The {nameof(context.Name)} property cannot be null", nameof(context));
            Context = context;
            Provider = provider;
            Types = types;
        }
    }
}
