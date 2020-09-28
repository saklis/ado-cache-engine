namespace AdoCache.Attributes
{
    using System;

    public class TableNameAttribute : Attribute
    {
        /// <inheritdoc />
        public TableNameAttribute(string tableName)
        {
            this.TableName = tableName;
        }
        
        public string TableName { get; }

    }
}