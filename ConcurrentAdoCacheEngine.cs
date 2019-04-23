// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ConcurrentAdoCacheEngine.cs" company="">
//   
// </copyright>
// <summary>
//   Concurrent ado cache engine.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace AdoCache
{
    using System;
    using System.Collections.Concurrent;

    /// <summary>
    /// Concurrent ado cache engine.
    /// </summary>
    public class ConcurrentAdoCacheEngine : AdoCacheEngine
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConcurrentAdoCacheEngine"/> class.
        /// </summary>
        /// <param name="connectionString">
        /// Connection string.
        /// </param>
        public ConcurrentAdoCacheEngine(string connectionString) : base(connectionString)
        {
        }

        /// <inheritdoc />
        protected override AdoCacheItem<TEntity> CreateItem<TEntity>(AdoCacheItemOptions options)
        {
            return new ConcurrentAdoCacheItem<TEntity>(base.ConnectionString, options);
        }
    }
}