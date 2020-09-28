using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AdoCache.Attributes;
using AdoCache.ryanohs;
using AdoCache.Structures;

namespace AdoCache {
    /// <summary>
    ///     Cache Item holding local data and provide API for CRUD operations.
    /// </summary>
    /// <typeparam name="TEntity">Type inheriting from AdoCacheEntity.</typeparam>
    public class AdoCacheItem<TEntity> where TEntity : AdoCacheEntity, new() {
        /// <summary>
        ///     Create new cached item.
        /// </summary>
        /// <param name="connectionString">Connection string to data base.</param>
        /// <param name="options">Configuration object. Can be null.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when unable to deduce table name. [TableName] attribute in model class may be empty or missing.
        /// - OR-
        /// Thrown when TEntity class don't have any public properties.
        /// - OR -
        /// Thrown when TEntity have more than 1 property marked with [AutoIncrement].
        /// </exception>
        public AdoCacheItem(string connectionString, AdoCacheItemOptions options) {
            string tableName = string.IsNullOrWhiteSpace(options?.OverrideTableName) ? (typeof(TEntity).GetCustomAttributes(typeof(TableNameAttribute), false)[0] as TableNameAttribute)?.TableName : options.OverrideTableName;
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentException("Could not deduce table name.");
            TableName = tableName;

            _connectionString = connectionString;

            _columns = typeof(TEntity).GetProperties().Where(p => p.CanWrite).ToArray();
            FieldInfo[] newValueFields = typeof(TEntity).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Where(f => f.CustomAttributes.Any(ca => ca.AttributeType == typeof(NewValueFieldAttribute))).ToArray();
            foreach (PropertyInfo column in _columns) {
                FieldInfo field = newValueFields.Single(f => f.GetCustomAttribute<NewValueFieldAttribute>().PropertyName == column.Name);
                _newValueFields.Add(column, field);
            }

            _keyColumns           = _columns.Where(p => p.CustomAttributes.Any(ca => ca.AttributeType == typeof(KeyAttribute))).ToArray();
            _autoIncrementColumns = _columns.Where(p => p.CustomAttributes.Any(ca => ca.AttributeType == typeof(AutoIncrementAttribute))).ToArray();
            if (options != null && options.EnableReadOnlyColumnsSupport) _readOnlyColumns = _columns.Where(p => p.CustomAttributes.Any(ca => ca.AttributeType == typeof(ReadOnlyAttribute))).Where(c => !_autoIncrementColumns.Contains(c)).ToArray();

            if (_columns == null || _columns.Length == 0) throw new ArgumentException($"Type {typeof(TEntity)} do not have any public Properties. Provided type needs to have at least one public property.");

            if (_autoIncrementColumns != null && _autoIncrementColumns.Length > 1) throw new ArgumentException($"Type {typeof(TEntity)} have more than 1 column with auto-increment enabled. Only types with up to 1 auto-increment columns are supported.");

            IsReadOnly = _keyColumns == null || _keyColumns.Length == 0;
        }

        #region Properties

        /// <summary>
        ///     Name of Table in data base.
        /// </summary>
        public string TableName { get; }

        /// <summary>
        ///     Is the item in Read Only mode? In Read Only, Insert, Update and Delete operations are not allowed.
        /// </summary>
        public bool IsReadOnly { get; }

        /// <summary>
        ///     Current CultureInfo. InvariantCulture is used for query building and whatever Culture is
        ///     set here will be enabled after query was build.
        /// </summary>
        public CultureInfo CurrentCulture { get; set; } = Thread.CurrentThread.CurrentCulture;

        /// <summary>
        ///     List of locally cached _entities.
        /// </summary>
        public virtual List<TEntity> Entities => _entities;

        /// <summary>
        ///     Return if item has data loaded.
        /// </summary>
        public bool IsLoaded { get; private set; }

        #endregion

        #region Fields

        /// <summary>
        ///     Array of columns with Auto Increment enabled. Those will be skipped during Insert and Update.
        /// </summary>
        protected PropertyInfo[] _autoIncrementColumns;

        /// <summary>
        ///     Array of columns with Read-Only property set to true. Those will be skipped during Insert and Update.
        /// </summary>
        protected PropertyInfo[] _readOnlyColumns;

        /// <summary>
        ///     List of locally cached _entities.
        /// </summary>
        protected List<TEntity> _entities = new List<TEntity>();

        /// <summary>
        ///     Array of columns in the Type.
        /// </summary>
        protected PropertyInfo[] _columns;

        protected Dictionary<PropertyInfo, FieldInfo> _newValueFields = new Dictionary<PropertyInfo, FieldInfo>();

        /// <summary>
        ///     Connection string to data base.
        /// </summary>
        protected string _connectionString;

        /// <summary>
        ///     Dictionary of 'primary keys'.
        /// </summary>
        protected ConcurrentDictionary<string, ConcurrentDictionary<object, TEntity>> _dictionaries = new ConcurrentDictionary<string, ConcurrentDictionary<object, TEntity>>();

