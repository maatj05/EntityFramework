// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Query.Expressions.Internal;
using Microsoft.EntityFrameworkCore.Query.ExpressionVisitors.Internal;
using Microsoft.EntityFrameworkCore.Query.ResultOperators.Internal;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;

namespace Microsoft.EntityFrameworkCore.Query.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class QueryModelPrinter : IQueryModelPrinter
    {
        private IndentedStringBuilder _stringBuilder;
        private QueryModelExpressionPrinter _expressionPrinter;
        private QueryModelPrintingVisitor _queryModelPrintingVisitor;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public QueryModelPrinter()
        {
            _stringBuilder = new IndentedStringBuilder();
            _expressionPrinter = new QueryModelExpressionPrinter();
            _queryModelPrintingVisitor = new QueryModelPrintingVisitor(_expressionPrinter, _stringBuilder);
            _expressionPrinter.SetStringBuilder(_stringBuilder);
            _expressionPrinter.SetQueryModelPrintingVisitor(_queryModelPrintingVisitor);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual string Print([NotNull] QueryModel queryModel)
        {
            _stringBuilder.Clear();

            _queryModelPrintingVisitor.VisitQueryModel(queryModel);

            return _stringBuilder.ToString();
        }

        private class QueryModelExpressionPrinter : ExpressionPrinter
        {
            private QueryModelPrintingVisitor _queryModelPrintingVisitor;

            public QueryModelExpressionPrinter()
                : base(new List<IConstantPrinter> { new EntityQueryableConstantPrinter() })
            {
            }

            public void SetQueryModelPrintingVisitor(QueryModelPrintingVisitor queryModelPrintingVisitor)
            {
                _queryModelPrintingVisitor = queryModelPrintingVisitor;
            }

            protected override Expression VisitConstant(ConstantExpression node)
            {
                return base.VisitConstant(node);
            }

            protected override Expression VisitExtension(Expression node)
            {
                var qsre = node as QuerySourceReferenceExpression;
                if (qsre != null)
                {
                    StringBuilder.Append(qsre.ReferencedQuerySource.ItemName);

                    return node;
                }

                var subquery = node as SubQueryExpression;
                if (subquery != null)
                {
                    using (StringBuilder.Indent())
                    {
                        var isSubuery = _queryModelPrintingVisitor.IsSubquery;
                        _queryModelPrintingVisitor.IsSubquery = true;
                        _queryModelPrintingVisitor.VisitQueryModel(subquery.QueryModel);
                        _queryModelPrintingVisitor.IsSubquery = isSubuery;
                    }

                    return node;
                }

                return base.VisitExtension(node);
            }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                if (EntityQueryModelVisitor.IsPropertyMethod(node.Method))
                {
                    StringBuilder.Append("Property(");
                    Visit(node.Arguments[0]);
                    StringBuilder.Append(", ");
                    Visit(node.Arguments[1]);
                    StringBuilder.Append(")");

                    return node;
                }

                return base.VisitMethodCall(node);
            }

            private class EntityQueryableConstantPrinter : IConstantPrinter
            {
                public bool TryPrintConstant(object value, IndentedStringBuilder stringBuilder)
                {
                    if (value != null && value.GetType().GetTypeInfo().IsGenericType 
                        && value.GetType().GetTypeInfo().GetGenericTypeDefinition() == typeof(EntityQueryable<>))
                    {
                        stringBuilder.Append($"[{value.GetType().ShortDisplayName()}]");
                        return true;
                    }

                    return false;
                }
            }
        }

        private class QueryModelPrintingVisitor : ExpressionTransformingQueryModelVisitor<ExpressionPrinter>
        {
            private IndentedStringBuilder _stringBuilder;

            public QueryModelPrintingVisitor([NotNull] ExpressionPrinter expressionPrinter, IndentedStringBuilder stringBuilder) 
                : base(expressionPrinter)
            {
                _stringBuilder = stringBuilder;
            }

            public bool IsSubquery { get; set; }

            public override void VisitMainFromClause(MainFromClause fromClause, QueryModel queryModel)
            {
                if (IsSubquery)
                {
                    _stringBuilder.AppendLine();
                }

                if (queryModel.ResultOperators.Count > 0)
                {
                    _stringBuilder.Append("(");
                }

                _stringBuilder.Append($"from {fromClause.ItemType.ShortDisplayName()} {fromClause.ItemName} in ");
                base.VisitMainFromClause(fromClause, queryModel);
            }

            public override void VisitAdditionalFromClause(AdditionalFromClause fromClause, QueryModel queryModel, int index)
            {
                _stringBuilder.AppendLine();
                _stringBuilder.Append($"from {fromClause.ItemType.ShortDisplayName()} {fromClause.ItemName} in ");
                base.VisitAdditionalFromClause(fromClause, queryModel, index);
            }

            public override void VisitJoinClause(JoinClause joinClause, QueryModel queryModel, int index)
            {
                base.VisitJoinClause(joinClause, queryModel, index);
            }

            public override void VisitJoinClause(JoinClause joinClause, QueryModel queryModel, GroupJoinClause groupJoinClause)
            {
                _stringBuilder.AppendLine();
                _stringBuilder.Append("on ");
                TransformingVisitor.Visit(joinClause.OuterKeySelector);
                _stringBuilder.Append(" equals ");
                TransformingVisitor.Visit(joinClause.InnerKeySelector);
            }

            public override void VisitGroupJoinClause(GroupJoinClause groupJoinClause, QueryModel queryModel, int index)
            {
                _stringBuilder.AppendLine();
                _stringBuilder.Append($"join {groupJoinClause.ItemType.ShortDisplayName()} {groupJoinClause.ItemName}");
                base.VisitGroupJoinClause(groupJoinClause, queryModel, index);
                _stringBuilder.Append($" into {groupJoinClause.ItemName}");
            }

            public override void VisitWhereClause(WhereClause whereClause, QueryModel queryModel, int index)
            {
                _stringBuilder.AppendLine();
                _stringBuilder.Append($"where ");
                base.VisitWhereClause(whereClause, queryModel, index);
            }

            public override void VisitOrderByClause(OrderByClause orderByClause, QueryModel queryModel, int index)
            {
                _stringBuilder.Append("order by ");

                var first = true;
                foreach (var ordering in orderByClause.Orderings)
                {
                    VisitOrdering(ordering, queryModel, orderByClause, index);
                    if (first)
                    {
                        first = false;
                    }
                    else
                    {
                        _stringBuilder.Append(", ");
                    }
                }

                _stringBuilder.AppendLine();
            }

            public override void VisitOrdering(Ordering ordering, QueryModel queryModel, OrderByClause orderByClause, int index)
            {
                base.VisitOrdering(ordering, queryModel, orderByClause, index);

                _stringBuilder.Append($" {ordering.OrderingDirection.ToString().ToLower()}");
            }

            protected override void VisitResultOperators(ObservableCollection<ResultOperatorBase> resultOperators, QueryModel queryModel)
            {
                if (resultOperators.Count > 0)
                {
                    _stringBuilder.Append(")");
                }

                base.VisitResultOperators(resultOperators, queryModel);
            }

            public override void VisitResultOperator(ResultOperatorBase resultOperator, QueryModel queryModel, int index)
            {
                _stringBuilder.AppendLine();
                _stringBuilder.Append($".{resultOperator.ToString()}");
            }

            public override void VisitSelectClause(SelectClause selectClause, QueryModel queryModel)
            {
                _stringBuilder.AppendLine();
                _stringBuilder.Append("select ");
                base.VisitSelectClause(selectClause, queryModel);
            }
        }
    }
}
