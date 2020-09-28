using System;

namespace AdoCache.Attributes {
    public class CurrentValueFieldAttribute : Attribute {
        public CurrentValueFieldAttribute(string propertyName) {
            PropertyName = propertyName;
        }

        public string PropertyName { get; }
    }
}