        /// <summary>
        ///     Dictionary of indexes. Indexes are sorted by dictionary key.
        /// </summary>
        protected ConcurrentDictionary<string, ConcurrentDictionary<object, List<TEntity>>> _indexes = new ConcurrentDictionary<string, ConcurrentDictionary<object, List<TEntity>>>();

        /// <summary>
        ///     Arrays of columns marked as Primary Key. Those will be used to identify entity.
        /// </summary>
        protected PropertyInfo[] _keyColumns;

        /// <summary>
        ///     Lock to prevent staring multiple load operations at the same time.
        /// </summary>
        protected object _isLoading = new object();

        /// <summary>
        ///     Holds last where clause used during LoadWhere(). Stored just in case user decides to call Reload().
        /// </summary>
        protected Expression<Func<TEntity, bool>> _whereClause;

        #endregion

        #region Methods

        #region Initialization API

        /// <summary>
        ///     Clears data from cache.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when cache data load operation is already in progress.</exception>
        public void Unload() {
            if (Monitor.TryEnter(_isLoading))
                try {
                    _entities.Clear();
                    IsLoaded = false;

                    foreach (ConcurrentDictionary<object, List<TEntity>> index in _indexes.Values) {
                        foreach (List<TEntity> list in index.Values) list.Clear();
                        index.Clear();
                    }

                    _indexes.Clear();

                    foreach (ConcurrentDictionary<object, TEntity> dictionary in _dictionaries.Values) dictionary.Clear();

                    _dictionaries.Clear();
                } finally {
                    Monitor.Exit(_isLoading);
                }
            else
                throw new InvalidOperationException("Cache data load operation is already in progress.");
        }

        /// <summary>
        ///     Reloads data into cache. Cache will be dropped and filled with new created Entities. Existing indexes and
        ///     dictionaries will be recreated.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when cache data load operation is already in progress.</exception>
        public void Reload() {
            if (Monitor.TryEnter(_isLoading)) {
                List<TEntity> newEntities = _whereClause != null ? GetEntitiesWhere(_whereClause) : GetEntities();

                try {
                    _entities.Clear();
                    _entities.AddRange(newEntities);

                    List<string> indexes      = _indexes.Keys.ToList();
                    List<string> dictionaries = _dictionaries.Keys.ToList();

                    foreach (ConcurrentDictionary<object, List<TEntity>> index in _indexes.Values) {
                        foreach (List<TEntity> list in index.Values) list.Clear();
                        index.Clear();
                    }

                    _indexes.Clear();

                    foreach (ConcurrentDictionary<object, TEntity> dictionary in _dictionaries.Values) dictionary.Clear();

                    _dictionaries.Clear();

                    foreach (string index in indexes) BuildIndex(index);

                    foreach (string dictionary in dictionaries) BuildDictionary(dictionary);
                } finally {
                    Monitor.Exit(_isLoading);
                }
            } else {
                throw new InvalidOperationException("Cache data reload already in progress.");
            }
        }

        /// <summary>
        ///     Load limited number of Entities into cache. Load only Entities that relate with another cached item.
        /// </summary>
        /// <remarks>
        ///     The established relation works according with SQL INNER JOIN rules. If bigger set of data is used for
        ///     <see cref="TRelation" /> type it may result in doubled Entities in this entity collection.
        ///     ATTENTION! Only simple keys are supported.
        /// </remarks>
        /// <typeparam name="TRelation">Type inherited from <see cref="AdoCacheEntity" /> that relates to <see cref="TEntity" />.</typeparam>
        /// <param name="cachedItem">Cache item holding relating Entities.</param>
        /// <param name="clause">Lambda expression describing data relation.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when data was already loaded.
        /// - OR -
        /// Thrown when data loading process is already underway.
        /// </exception>
        public void LoadRelatedWith<TRelation>(AdoCacheItem<TRelation> cachedItem, Expression<Func<TEntity, TRelation, bool>> clause) where TRelation : AdoCacheEntity, new() {
            if (IsLoaded) throw new InvalidOperationException("Cache data has already been loaded. Call Unload() to refresh data or Unload to clear data from cache");

            if (Monitor.TryEnter(_isLoading))
                try {
                    _entities.AddRange(GetEntitiesRelatedWith(cachedItem, clause));
                    IsLoaded = true;
                } finally {
                    Monitor.Exit(_isLoading);
                }
            else
                throw new InvalidOperationException("Cache data is already being loaded.");
        }

        /// <summary>
        ///     Load limited number of Entities into cache.
        /// </summary>
        /// <param name="clause">Lambda expression describing data limitations.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when data was already loaded.
        /// - OR -
        /// Thrown when data loading process is already underway.
        /// </exception>
        public void LoadWhere(Expression<Func<TEntity, bool>> clause) {
            if (IsLoaded) throw new InvalidOperationException("Cache data has already been loaded. Call Reload() to refresh data or Unload() to clear data from cache");

            if (Monitor.TryEnter(_isLoading))
                try {
                    _whereClause = clause;
                    _entities.AddRange(GetEntitiesWhere(_whereClause));
                    IsLoaded = true;
                } finally {
                    Monitor.Exit(_isLoading);
                }
            else
                throw new InvalidOperationException("Cache data is already being loaded.");
        }

