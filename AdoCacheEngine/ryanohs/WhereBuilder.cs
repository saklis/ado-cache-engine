﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SqlServer.Server;

namespace AdoCache.ryanohs
{
    /// <summary>
    /// source: http://ryanohs.com/2016/04/generating-sql-from-expression-trees-part-2/
    /// </summary>
    internal class WhereBuilder
    { 

        public WhereBuilder()
        {
        }

        public WherePart ToSql<T>(Expression<Func<T, bool>> expression)
        {
            var i = 1;
            return Recurse(ref i, expression.Body);
        }

        public WherePart ExpressionToSql(Expression expression) {
            var i = 1;
            return Recurse(ref i, expression, false, null, null, true);
        }

        private WherePart Recurse(ref int i, Expression expression, bool isUnary = false, string prefix = null, string postfix = null, bool addReflectedType = false)
        {
            if (expression is UnaryExpression) {
                var unary = (UnaryExpression)expression;
                WherePart unaryRight = Recurse(ref i, unary.Operand, true, addReflectedType: addReflectedType);
                return WherePart.Concat(NodeTypeToString(null, unary.NodeType, unaryRight), unaryRight);
            }
            if (expression is BinaryExpression) {
                var body = (BinaryExpression)expression;
                WherePart left = Recurse(ref i, body.Left, addReflectedType: addReflectedType);
                WherePart right = Recurse(ref i, body.Right, addReflectedType: addReflectedType);
                return WherePart.Concat(left, NodeTypeToString(left, body.NodeType, right), right);
            }
            if (expression is ConstantExpression) {
                var constant = (ConstantExpression)expression;
                var value = constant.Value;
                if (value is int) {
                    return WherePart.IsSql(value.ToString());
                }
                if (value is string) {
                    value = prefix + (string)value + postfix;
                }
                if (value is bool && isUnary) {
                    return WherePart.Concat(WherePart.IsParameter(i++, value), "=", WherePart.IsSql("1"));
                }
                return WherePart.IsParameter(i++, value);
            }
            if (expression is MemberExpression) {
                var member = (MemberExpression)expression;

                if (member.Member is PropertyInfo) {
                    var property = (PropertyInfo)member.Member;
                    var colName = (addReflectedType ? $"{property.ReflectedType.Name}." : "") + property.Name;
                    if (isUnary && member.Type == typeof(bool)) {
                        return WherePart.Concat(Recurse(ref i, expression, addReflectedType: addReflectedType), "=", WherePart.IsParameter(i++, true));
                    }
                    if (member.NodeType == ExpressionType.MemberAccess && member.Expression.NodeType == ExpressionType.MemberAccess) {
                        string name = (member.Expression as MemberExpression).Member.Name;
                        ConstantExpression constant = (member.Expression as MemberExpression).Expression as ConstantExpression;
                        object item = constant.Type.GetField(name).GetValue(constant.Value);

                        object value = item.GetType().GetProperty(member.Member.Name).GetValue(item);

                        return WherePart.IsParameter(i++, value);
                    }
                    return WherePart.IsSql("[" + colName + "]");
                }
                if (member.Member is FieldInfo) {
                    var value = GetValue(member);
                    if (value is string) {
                        value = prefix + (string)value + postfix;
                    }
                    return WherePart.IsParameter(i++, value);
                }
                throw new Exception($"Expression does not refer to a property or field: {expression}");
            }
            if (expression is MethodCallExpression) {
                var methodCall = (MethodCallExpression)expression;
                // LIKE queries:
                if (methodCall.Method == typeof(string).GetMethod("Contains", new[] { typeof(string) })) {
                    return WherePart.Concat(Recurse(ref i, methodCall.Object, addReflectedType: addReflectedType), "LIKE", Recurse(ref i, methodCall.Arguments[0], prefix: "%", postfix: "%", addReflectedType:addReflectedType));
                }
                if (methodCall.Method == typeof(string).GetMethod("StartsWith", new[] { typeof(string) })) {
                    return WherePart.Concat(Recurse(ref i, methodCall.Object, addReflectedType: addReflectedType), "LIKE", Recurse(ref i, methodCall.Arguments[0], postfix: "%", addReflectedType: addReflectedType));
                }
                if (methodCall.Method == typeof(string).GetMethod("EndsWith", new[] { typeof(string) })) {
                    return WherePart.Concat(Recurse(ref i, methodCall.Object, addReflectedType: addReflectedType), "LIKE", Recurse(ref i, methodCall.Arguments[0], prefix: "%", addReflectedType: addReflectedType));
                }
                // IN queries:
                if (methodCall.Method.Name == "Contains") {
                    Expression collection;
                    Expression property;
                    if (methodCall.Method.IsDefined(typeof(ExtensionAttribute)) && methodCall.Arguments.Count == 2) {
                        collection = methodCall.Arguments[0];
                        property = methodCall.Arguments[1];
                    } else if (!methodCall.Method.IsDefined(typeof(ExtensionAttribute)) && methodCall.Arguments.Count == 1) {
                        collection = methodCall.Object;
                        property = methodCall.Arguments[0];
                    } else {
                        throw new Exception("Unsupported method call: " + methodCall.Method.Name);
                    }
                    var values = (IEnumerable)GetValue(collection);
                    return WherePart.Concat(Recurse(ref i, property, addReflectedType: addReflectedType), "IN", WherePart.IsCollection(ref i, values));
                }
                throw new Exception("Unsupported method call: " + methodCall.Method.Name);
            }
            throw new Exception("Unsupported expression: " + expression.GetType().Name);
        }

        private static object GetValue(Expression member)
        {
            // source: http://stackoverflow.com/a/2616980/291955
            var objectMember = Expression.Convert(member, typeof(object));
            var getterLambda = Expression.Lambda<Func<object>>(objectMember);
            var getter = getterLambda.Compile();
            return getter();
        }

        public static string NodeTypeToString(WherePart left, ExpressionType nodeType, WherePart right)
        {
            switch (nodeType) {
                case ExpressionType.Add:
                    return "+";
                case ExpressionType.And:
                    return "&";
                case ExpressionType.AndAlso:
                    return "AND";
                case ExpressionType.Divide:
                    return "/";
                case ExpressionType.Equal:
                    if (IsParamValueNull(right)) return "IS";
                    else if (IsParamValueNull(left)) return "IS";
                    else return "=";
                case ExpressionType.ExclusiveOr:
                    return "^";
                case ExpressionType.GreaterThan:
                    return ">";
                case ExpressionType.GreaterThanOrEqual:
                    return ">=";
                case ExpressionType.LessThan:
                    return "<";
                case ExpressionType.LessThanOrEqual:
                    return "<=";
                case ExpressionType.Modulo:
                    return "%";
                case ExpressionType.Multiply:
                    return "*";
                case ExpressionType.Negate:
                    return "-";
                case ExpressionType.Not:
                    return "NOT";
                case ExpressionType.NotEqual:
                    if (IsParamValueNull(right)) return "IS NOT";
                    else if (IsParamValueNull(left)) return "IS NOT";
                    else return "<>";
                case ExpressionType.Or:
                    return "|";
                case ExpressionType.OrElse:
                    return "OR";
                case ExpressionType.Subtract:
                    return "-";
                case ExpressionType.Convert:
                    return "";
            }
            throw new Exception($"Unsupported node type: {nodeType}");
        }
        
        private static bool IsParamValueNull(WherePart param) {
            return param != null && param.Sql.StartsWith("@") && param.Sql.Substring(1).All(char.IsDigit) && param.Parameters[param.Sql.Substring(1)] == null;
        }
    }
}
