//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using Microsoft.Azure.CosmosDB.BulkExecutor.Graph.Element;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;

namespace GraphBulkImportSample
{
    internal sealed class Utils
    {
        /// <summary>
        /// Get the collection if it exists, null if it doesn't.
        /// </summary>
        /// <returns>The requested collection.</returns>
        public static DocumentCollection GetCollectionIfExists(DocumentClient client, string databaseName, string collectionName)
        {
            if (GetDatabaseIfExists(client, databaseName) == null)
            {
                return null;
            }

            return client.CreateDocumentCollectionQuery(UriFactory.CreateDatabaseUri(databaseName))
                .Where(c => c.Id == collectionName).AsEnumerable().FirstOrDefault();
        }

        /// <summary>
        /// Get the database if it exists, null if it doesn't.
        /// </summary>
        /// <returns>The requested database.</returns>
        public static Database GetDatabaseIfExists(DocumentClient client, string databaseName)
        {
            return client.CreateDatabaseQuery().Where(d => d.Id == databaseName).AsEnumerable().FirstOrDefault();
        }

        /// <summary>
        /// Create a partitioned collection.
        /// </summary>
        /// <returns>The created collection.</returns>
        public static async Task<DocumentCollection> CreatePartitionedCollectionAsync(DocumentClient client, string databaseName,
            string collectionName, int collectionThroughput)
        {
            PartitionKeyDefinition partitionKey = new PartitionKeyDefinition
            {
                Paths = new Collection<string> { $"/{ConfigurationManager.AppSettings["CollectionPartitionKey"]}" }
            };
            DocumentCollection collection = new DocumentCollection { Id = collectionName, PartitionKey = partitionKey };

            try
            {
                collection = await client.CreateDocumentCollectionAsync(
                    UriFactory.CreateDatabaseUri(databaseName),
                    collection,
                    new RequestOptions { OfferThroughput = collectionThroughput });
            }
            catch (Exception e)
            {
                throw e;
            }

            return collection;
        }

        public static IEnumerable<GremlinEdge> GenerateEdges(long count)
        {
            for (long i = 0; i < count - 1; i++)
            {
                GremlinEdge e = new GremlinEdge(
                    "e" + i,
                    "knows",
                    i.ToString(),
                    (i + 1).ToString(),
                    "vertex",
                    "vertex",
                    i,
                    i + 1);

                e.AddProperty("duration", i);

                yield return e;
            }
        }

        public static IEnumerable<GremlinVertex> GenerateVertices(long count)
        {
            // CosmosDB currently doesn't support documents with id length > 1000
            GremlinVertex vBad = new GremlinVertex(getLongId(), "vertex");
            vBad.AddProperty(ConfigurationManager.AppSettings["CollectionPartitionKey"], 0);
            yield return vBad;

            for (long i = 0; i < count; i++)
            {
                GremlinVertex v = new GremlinVertex(i.ToString(), "vertex");
                v.AddProperty(ConfigurationManager.AppSettings["CollectionPartitionKey"], i);
                v.AddProperty("name1", "name" + i);
                v.AddProperty("name2", i * 2);
                v.AddProperty("name3", i * 3);
                v.AddProperty("name4", i + 100);

                yield return v;
            }
        }

        private static string getLongId()
        {
            return new string('1', 2000);
        }
    }
}