        /// <summary>
        ///     Loads Entities into cache.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when data was already loaded.
        /// - OR -
        /// Thrown when data loading process is already underway.
        /// </exception>
        public void LoadAll() {
            if (IsLoaded) throw new InvalidOperationException("Cache data has already been loaded. Call Reload() to refresh data or Unload() to clear data from cache");

            if (Monitor.TryEnter(_isLoading))
                try {
                    _whereClause = null;
                    _entities.AddRange(GetEntities());
                    IsLoaded = true;
                } finally {
                    Monitor.Exit(_isLoading);
                }
            else
                throw new InvalidOperationException("Cache data is already being loaded.");
        }

        /// <summary>
        ///     Build new index. Name of column will be used as key and sorting property.
        /// </summary>
        /// <param name="nameOfColumn">Name of column used as key.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when model for this item don't have property named same as <see cref="nameOfColumn"/>.</exception>
        /// <exception cref="ArgumentException">Index for supplied column already exists.</exception>
        public virtual void BuildIndex(string nameOfColumn) {
            PropertyInfo property = typeof(TEntity).GetProperty(nameOfColumn);
            if (property == null) throw new ArgumentOutOfRangeException(nameOfColumn, $"Type {typeof(TEntity).Name} does not have a property named {nameOfColumn}");

            ConcurrentDictionary<object, List<TEntity>> newIndex = new ConcurrentDictionary<object, List<TEntity>>();

            if (!ReferenceEquals(_indexes.GetOrAdd(nameOfColumn, newIndex), newIndex)) throw new ArgumentException($"Index for {nameOfColumn} already exists.");

            foreach (TEntity entity in _entities) {
                object        value = property.GetValue(entity);
                List<TEntity> list  = newIndex.GetOrAdd(value ?? DBNull.Value, new List<TEntity>());
                list.Add(entity);
            }
        }

        /// <summary>
        ///     Build new dictionary. Name of column will be used as key and sorting property.
        /// </summary>
        /// <param name="nameOfColumn">
        ///     Name of column used as key. ATTENTION! Make sure that values in this column are unique.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when model for this item don't have property named same as <see cref="nameOfColumn"/>.</exception>
        /// <exception cref="ArgumentException">Dictionary for supplied column already exists.</exception>
        public virtual void BuildDictionary(string nameOfColumn) {
            if (_dictionaries.ContainsKey(nameOfColumn)) throw new ArgumentException($"Dictionary for {nameOfColumn} already exists.");

            PropertyInfo property = typeof(TEntity).GetProperty(nameOfColumn);
            if (property == null) throw new ArgumentOutOfRangeException(nameOfColumn, $"Type {typeof(TEntity).Name} does not have a property named {nameOfColumn}");

            ConcurrentDictionary<object, TEntity> newDictionary = new ConcurrentDictionary<object, TEntity>();

            if (!ReferenceEquals(_dictionaries.GetOrAdd(nameOfColumn, newDictionary), newDictionary)) throw new ArgumentException($"Dictionary for {nameOfColumn} already exists.");

            foreach (TEntity entity in _entities) newDictionary.TryAdd(property.GetValue(entity), entity);
        }

        #endregion

        #region Usability API

        /// <summary>
        ///     Update entity in local cache and in data base. ATTENTION! Ensure that you're only passing objects that are part of
        ///     the cache Entities collection!
        /// </summary>
        /// <param name="entity">
        ///     Entity that should be used as data source for update. ATTENTION! Ensure that you're only passing
        ///     objects that are part of the cache Entities collection!
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when supplied type is marked as read-only.
        /// - OR -
        /// Thrown when there was an unexpected result for UPDATE operation on db engine - different number affected rows than 1.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when supplied entity is not managed by cache engine instance.</exception>
        /// <returns>Updated entity.</returns>
        public virtual TEntity Update(TEntity entity) {
            if (IsReadOnly) throw new InvalidOperationException($"Type {typeof(TEntity)} do not have any public Properties marked with [Key] attribute. Please mark at least one of the properties as [Key].");
            if (!entity.IsManagedByCacheEngine) throw new ArgumentOutOfRangeException(nameof(entity), entity, "Supplied entity is not managed by cache engine and can't be used in update operation.");

            using (SqlConnection conn = new SqlConnection(_connectionString)) {
                using (SqlCommand cmd = BuildUpdateCommand(entity, conn)) {
                    conn.Open();

                    int rowsAffected = cmd.ExecuteNonQuery();

                    if (rowsAffected > 0) {
                        // UPDATE INDEXES
                        DeleteFromIndexes(entity);

                        if (_readOnlyColumns != null) UpdateReadOnlyColumn(entity, conn);

                        MethodInfo copyNewValuesMethod = typeof(TEntity).GetMethod("CopyNewValues", BindingFlags.NonPublic | BindingFlags.Instance);
                        copyNewValuesMethod.Invoke(entity, null);

                        // add entity to new aggregations
                        InsertIntoIndexes(entity);
                    } else {
                        throw new InvalidOperationException($"There was an unexpected result while Updating data in data base. Entity key could not be located or is duplicated. Affected rows: {rowsAffected}");
                    }
                }

                conn.Close();
            }

            return entity;
        }

