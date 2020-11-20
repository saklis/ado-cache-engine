using System;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Reflection;
using AdoCache.Attributes;

namespace AdoCache.ORM
{
    public class Insert<TEntity> where TEntity : AdoCacheEntity, new()
    {
        private readonly SqlConnection _sqlConn;

        /// <summary>
        ///     Array of columns with Auto Increment enabled. Those will be skipped during Insert and Update.
        /// </summary>
        protected PropertyInfo[] _autoIncrementColumns;

        /// <summary>
        ///     Array of columns in the Type.
        /// </summary>
        protected PropertyInfo[] _columns;

        /// <summary>
        ///     Array of columns with Read-Only property set to true. Those will be skipped during
        ///     Insert and Update.
        /// </summary>
        protected PropertyInfo[] _readOnlyColumns;

        internal Insert(SqlConnection sqlConn)
        {
            _sqlConn = sqlConn;

            string tableName = (typeof(TEntity).GetCustomAttributes(typeof(TableNameAttribute), false)[0] as TableNameAttribute)?.TableName;
            if (string.IsNullOrWhiteSpace(tableName))
            {
                throw new ArgumentException("Could not deduce table name.");
            }

            TableName = tableName;

            _columns = typeof(TEntity).GetProperties().Where(p => p.CanWrite).ToArray();
            if (_columns == null || _columns.Length == 0)
            {
                throw new
                    ArgumentException($"Type {typeof(TEntity)} do not have any public Properties. Provided type needs to have at least one public property.");
            }

            _autoIncrementColumns = _columns
                .Where(p => p.CustomAttributes.Any(ca => ca.AttributeType == typeof(AutoIncrementAttribute)))
                .ToArray();

            _readOnlyColumns = _columns
                .Where(p => p.CustomAttributes.Any(ca => ca.AttributeType == typeof(ReadOnlyAttribute)))
                .Where(c => !_autoIncrementColumns.Contains(c)).ToArray();

            if (_autoIncrementColumns != null && _autoIncrementColumns.Length > 1)
            {
                throw new
                    ArgumentException($"Type {typeof(TEntity)} have more than 1 column with auto-increment enabled. Only types with up to 1 auto-increment columns are supported.");
            }
        }

        /// <summary>
        ///     Name of Table in data base.
        /// </summary>
        public string TableName { get; }

        /// <summary>
        ///     Build Sql Command to insert entity to data base.
        /// </summary>
        /// <param name="entity">           Entity to insert. </param>
        /// <param name="conn">             Sql Connection that should be used. </param>
        /// <param name="addScopeIdentity">
        ///     Should command include SCOPE_IDENTITY call at the end?
        /// </param>
        /// <returns> Sql Command to insert entity. </returns>
        private SqlCommand BuildInsertCommand(TEntity entity, SqlConnection conn, bool addScopeIdentity = false)
        {
            PropertyInfo[] cols = _columns.Where(c => !_autoIncrementColumns.Contains(c)).ToArray();
            if (_readOnlyColumns != null)
            {
                cols = cols.Where(c => !_readOnlyColumns.Contains(c)).ToArray();
            }

            string query =
                $"INSERT INTO {TableName}({string.Join(", ", cols.Select(c => c.Name).ToArray())}) VALUES({string.Join(", ", cols.Select(c => "@" + c.Name).ToArray())});{(addScopeIdentity ? "SELECT SCOPE_IDENTITY();" : "")}";

            SqlCommand cmd = new SqlCommand(query, conn);
            foreach (PropertyInfo col in cols)
            {
                object value = col.GetValue(entity);
                cmd.Parameters.AddWithValue("@" + col.Name, value ?? DBNull.Value);
            }

            return cmd;
        }

        #region API

        public int Value(TEntity entity)
        {
            int rowsCount = 0;
            using (SqlCommand insert = BuildInsertCommand(entity, _sqlConn))
            {
                rowsCount = insert.ExecuteNonQuery();

                if (rowsCount <= 0)
                {
                    throw new
                        InvalidOperationException($"There was an unexpected result while Inserting data to data base. Number of rows affected: {rowsCount}");
                }
            }

            return rowsCount;
        }

        public int Value(TEntity entity, out int scopeIdentity)
        {
            using (SqlCommand insert = BuildInsertCommand(entity, _sqlConn, true))
            {
                object scalar = insert.ExecuteScalar();
                try
                {
                    scopeIdentity = Convert.ToInt32(scalar);
                    if (scopeIdentity <= 0)
                    {
                        throw new
                            InvalidOperationException($"There was an unexpected result while Inserting data to data base. Returned index: {scopeIdentity}");
                    }
                }
                catch (Exception e)
                {
                    throw new
                        InvalidOperationException("INSERT command returned unexpected value.", e);
                }
            }

            return 1;
        }

        #endregion API
    }
}