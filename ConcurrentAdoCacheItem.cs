// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ConcurrentAdoCasheItem.cs" company="">
//   
// </copyright>
// <summary>
//   Concurrent ado cashe item.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using AdoCache.Attributes;

namespace AdoCache
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Data;
    using System.Data.SqlClient;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Threading;

    using AdoCache.Attributes;

    /// <summary>
    /// Concurrent ado cashe item.
    /// </summary>
    /// <typeparam name="TEntity">
    /// </typeparam>
    public class ConcurrentAdoCacheItem<TEntity> : AdoCacheItem<TEntity>
        where TEntity : AdoCacheEntity, new()
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConcurrentAdoCacheItem{TEntity}"/> class.
        /// </summary>
        /// <param name="connectionString">
        /// Connection string to data base.
        /// </param>
        /// <param name="options"></param>
        internal ConcurrentAdoCacheItem(string connectionString, AdoCacheItemOptions options) : base(connectionString, options)
        {

        }

        #region Properties

        /// <inheritdoc />
        public override List<TEntity> Entities
        {
            get
            {
                this.Padlock.EnterReadLock();
                try
                {
                    return this._entities.ToList();
                }
                finally
                {
                    this.Padlock.ExitReadLock();
                }
            }
        }

        #endregion

        #region Fields

        /// <summary>
        ///     Lock for multi-thread operations;
        /// </summary>
        protected ReaderWriterLockSlim Padlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

        #endregion

        #region Methods

        /// <inheritdoc />
        public override void BuildDictionary(string nameOfColumn)
        {
            this.Padlock.EnterWriteLock();
            try
            {
                base.BuildDictionary(nameOfColumn);
            }
            finally
            {
                this.Padlock.ExitWriteLock();
            }
        }

        /// <inheritdoc />
        public override void BuildIndex(string nameOfColumn)
        {
            this.Padlock.EnterWriteLock();
            try
            {
                base.BuildIndex(nameOfColumn);
            }
            finally
            {
                this.Padlock.ExitWriteLock();
            }
        }

        /// <inheritdoc />
        public override void Delete(TEntity entity)
        {
            this.Padlock.EnterWriteLock();
            try
            {
                base.Delete(entity);
            }
            finally
            {
                this.Padlock.ExitWriteLock();
            }
        }

        /// <inheritdoc />
        public override List<TEntity> FindInIndex(string nameOfColumn, object value)
        {
            this.Padlock.EnterReadLock();
            try
            {
                return base.FindInIndex(nameOfColumn, value);
            }
            finally
            {
                this.Padlock.ExitReadLock();
            }
        }

        /// <inheritdoc />
        public override TEntity FindInDictionary(string nameOfColumn, object value)
        {
            this.Padlock.EnterReadLock();
            try
            {
                return base.FindInDictionary(nameOfColumn, value);
            }
            finally
            {
                this.Padlock.ExitReadLock();
            }
        }

        /// <inheritdoc />
        public override ConcurrentDictionary<object, List<TEntity>> GetIndex(string nameOfColumn)
        {
            this.Padlock.EnterReadLock();
            try
            {
                return new ConcurrentDictionary<object, List<TEntity>>(base.GetIndex(nameOfColumn).ToDictionary(k => k.Key, v => v.Value.ToList()));
            }
            finally
            {
             this.Padlock.ExitReadLock();   
            }
        }

        /// <inheritdoc />
        public override ConcurrentDictionary<object, TEntity> GetDictionary(string nameOfColumn)
        {
            this.Padlock.EnterReadLock();
            try
            {
                return base.GetDictionary(nameOfColumn);
            }
            finally
            {
                this.Padlock.ExitReadLock();
            }
        }

        /// <inheritdoc />
        public override TEntity Insert(TEntity entity)
        {
            this.Padlock.EnterWriteLock();
            try
            {
                return base.Insert(entity);
            }
            finally
            {
                this.Padlock.ExitWriteLock();
            }
        }

        /// <inheritdoc />
        public override TEntity Update(TEntity entity)
        {
            this.Padlock.EnterWriteLock();
            try
            {
                return base.Update(entity);
            }
            finally
            {
                this.Padlock.ExitWriteLock();
            }
        }

        #endregion


    }
}