        /// <summary>
        ///     Uses BinarySearch to find all Entities with matching value on provided column.
        /// </summary>
        /// <param name="nameOfColumn">Name of column</param>
        /// <param name="value">Value to find</param>
        /// <exception cref="ArgumentException">Thrown when index for column doesn't exist.</exception>
        /// <returns>List of Entities with matching value.</returns>
        public virtual List<TEntity> FindInIndex(string nameOfColumn, object value) {
            if (_indexes.TryGetValue(nameOfColumn, out ConcurrentDictionary<object, List<TEntity>> index)) {
                index.TryGetValue(value, out List<TEntity> entity);
                return entity?.ToList();
            }

            throw new ArgumentException($"Index for {nameOfColumn} does not exists.");
        }

        /// <summary>
        ///     Get entity from dictionary.
        /// </summary>
        /// <param name="nameOfColumn">Name of column.</param>
        /// <param name="value">Value of key field.</param>
        /// <exception cref="ArgumentException">Thrown when index for column doesn't exist.</exception>
        /// <returns>Entity with key.</returns>
        public virtual TEntity FindInDictionary(string nameOfColumn, object value) {
            if (_dictionaries.TryGetValue(nameOfColumn, out ConcurrentDictionary<object, TEntity> dictionary)) {
                dictionary.TryGetValue(value, out TEntity entity);
                return entity;
            }

            throw new ArgumentException($"Index for {nameOfColumn} does not exists.");
        }

        /// <summary>
        ///     Insert entity to to local cache and to data base.
        /// </summary>
        /// <param name="entity">Entity to insert.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when Item is marked as read-only.
        /// - OR -
        /// Thrown when INSERT operation on db engine returns unexpected value (below zero) from SCOPE_IDENTITY.
        /// - OR -
        /// Thrown when at least one column is marked with [AutoIncrement] attribute, but SCOPE_IDENTITY do not return value. 
        /// </exception>
        /// <returns>Inserted entity.</returns>
        public virtual TEntity Insert(TEntity entity) {
            if (IsReadOnly) throw new InvalidOperationException($"Type {typeof(TEntity)} do not have any public Properties marked with Key attribute. Please mark at least one of the properties as [Key].");

            // create instance of TEntity while passing 'true' to isManagedByCacheEngine
            TEntity newEntity = (TEntity) Activator.CreateInstance(typeof(TEntity), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] {true}, null, null);

            using (SqlConnection conn = new SqlConnection(_connectionString)) {
                using (SqlCommand insert = BuildInsertCommand(entity, conn)) {
                    conn.Open();
                    
                    object scalar = insert.ExecuteScalar();

                    if (_autoIncrementColumns.Length > 0) {
                        try {
                            int insertedId = Convert.ToInt32(scalar);
                            if (insertedId > 0) {
                                if (_autoIncrementColumns != null && _autoIncrementColumns.Length == 1) _autoIncrementColumns[0].SetValue(newEntity, insertedId);
                            } else {
                                throw new InvalidOperationException($"There was an unexpected result while Inserting data to data base. Returned index: {insertedId}");
                            }
                        } catch (InvalidCastException ex) {
                            throw new InvalidOperationException($"Column(s) {string.Join<PropertyInfo>(", ", _autoIncrementColumns)} are marked with [AutoIncrement] attribute, but db engine did not return scope identity after Insert(). DATA ARE INCONSISTENT.", ex);
                        }
                    }

                    foreach (PropertyInfo keyColumn in _keyColumns.Where(c => !_autoIncrementColumns.Contains(c))) keyColumn.SetValue(newEntity, keyColumn.GetValue(entity));

                    MethodInfo copyNewValuesMethod = typeof(TEntity).GetMethod("CopyNewValues", BindingFlags.NonPublic | BindingFlags.Instance);
                    copyNewValuesMethod.Invoke(newEntity, null);

                    if (_readOnlyColumns == null) {
                        foreach (PropertyInfo info in _columns.Except(_keyColumns).Except(_autoIncrementColumns)) info.SetValue(newEntity, info.GetValue(entity));
                    } else {
                        foreach (PropertyInfo info in _columns.Except(_keyColumns).Except(_autoIncrementColumns).Except(_readOnlyColumns)) info.SetValue(newEntity, info.GetValue(entity));
                        UpdateReadOnlyColumn(newEntity, conn);
                    }

                    copyNewValuesMethod.Invoke(newEntity, null);

                    _entities.Add(newEntity);
                    InsertIntoIndexes(newEntity);
                    
                    conn.Close();
                }
            }

