using System;

namespace AdoCache.Attributes {
    /// <summary>
    ///     Marks property as one that is read-only. This property will be skipped during insert and update operations.
    /// </summary>
    public class ReadOnlyAttribute : Attribute { }
}