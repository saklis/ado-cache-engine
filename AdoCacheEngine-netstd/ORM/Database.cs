using System.Data;
using Microsoft.Data.SqlClient;

namespace AdoCache.ORM
{
    public class Database
    {
        private SqlConnection _sqlConn;

        public Database(string connectionString) => ConnectionString = connectionString;

        #region API

        public Delete<TEntity> Delete<TEntity>() where TEntity : AdoCacheEntity, new() => new Delete<TEntity>(_sqlConn);

        public Insert<TEntity> Insert<TEntity>() where TEntity : AdoCacheEntity, new() => new Insert<TEntity>(_sqlConn);

        public Select<TEntity> Select<TEntity>() where TEntity : AdoCacheEntity, new() => new Select<TEntity>(_sqlConn);

        public Update<TEntity> Update<TEntity>() where TEntity : AdoCacheEntity, new() => new Update<TEntity>(_sqlConn);

        #endregion API

        #region Open/Close connection

        public string ConnectionString { get; }

        public void Close()
        {
            if (_sqlConn != null && (_sqlConn.State != ConnectionState.Closed || _sqlConn.State != ConnectionState.Broken))
            {
                _sqlConn.Close();
            }
        }

        public void Open()
        {
            _sqlConn = new SqlConnection(ConnectionString);
            _sqlConn.Open();
        }

        #endregion Open/Close connection
    }
}