            return newEntity;
        }

        /// <summary>
        ///     Get dictionary for  column.
        /// </summary>
        /// <param name="nameOfColumn">Name of column that dictionary is based on.</param>
        /// <returns>Dictionary - a collection of KeyValuePair objects optimized for quick access by object's key.</returns>
        public virtual ConcurrentDictionary<object, TEntity> GetDictionary(string nameOfColumn) {
            return _dictionaries[nameOfColumn];
        }

        /// <summary>
        ///     Get index for column.
        /// </summary>
        /// <param name="nameOfColumn">Name of column that index is based on.</param>
        /// <returns>Index - list of references sorted by column.</returns>
        public virtual ConcurrentDictionary<object, List<TEntity>> GetIndex(string nameOfColumn) {
            return _indexes[nameOfColumn];
        }

        /// <summary>
        ///     Delete Entity from local cache and from data base. ATTENTION! Ensure that you're only passing objects that are part
        ///     of the cache Entities collection!
        /// </summary>
        /// <param name="entity">
        ///     Entity that should be deleted. ATTENTION! Ensure that you're only passing objects that are part of
        ///     the cache Entities collection!
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when item is marked as read-only.
        /// - OR -
        /// Thrown when there was unexpected result from DELETE operation on db engine - number of affected rows was different than 1.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when supplied entity is not managed by cache engine instance.
        /// - OR -
        /// Thrown when it was not possible to deduce if entity is managed by cache engine instance. 
        /// </exception>
        public virtual void Delete(TEntity entity) {
            if (IsReadOnly) throw new InvalidOperationException($"Type {typeof(TEntity)} do not have any public Properties marked with Key attribute. Please mark at least one of the properties as [Key].");
            if (!entity.IsManagedByCacheEngine) throw new ArgumentOutOfRangeException(nameof(entity), entity, "Supplied entity is not managed by cache engine and can't be an delete operation argument.");

            string query = BuildDeleteQuery(entity);
            using (SqlConnection conn = new SqlConnection(_connectionString)) {
                conn.Open();

                using (SqlCommand command = new SqlCommand(query, conn)) {
                    int rowsAffected = command.ExecuteNonQuery();
                    if (rowsAffected > 0) {
                        _entities.Remove(entity);
                        DeleteFromIndexes(entity);

                        FieldInfo field = typeof(TEntity).GetField("_isManagedByCacheEngine", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (field != null)
                            field.SetValue(entity, false, BindingFlags.Instance | BindingFlags.NonPublic, null, CurrentCulture);
                        else
                            throw new ArgumentOutOfRangeException("_isManagedByCacheEngine", null, "Unable to retrieve field '_isManagedByCacheEngine' from removed entity.");
                    } else {
                        throw new InvalidOperationException($"There was an unexpected result while Deleting data from data base. Number of affected rows: {rowsAffected}");
                    }
                }

                conn.Close();
            }
        }

        #endregion

        #region Internal API

        #region Query builders

        /// <summary>
        ///     Build a query to select values in all columns market with ReadOnlyAttribute.
        /// </summary>
        /// <param name="entity">Entity for which the query need to be built.</param>
        /// <returns>Query string of Select statement.</returns>
        private string BuildSelectReadOnlyColumnsQuery(TEntity entity) {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            string       query        = $"SELECT {string.Join(", ", _readOnlyColumns.Select(c => c.Name).ToArray())} FROM {TableName}";
            List<string> whereClauses = new List<string>(_keyColumns.Length);
            foreach (PropertyInfo key in _keyColumns) whereClauses.Add(key.Name + " = '" + key.GetValue(entity) + "'");
            query += " WHERE " + string.Join(" AND ", whereClauses);

            Thread.CurrentThread.CurrentCulture = CurrentCulture;
            return query;
        }

        /// <summary>
        ///     Build a query to delete provided entity.
        /// </summary>
        /// <param name="entity">Entity to delete.</param>
        /// <returns>Query string of Delete statement.</returns>
        private string BuildDeleteQuery(TEntity entity) {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            string query = $"DELETE FROM {TableName}";

            List<string> whereClauses = new List<string>(_keyColumns.Length);
            foreach (PropertyInfo key in _keyColumns) whereClauses.Add(key.Name + " = '" + key.GetValue(entity) + "'");

            query += " WHERE " + string.Join(" AND ", whereClauses);

            Thread.CurrentThread.CurrentCulture = CurrentCulture;
            return query;
        }

        /// <summary>
        ///     Build a query to find entity in data base.
        /// </summary>
        /// <param name="entity">Entity to find.</param>
        /// <returns>Query string of Select statement. Returns result of count(*).</returns>
        private string BuildFindInDbByIdQuery(TEntity entity) {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            string query = $"SELECT COUNT(*) FROM {TableName}";

            List<string> whereClauses = new List<string>(_keyColumns.Length);
            foreach (PropertyInfo key in _keyColumns) whereClauses.Add(key.Name + " = '" + key.GetValue(entity) + "'");

            query += " WHERE " + string.Join(" AND ", whereClauses);

            Thread.CurrentThread.CurrentCulture = CurrentCulture;
            return query;
        }

        /// <summary>
        /// Build Sql Command to insert entity to data base.
        /// </summary>
        /// <param name="entity">Entity to insert.</param>
        /// <param name="conn">Sql Connection that should be used.</param>
        /// <returns>Sql Command to insert entity.</returns>
        private SqlCommand BuildInsertCommand(TEntity entity, SqlConnection conn) {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            PropertyInfo[] cols                = _columns.Where(c => !_autoIncrementColumns.Contains(c)).ToArray();
            if (_readOnlyColumns != null) cols = cols.Where(c => !_readOnlyColumns.Contains(c)).ToArray();
            
            string query = $"INSERT INTO {TableName}({string.Join(", ", cols.Select(c => c.Name).ToArray())}) VALUES({string.Join(", ", cols.Select(c => "@" + c.Name).ToArray())});SELECT SCOPE_IDENTITY();";

            SqlCommand cmd = new SqlCommand(query, conn);
            foreach (PropertyInfo col in cols) {
                object value = col.GetValue(entity);
                cmd.Parameters.AddWithValue("@" + col.Name, value ?? DBNull.Value);
            }
            
            Thread.CurrentThread.CurrentCulture = CurrentCulture;

            return cmd;
        }

        /// <summary>
        /// Build Sql Command to update entity in data base.
        /// </summary>
        /// <param name="entity">Entity to update.</param>
        /// <param name="conn">Sql Connection to use.</param>
        /// <returns>Sql Command with Update statement.</returns>
        private SqlCommand BuildUpdateCommand(TEntity entity, SqlConnection conn) {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            string query = "UPDATE " + TableName + " SET ";

            PropertyInfo[] colsToUpdate                = _columns.Where(c => !_keyColumns.Contains(c)).ToArray();
            if (_readOnlyColumns != null) colsToUpdate = colsToUpdate.Where(c => !_readOnlyColumns.Contains(c)).ToArray();
            List<string> setClauses                    = new List<string>(colsToUpdate.Length);
            foreach (PropertyInfo property in colsToUpdate) {
                setClauses.Add($"{property.Name} = @{property.Name} ");
            }

            query += string.Join(", ", setClauses);

            List<string> whereClauses = new List<string>(_keyColumns.Length);
            foreach (PropertyInfo key in _keyColumns) whereClauses.Add(key.Name + " = '" + key.GetValue(entity) + "'");

            query += " WHERE " + string.Join(" AND ", whereClauses);
            
            SqlCommand cmd = new SqlCommand(query, conn);
            foreach (PropertyInfo col in colsToUpdate) {
                object value = _newValueFields[col].GetValue(entity);
                cmd.Parameters.AddWithValue(col.Name, value ?? DBNull.Value);
            }
            
            Thread.CurrentThread.CurrentCulture = CurrentCulture;

            return cmd;
        }

        #endregion

        /// <summary>
        ///     Check if entity is present in data base.
        /// </summary>
        /// <param name="entity">Entity to find.</param>
        /// <returns>True if entity is in data base. False otherwise.</returns>
        private bool IsPresentInDb(TEntity entity) {
            bool result = false;
            using (SqlConnection conn = new SqlConnection(_connectionString)) {
                conn.Open();
                string query = BuildFindInDbByIdQuery(entity);
                using (SqlCommand command = new SqlCommand(query, conn)) {
                    using (SqlDataReader dataReader = command.ExecuteReader()) {
                        dataReader.Read();
                        if ((int) dataReader[0] == 1) result = true;
                    }
                }

                conn.Close();
            }

            return result;
        }

        /// <summary>
        ///     Find entity in local collection.
        /// </summary>
        /// <param name="entity">Entity to find.</param>
        /// <returns>Found entity. Returns null if entity could not be found.</returns>
        private TEntity FindLocal(TEntity entity) {
            List<TEntity> filtered                             = _entities.ToList();
            foreach (PropertyInfo key in _keyColumns) filtered = FilterOutNotMatchingKeys(filtered, key, entity);

            if (filtered.Count == 1) return filtered[0];

            return null;
        }

        /// <summary>
        ///     Deletes entity from declared indexes.
        /// </summary>
        /// <param name="entity">Entity to delete.</param>
        private void DeleteFromIndexes(TEntity entity) {
            foreach (KeyValuePair<string, ConcurrentDictionary<object, List<TEntity>>> index in _indexes) {
                PropertyInfo property = typeof(TEntity).GetProperty(index.Key);
                object       value    = property.GetValue(entity);
                if (index.Value.TryGetValue(value ?? DBNull.Value, out List<TEntity> list)) list.Remove(entity);
            }

            foreach (KeyValuePair<string, ConcurrentDictionary<object, TEntity>> dictionary in _dictionaries) dictionary.Value.TryRemove(typeof(TEntity).GetProperty(dictionary.Key).GetValue(entity), out TEntity _);
        }

        /// <summary>
        ///     Remove all items from orgList that don't match key values of entity.
        /// </summary>
        /// <param name="orgList">List of cashed items.</param>
        /// <param name="key">Property which value will be compared.</param>
        /// <param name="entity">Entity containing searched values.</param>
        /// <returns>List of matching Entities.</returns>
        private static List<TEntity> FilterOutNotMatchingKeys(List<TEntity> orgList, PropertyInfo key, TEntity entity) {
            List<TEntity> filteredList = new List<TEntity>(orgList.Capacity);

            dynamic entityValue = key.GetValue(entity);

            foreach (TEntity orgEntity in orgList) {
                dynamic orgValue = key.GetValue(orgEntity);
                if (orgValue == entityValue) filteredList.Add(orgEntity);
            }

            return filteredList;
        }

        /// <summary>
        /// Get list of entities for table.
        /// </summary>
        /// <param name="table">Table name.</param>
        /// <returns>List of entities.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when one of the columns in table doesn't exist in model class.</exception>
        private static List<TEntity> GetEntityList(DataTable table) {
            List<TEntity> entities = new List<TEntity>();
            foreach (DataRow row in table.Rows) {
                // create instance of TEntity while passing 'true' to isManagedByCacheEngine
                TEntity newEntity = (TEntity) Activator.CreateInstance(typeof(TEntity), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] {true}, null, null);

                foreach (DataColumn column in table.Columns) {
                    PropertyInfo property = typeof(TEntity).GetProperty(column.ColumnName);
                    if (property != null)
                        property.SetValue(newEntity, row[column.ColumnName] is DBNull ? null : row[column.ColumnName]);
                    else
                        throw new ArgumentOutOfRangeException($"Column '{column.ColumnName}' doesn't exists in model class.");
                }

                MethodInfo CopyNewValuesMethod = typeof(TEntity).GetMethod("CopyNewValues", BindingFlags.NonPublic | BindingFlags.Instance);
                CopyNewValuesMethod.Invoke(newEntity, null);

                entities.Add(newEntity);
            }

            return entities;
        }

