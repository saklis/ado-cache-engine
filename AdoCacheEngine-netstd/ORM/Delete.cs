using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Linq.Expressions;
using AdoCache.Attributes;
using AdoCache.ryanohs;

namespace AdoCache.ORM
{
    public class Delete<TEntity> where TEntity : AdoCacheEntity, new()
    {
        private readonly SqlConnection _sqlConn;

        private Expression<Func<TEntity, bool>> _clause;

        internal Delete(SqlConnection sqlConn)
        {
            _sqlConn = sqlConn;

            string tableName = (typeof(TEntity).GetCustomAttributes(typeof(TableNameAttribute), false)[0] as TableNameAttribute)?.TableName;
            if (string.IsNullOrWhiteSpace(tableName))
            {
                throw new ArgumentException("Could not deduce table name.");
            }

            TableName = tableName;
        }

        /// <summary>
        ///     Name of Table in data base.
        /// </summary>
        public string TableName { get; }

        #region API

        public int All() => Execute();

        public int Execute()
        {
            SqlCommand command = new SqlCommand("", _sqlConn);

            if (_clause == null)
            {
                command.CommandText = $"DELETE FROM {TableName}";
            }
            else
            {
                WherePart sql = new WhereBuilder().ToSql(_clause);
                string whereClause = sql.Sql;

                foreach (KeyValuePair<string, object> pair in sql.Parameters)
                {
                    if (pair.Value == null)
                    {
                        whereClause = whereClause.Replace($"@{pair.Key}", "NULL");
                    }
                    else
                    {
                        command.Parameters.AddWithValue($"@{pair.Key}", pair.Value);
                    }
                }

                command.CommandText = $"DELETE FROM {TableName} WHERE {whereClause}";
            }

            return command.ExecuteNonQuery();
        }

        public Delete<TEntity> Where(Expression<Func<TEntity, bool>> clause)
        {
            _clause = clause;
            return this;
        }

        #endregion API
    }
}