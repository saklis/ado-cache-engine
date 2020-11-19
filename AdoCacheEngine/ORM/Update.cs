using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using AdoCache.Attributes;
using AdoCache.ryanohs;

namespace AdoCache.ORM
{
    public class Update<TEntity> where TEntity : AdoCacheEntity, new()
    {
        protected readonly SqlConnection _sqlConn;
        protected Expression<Func<TEntity, bool>> _clause;

        protected Dictionary<string, object> _setDict = new Dictionary<string, object>();

        internal Update(SqlConnection sqlConn)
        {
            _sqlConn = sqlConn;

            string tableName = (typeof(TEntity).GetCustomAttributes(typeof(TableNameAttribute), false)[0] as TableNameAttribute)?.TableName;
            if (string.IsNullOrWhiteSpace(tableName))
            {
                throw new ArgumentException("Could not deduce table name.");
            }

            TableName = tableName;
        }

        public string TableName { get; }

        /// <summary>
        ///     Build Sql Command to update entity in data base.
        /// </summary>
        /// <param name="entity"> Entity to update. </param>
        /// <param name="conn">   Sql Connection to use. </param>
        /// <returns> Sql Command with Update statement. </returns>
        private SqlCommand BuildUpdateCommand(SqlConnection conn)
        {
            StringBuilder query = new StringBuilder($"UPDATE {TableName} ");

            SqlCommand command = new SqlCommand {Connection = conn};

            query.Append($"SET {string.Join(", ", _setDict.Select(sd => $"{sd.Key} = @{sd.Key}"))}");
            foreach (KeyValuePair<string, object> pair in _setDict)
            {
                command.Parameters.AddWithValue($@"{pair.Key}", pair.Value ?? DBNull.Value);
            }

            if (_clause != null)
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

                query.Append($" WHERE {whereClause}");
            }

            command.CommandText = query.ToString();
            return command;
        }

        #region API

        public int Execute()
        {
            int rowsCount = 0;
            using (SqlCommand update = BuildUpdateCommand(_sqlConn))
            {
                rowsCount = update.ExecuteNonQuery();
                if (rowsCount <= 0)
                {
                    throw new
                        InvalidOperationException($"There was an unexpected result while Updating data in data base. Number of rows affected: {rowsCount}");
                }
            }

            return rowsCount;
        }

        public Update<TEntity> Set<TResult>(Expression<Func<TEntity, TResult>> selector, TResult value)
        {
            if (selector.Body.NodeType == ExpressionType.MemberAccess)
            {
                if (selector.Body is MemberExpression member)
                {
                    if (_setDict.ContainsKey(member.Member.Name))
                    {
                        _setDict[member.Member.Name] = value;
                    }
                    else
                    {
                        _setDict.Add(member.Member.Name, value);
                    }
                }
                else
                {
                    throw new ArgumentException("Selector's Body need to be a MemberExpression.");
                }
            }
            else
            {
                throw new ArgumentException("Provided selector has incorrect structure. Only Expressions of type MemberAccess are supported.");
            }

            return this;
        }

        public Update<TEntity> Where(Expression<Func<TEntity, bool>> clause)
        {
            _clause = clause;
            return this;
        }

        #endregion API
    }
}