        /// <summary>
        ///     Get Entities from database.
        /// </summary>
        /// <typeparam name="TRelation">Type of cached entity that is base for loading data.</typeparam>
        /// <param name="cachedItem">Cached item object that is used as base for loading data.</param>
        /// <param name="clause">Key clause.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when expression describing key could not be parsed.
        /// - OR -
        /// Thrown when expression has unexpected number of parameters.
        /// </exception>
        /// <returns>The <see cref="List{T}" /> list of Entities.</returns>
        private List<TEntity> GetEntitiesRelatedWith<TRelation>(AdoCacheItem<TRelation> cachedItem, Expression<Func<TEntity, TRelation, bool>> clause) where TRelation : AdoCacheEntity, new() {
            string                 typeName     = typeof(TEntity).Name;
            ConcurrentBag<TEntity> entitiesList = new ConcurrentBag<TEntity>();

            WherePart sql  = new WhereBuilder().ExpressionToSql(clause.Body);
            string    type = typeof(TRelation).Name;
            if (sql.Sql.Contains(type)) {
                List<ReplaceInfo> replaceInfos = ReplaceInfo.AllIndexesOf(sql.Sql, type);

                foreach (ReplaceInfo info in replaceInfos) {
                    info.Property = typeof(TRelation).GetProperty(info.Field);
                }

                Parallel.ForEach(cachedItem.Entities, entity => {
                                                          using (SqlConnection conn = new SqlConnection(_connectionString)) {
                                                              conn.Open();
                                                              string whereClause = sql.Sql;
                                                              foreach (ReplaceInfo info in replaceInfos) {
                                                                  info.NewString = info.IsString ? $"'{info.Property.GetValue(entity)}'" : info.Property.GetValue(entity).ToString();
                                                                  whereClause    = whereClause.Replace(info.OldString, info.NewString);
                                                              }

                                                              foreach (KeyValuePair<string, object> parameter in sql.Parameters) whereClause = whereClause.Replace($"@{parameter.Key}", $"{(parameter.Value == null ? "NULL" : $"'{parameter.Value.ToString().Replace("\"", "")}'")}");
                                                              whereClause = whereClause.Replace("[", "").Replace("]", "").Replace($"{typeName}.", "");

                                                              string    queryContent = $"SELECT * FROM {TableName} WHERE {whereClause}";
                                                              DataTable table        = null;
                                                              using (SqlDataAdapter adapter = new SqlDataAdapter(queryContent, conn)) {
                                                                  table = new DataTable(TableName);
                                                                  adapter.Fill(table);
                                                              }

                                                              List<TEntity> entities = GetEntityList(table);
                                                              foreach (TEntity ent in entities) {
                                                                  entitiesList.Add(ent);
                                                              }

                                                              conn.Close();
                                                          }
                                                      });
            }

            List<TEntity> list = entitiesList.ToList();
            return list;
        }

