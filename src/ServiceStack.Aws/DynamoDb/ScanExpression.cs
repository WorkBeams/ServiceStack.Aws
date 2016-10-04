﻿// Copyright (c) Service Stack LLC. All Rights Reserved.
// License: https://raw.github.com/ServiceStack/ServiceStack/master/license.txt

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace ServiceStack.Aws.DynamoDb
{
    public class ScanExpression : ScanRequest, IDynamoCommonQuery
    {
        protected IPocoDynamo Db { get; set; }

        protected DynamoMetadataType Table { get; set; }

        public ScanExpression Projection<TModel>()
        {
            this.SelectFields(typeof(TModel).AllFields().Where(Table.HasField));
            return this;
        }

        public void AddArguments(Dictionary<string, object> args)
        {
            if (args != null)
            {
                Db.ToExpressionAttributeValues(args).Each(x =>
                    ExpressionAttributeValues[x.Key] = x.Value);
            }
        }
    }

    public class ScanExpression<T> : ScanExpression
    {
        public ScanExpression(IPocoDynamo db)
            : this(db, db.GetTableMetadata(typeof(T))) {}

        public ScanExpression(IPocoDynamo db, DynamoMetadataType table)
        {
            this.Db = db;
            this.Table = table;
            this.TableName = this.Table.Name;
        }

        public ScanExpression<T> Clone()
        {
            var q = new ScanExpression<T>(Db)
            {
                Table = Table,
                TableName = TableName,
                AttributesToGet = new List<string>(AttributesToGet),
                ConditionalOperator = ConditionalOperator,
                ConsistentRead = ConsistentRead,
                ExclusiveStartKey = new Dictionary<string, AttributeValue>(ExclusiveStartKey),
                ExpressionAttributeNames = new Dictionary<string, string>(ExpressionAttributeNames),
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>(ExpressionAttributeValues),
                FilterExpression = FilterExpression,
                IndexName = IndexName,
                Limit = Limit,
                ProjectionExpression = ProjectionExpression,
                ScanFilter = new Dictionary<string, Condition>(ScanFilter),
                ReturnConsumedCapacity = ReturnConsumedCapacity,
                Segment = Segment,
                TotalSegments = TotalSegments,                
            }.SetSelect(base.Select);

#if !NETSTANDARD1_6
            if (ReadWriteTimeoutInternal != null)
                q.ReadWriteTimeoutInternal = ReadWriteTimeoutInternal;
            if (TimeoutInternal != null)
                q.TimeoutInternal = TimeoutInternal;
#endif

            return q;
        }

        internal ScanExpression<T> SetSelect(Select select)
        {
            base.Select = select;
            return this;
        }

        public ScanExpression<T> AddFilterExpression(string filterExpression)
        {
            if (this.FilterExpression == null)
                this.FilterExpression = filterExpression;
            else
                this.FilterExpression += " AND " + filterExpression;

            return this;
        }
        
        public ScanExpression<T> IndexCondition(Expression<Func<T, bool>> keyExpression, string indexName = null)
        {
            var q = PocoDynamoExpression.Create(typeof(T), keyExpression, paramPrefix: "i");

            if (q.ReferencedFields.Distinct().Count() != 1)
                throw new ArgumentException("Only 1 Index can be queried per QueryRequest");

            if (indexName == null)
            {
                var indexField = q.ReferencedFields.First();
                var index = q.Table.GetIndexByField(indexField);

                if (index == null)
                    throw new ArgumentException($"Could not find index for field '{indexField}'");

                this.IndexName = index.Name;
            }
            else
            {
                this.IndexName = indexName;
            }

            AddFilterExpression(q.FilterExpression);

            Db.ToExpressionAttributeValues(q.Params).Each(x =>
                this.ExpressionAttributeValues[x.Key] = x.Value);

            return this;
        }

        public ScanExpression<T> Filter(string filterExpression, Dictionary<string, object> args = null)
        {
            AddFilterExpression(filterExpression);

            if (args != null)
            {
                Db.ToExpressionAttributeValues(args).Each(x =>
                    this.ExpressionAttributeValues[x.Key] = x.Value);
            }

            return this;
        }

        public ScanExpression<T> Filter(string filterExpression, object args)
        {
            return Filter(filterExpression, args.ToObjectDictionary());
        }

        public ScanExpression<T> Filter(Expression<Func<T, bool>> filterExpression)
        {
            var q = PocoDynamoExpression.Create(typeof(T), filterExpression, paramPrefix: "p");
            return Filter(q.FilterExpression, q.Params);
        }
        
        public ScanExpression<T> PagingLimit(int limit)
        {
            this.Limit = limit;
            return this;
        }

        public ScanExpression<T> Select(IEnumerable<string> fields)
        {
            this.SelectFields(fields);
            return this;
        }

        /// <summary>
        /// Select all table fields, useful when querying an index with only a partial field set
        /// </summary>
        public ScanExpression<T> SelectIndexedTableFields()
        {
            return Select(Table.Fields.Map(x => x.Name));
        }

        public ScanExpression<T> Select<TModel>()
        {
            return Select(typeof(TModel).AllFields().Where(Table.HasField));
        }

        public ScanExpression<T> Select(Func<T, object> fields)
        {
            return Select(fields(typeof(T).CreateInstance<T>()).GetType().AllFields());
        }

        public ScanExpression<T> Select<TModel>(Func<T, object> fields)
        {
            return Select(fields(typeof(TModel).CreateInstance<T>()).GetType().AllFields()
                .Where(Table.HasField));
        }

        public IEnumerable<T> Exec()
        {
            return Db.Scan(this);
        }

        public List<T> Exec(int limit)
        {
            return Db.Scan(this, limit);
        }

        public IEnumerable<Into> ExecInto<Into>()
        {
            return Db.Scan<Into>(this.Projection<Into>());
        }

        public List<Into> Exec<Into>(int limit)
        {
            return Db.Scan<Into>(this.Projection<Into>(), limit:limit);
        }

        public IEnumerable<TKey> ExecColumn<TKey>(Expression<Func<T, TKey>> fields)
        {
            var q = new PocoDynamoExpression(typeof(T)).Parse(fields);
            var field = q.ReferencedFields[0];
            this.ProjectionExpression = field;

            foreach (var attrValue in Db.Scan(this))
            {
                object value = Table.GetField(field).GetValue(attrValue);
                yield return (TKey)value;
            }
        }
    }
}