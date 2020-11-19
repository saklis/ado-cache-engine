using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq.Expressions;
using System.Reflection;
using AdoCache.Attributes;
using AdoCache.ryanohs;

namespace AdoCache.ORM
{
    public class Select<TEntity> where TEntity : AdoCacheEntity, new()
    {
        private readonly SqlConnection _sqlConn;
        protected Expression<Func<TEntity, bool>> _clause;

        internal Select(SqlConnection sqlConn)
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

        private static List<TEntity> GetEntityList(DataTable table)
        {
            List<TEntity> entities = new List<TEntity>();
            foreach (DataRow row in table.Rows)
            {
                TEntity newEntity = new TEntity();

                foreach (DataColumn column in table.Columns)
                {
                    PropertyInfo property = typeof(TEntity).GetProperty(column.ColumnName);
                    if (property != null)
                    {
                        property.SetValue(newEntity, row[column.ColumnName] is DBNull ? null : row[column.ColumnName]);
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException($"Column '{column.ColumnName}' doesn't exists in model class.");
                    }
                }

                entities.Add(newEntity);
            }

            return entities;
        }

        #region API

        public List<TEntity> All() => Execute();

        public List<TEntity> Execute()
        {
            DataTable table = null;

            SqlCommand command = new SqlCommand("", _sqlConn);

            if (_clause == null)
            {
                command.CommandText = $"SELECT * FROM {TableName}";
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

                command.CommandText = $"SELECT * FROM {TableName} WHERE {whereClause}";
            }

            using (SqlDataAdapter adapter = new SqlDataAdapter(command))
            {
                table = new DataTable(TableName);
                adapter.Fill(table);
            }

            return GetEntityList(table);
        }

        public Select<TEntity> Where(Expression<Func<TEntity, bool>> clause)
        {
            _clause = clause;
            return this;
        }

        #endregion API
    }
}