        /// <summary>
        /// Find all position of substring in larger text.
        /// </summary>
        /// <remarks>
        /// source: https://stackoverflow.com/questions/2641326/finding-all-positions-of-substring-in-a-larger-string-in-c-sharp
        /// </remarks>
        /// <param name="str">String to look through.</param>
        /// <param name="value">String to look for.</param>
        /// <returns>List of indexes.</returns>
        private List<int> AllIndexesOf(string str, string value)
        {
            if (String.IsNullOrEmpty(value))
                throw new ArgumentException("The string to find cannot be empty", nameof(value));
            List<int> indexes = new List<int>();
            for (int index = 0; ; index += value.Length)
            {
                index = str.IndexOf(value, index);
                if (index == -1)
                    return indexes;
                indexes.Add(index);
            }
        }

        /// <summary>
        ///     Get Entities from database.
        /// </summary>
        /// <param name="clause">Where clause.</param>
        /// <returns>The <see cref="List{T}" /> list of Entities.</returns>
        private List<TEntity> GetEntitiesWhere(Expression<Func<TEntity, bool>> clause) {
            DataTable table = null;

            using (SqlConnection conn = new SqlConnection(_connectionString)) {
                conn.Open();

                WherePart sql                                                             = new WhereBuilder().ToSql(clause);
                string    whereClause                                                     = sql.Sql;
                foreach (KeyValuePair<string, object> pair in sql.Parameters) 
                    whereClause = whereClause.Replace($"@{pair.Key}", $"{(pair.Value == null ? "NULL" : $"'{pair.Value.ToString().Replace("\"", "")}'")}");

                using (SqlDataAdapter adapter = new SqlDataAdapter($"SELECT * FROM {TableName} WHERE {whereClause}", conn)) {
                    table = new DataTable(TableName);
                    adapter.Fill(table);
                }

                conn.Close();
            }

            return GetEntityList(table);
        }

