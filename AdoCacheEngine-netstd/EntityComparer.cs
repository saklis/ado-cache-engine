namespace AdoCache
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    internal class EntityComparer<TEntity> : IComparer<TEntity>
    {
        private readonly PropertyInfo[] _keyColumns;
        private readonly PropertyInfo _property;

        public EntityComparer(PropertyInfo property, PropertyInfo[] keyColumns = null)
        {
            this._property   = property;
            this._keyColumns = keyColumns;
        }

        public int Compare(TEntity x, TEntity y)
        {
            int propertyComparison =
                (this._property.GetValue(x) as IComparable)?.CompareTo(this._property.GetValue(y) as IComparable) ??
                (this._property.GetValue(y) == null ? 0 : -1);
            if (propertyComparison != 0)
            {
                return propertyComparison;
            }

            return this._keyColumns
                       ?.Select(keyColumn =>
                           (keyColumn.GetValue(x) as IComparable)?.CompareTo(keyColumn.GetValue(y) as IComparable) ??
                           (keyColumn.GetValue(y) == null ? 0 : -1))
                       .FirstOrDefault(keyComparison => keyComparison != 0) ?? 0;
        }
    }
}