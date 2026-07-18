using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;

namespace Mauren.Net.Plugin
{
    /// <summary>
    /// Represents a contract for a registered <see cref="Assembly"/>.
    /// </summary>
    /// 
    /// <typeparam name="TContract">
    /// The contract from which the discovered <see cref="Type"/>s conform to.
    /// </typeparam>
    public interface IPluginBundle<TContract>
    {
        /// <summary>
        /// A unique identifier for the <typeparamref name="TContract"/> <see cref="Assembly"/>.
        /// </summary>
        public String Id { get; set; }

        /// <summary>
        /// All discovered <see cref="Type"/>s implementing <typeparamref name="TContract"/>
        /// contained by the <see cref="Assembly"/>.
        /// </summary>
        public IEnumerable<Type> Types { get; set; }

        /// <summary>
        /// The context scope of the loaded <see cref="Assembly"/>.
        /// </summary>
        public AssemblyLoadContext Context { get; }

        /// <summary>
        /// A built service provider containing services dependencies of the <see cref="Assembly"/>.
        /// </summary>
        public IServiceProvider Provider { get; }
    }
}