        /// <summary>
        ///     Gets Entities from database.
        /// </summary>
        /// <returns>
        ///     The <see cref="List{T}" /> list of Entities.
        /// </returns>
        private List<TEntity> GetEntities() {
            DataTable table = null;

            using (SqlConnection conn = new SqlConnection(_connectionString)) {
                conn.Open();
                using (SqlDataAdapter adapter = new SqlDataAdapter($"SELECT * FROM {TableName}", conn)) {
                    table = new DataTable(TableName);
                    adapter.Fill(table);
                }

                conn.Close();
            }

            return GetEntityList(table);
        }

        /// <summary>
        ///     Inserts new entity into declared indexes and re-sort them.
        /// </summary>
        /// <param name="entity">Entity to insert.</param>
        private void InsertIntoIndexes(TEntity entity) {
            foreach (KeyValuePair<string, ConcurrentDictionary<object, List<TEntity>>> index in _indexes) {
                PropertyInfo  property = typeof(TEntity).GetProperty(index.Key);
                object        value    = property.GetValue(entity);
                List<TEntity> list     = index.Value.GetOrAdd(value ?? DBNull.Value, new List<TEntity>());
                list.Add(entity);
            }

            foreach (KeyValuePair<string, ConcurrentDictionary<object, TEntity>> dictionary in _dictionaries) dictionary.Value.TryAdd(typeof(TEntity).GetProperty(dictionary.Key).GetValue(entity), entity);
        }

        /// <summary>
        ///     Queries values for all columns market by ReadOnlyAttribute and updates the entity.
        /// </summary>
        /// <param name="entity">Entity to update.</param>
        /// <param name="conn">DB connection to use.</param>
        private void UpdateReadOnlyColumn(TEntity entity, SqlConnection conn) {
            string selectQuery = BuildSelectReadOnlyColumnsQuery(entity);
            using (SqlCommand select = new SqlCommand(selectQuery, conn)) {
                SqlDataReader selectReader = select.ExecuteReader();
                if (selectReader.HasRows) {
                    int resultCount = 0;
                    while (selectReader.Read()) {
                        resultCount++;
                        if (resultCount > 1) throw new InvalidOperationException("There was an error while querying values of ReadOnly columns. Query returned more than 1 result.");
                        foreach (PropertyInfo readOnlyColumn in _readOnlyColumns) {
                            object value = selectReader[readOnlyColumn.Name];
                            readOnlyColumn.SetValue(entity, value == DBNull.Value ? null : value);
                        }
                    }
                } else {
                    throw new InvalidOperationException("There was an error while querying values of ReadOnly columns. Query returned no results.");
                }
            }
        }

        #endregion

        #endregion
    }
}
