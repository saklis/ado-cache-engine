namespace AdoCache {
    /// <summary>
    ///     Hold all configuration values for <see cref="AdoCacheItem{TEntity}" /> instance.
    /// </summary>
    public class AdoCacheItemOptions {
        /// <summary>
        ///     If set to true, Cache Item will respect <see cref="ReadOnlyColumnAttribute" /> on properties in the model.
        ///     Values in those properties will be updated from database after each Insert and Update operation.
        ///     ATTENTION! THis is performance heavy. Each Insert and Update needs to do additional Data Base query to retrieve
        ///     read-only values.
        ///     Default value: false;
        /// </summary>
        public bool EnableReadOnlyColumnsSupport { get; set; } = false;

        /// <summary>
        ///     If set to string value, this string value will be treated as SQL table name.
        ///     Value read from <see cref="TableNameAttribute" /> on the model class will be ignored.
        ///     Default value: null;
        /// </summary>
        public string OverrideTableName { get; set; } = null;
    }
}