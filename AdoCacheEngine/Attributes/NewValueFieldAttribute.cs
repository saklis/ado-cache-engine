using System;

namespace AdoCache.Attributes {
    public class NewValueFieldAttribute : Attribute {
        public NewValueFieldAttribute(string propertyName) {
            PropertyName = propertyName;
        }

        public string PropertyName { get; }
    }
}