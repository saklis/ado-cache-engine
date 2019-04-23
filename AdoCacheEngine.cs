using System;
using System.Collections.Concurrent;

namespace AdoCache {
    /// <summary>
    ///     Allows creating cashed collections of objects inherited from AdoCacheEntity class.
    /// </summary>
    public class AdoCacheEngine {
        /// <summary>
        ///     Dictionary of items that stores cache items.
        /// </summary>
        private readonly ConcurrentDictionary<Type, dynamic> _items = new ConcurrentDictionary<Type, dynamic>();

        /// <summary>
        ///     Data base connection string.
        /// </summary>
        protected readonly string ConnectionString;

        /// <summary>
        ///     Create new instance of Cache Engine.
        /// </summary>
        /// <param name="connectionString">Connection string to data base for items to be cashed.</param>
        public AdoCacheEngine(string connectionString) {
            ConnectionString = connectionString;
        }

        /// <summary>
        ///     Create cached item.
        /// </summary>
        /// <typeparam name="TEntity">Type inheriting from AdoCacheEntity which will be cashed.</typeparam>
        /// <returns>
        ///     Created cache item.
        /// </returns>
        public AdoCacheItem<TEntity> CreateCache<TEntity>() where TEntity : AdoCacheEntity, new() {
            return CreateCache<TEntity>(null);
        }

        /// <summary>
        ///     Create cache item.
        /// </summary>
        /// <typeparam name="TEntity">Type inheriting from AdoCacheEntity which will be cashed.</typeparam>
        /// <param name="options">An object that contains configuration for Cache Item instance.</param>
        /// <returns>
        ///     Created cache item.
        /// </returns>
        public AdoCacheItem<TEntity> CreateCache<TEntity>(AdoCacheItemOptions options) where TEntity : AdoCacheEntity, new() {
            AdoCacheItem<TEntity> item = CreateItem<TEntity>(options);
            if (!_items.TryAdd(typeof(TEntity), item)) throw new InvalidOperationException($"Cache for type {typeof(TEntity)} already exists. Drop existing cache before trying to create new one.");

            return item;
        }

        /// <summary>
        ///     Wrapper for inheritance purposes.
        /// </summary>
        /// <typeparam name="TEntity">Type inheriting from AdoCacheEntity which will be cashed.</typeparam>
        /// <param name="options">An object that contains configuration for Cache Item instance.</param>
        /// <returns>Created cache item.</returns>
        protected virtual AdoCacheItem<TEntity> CreateItem<TEntity>(AdoCacheItemOptions options) where TEntity : AdoCacheEntity, new() {
            return new AdoCacheItem<TEntity>(ConnectionString, options);
        }

        /// <summary>
        ///     Remove existing cache.
        /// </summary>
        /// <typeparam name="TEntity">Cashed type inherited from AdoCacheEntity.</typeparam>
        public void DropCache<TEntity>() where TEntity : AdoCacheEntity, new() {
            Item<TEntity>().Unload();
        }

        /// <summary>
        ///     Check if cache exists for the type.
        /// </summary>
        /// <typeparam name="TEntity">Type inheriting from AdoCacheEntity.</typeparam>
        /// <returns>True if cache for the type exists. False otherwise.</returns>
        public bool IsCacheCreated<TEntity>() where TEntity : AdoCacheEntity, new() {
            if (!_items.ContainsKey(typeof(TEntity))) return false;
            return _items[typeof(TEntity)] != null;
        }

        /// <summary>
        ///     Removes data from Cashe and fill with new _entities. Existing indexes and dictionaries will be recreated.
        /// </summary>
        /// <typeparam name="TEntity"></typeparam>
        public void ReCreateCache<TEntity>() where TEntity : AdoCacheEntity, new() {
            Item<TEntity>().Reload();
        }

        /// <summary>
        ///     Get cached item.
        /// </summary>
        /// <typeparam name="TEntity">Cached type inheriting from AdoCacheEntity.</typeparam>
        /// <returns>Cached item.</returns>
        public AdoCacheItem<TEntity> Item<TEntity>() where TEntity : AdoCacheEntity, new() {
            if (_items.ContainsKey(typeof(TEntity))) return _items[typeof(TEntity)] ?? throw new InvalidOperationException($"Cache for type {typeof(TEntity)} does not exist. Create cache using CreateCache() method before trying to use it.");
            throw new InvalidOperationException($"Cache for type {typeof(TEntity)} does not exist. Create cache using CreateCache() method before trying to use it.");
        }
    }
}