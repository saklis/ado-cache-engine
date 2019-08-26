using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace AdoCache.Structures
{
    /// <summary>
    /// Holds replacement information used during generation of query for join (LoadRelatedWith()).
    /// </summary>
    internal class ReplaceInfo {
        /// <summary>
        /// Name of model type.
        /// </summary>
        public string TypeName { get; set; } = "";
        /// <summary>
        /// Index in query where this replacement will take place.
        /// </summary>
        public int IndexOf { get; set; } = 0;
        /// <summary>
        /// Class.Field identifier.
        /// </summary>
        public string ClassField { get; set; } = "";
        /// <summary>
        /// Field identifier.
        /// </summary>
        public string Field { get; set; } = "";
        /// <summary>
        /// Old string that will be replaced.
        /// </summary>
        public string OldString { get; set; } = "";
        /// <summary>
        /// New string that will be inserted into query.
        /// </summary>
        public string NewString { get; set; } = "";
        /// <summary>
        /// Property from model class.
        /// </summary>
        public PropertyInfo Property { get; set; } = null;
        /// <summary>
        /// Is property a string?
        /// </summary>
        public bool IsString => Property.PropertyType == typeof(string);

        /// <summary>
        /// Build list of ReplaceInfo objects.
        /// </summary>
        /// <remarks>
        /// Based on: https://stackoverflow.com/questions/2641326/finding-all-positions-of-substring-in-a-larger-string-in-c-sharp
        /// </remarks>
        /// <param name="query">Query string to look through.</param>
        /// <param name="typeName">Type name to look for</param>
        /// <returns>List of ReplaceInfo objects found.</returns>
        internal static List<ReplaceInfo> AllIndexesOf(string query, string typeName) {
            List<ReplaceInfo> infos = new List<ReplaceInfo>();
            for (int index = 0; ; index += typeName.Length)
            {
                index = query.IndexOf(typeName, index);
                if (index == -1) return infos;

                ReplaceInfo info = new ReplaceInfo
                                          {
                                              TypeName   = typeName,
                                              IndexOf    = index,
                                              ClassField = GetClassField(query, index),
                                          };
                info.Field = info.ClassField.Substring(info.ClassField.IndexOf('.') + 1);
                info.OldString = info.ClassField;
                infos.Add(info);
            }
        }

        /// <summary>
        /// Simple query index to get first Class.Field structures available.
        /// It looks from index up to first ']' sign.
        /// </summary>
        /// <param name="query">Query to look through.</param>
        /// <param name="index">Beginning index.</param>
        /// <returns>Class.Field identifier.</returns>
        private static string GetClassField(string query, int index) {
            string classField = query.Substring(index);
            return classField.Substring(0, classField.IndexOf(']'));
        }
    }
}
