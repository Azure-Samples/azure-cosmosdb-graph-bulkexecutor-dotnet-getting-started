﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace GraphBulkImportSample
{
    using System;
    using System.Configuration;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.Azure.Documents;
    using Microsoft.Azure.Documents.Client;
    using Microsoft.Azure.CosmosDB.BulkExecutor;
    using Microsoft.Azure.CosmosDB.BulkExecutor.BulkImport;
    using Microsoft.Azure.CosmosDB.BulkExecutor.Graph;
    using System.Collections.Generic;

    class Program
    {
        private static readonly string EndpointUrl = ConfigurationManager.AppSettings["EndPointUrl"];
        private static readonly string AuthorizationKey = ConfigurationManager.AppSettings["AuthorizationKey"];
        private static readonly string DatabaseName = ConfigurationManager.AppSettings["DatabaseName"];
        private static readonly string CollectionName = ConfigurationManager.AppSettings["CollectionName"];
        private static readonly int CollectionThroughput = int.Parse(ConfigurationManager.AppSettings["CollectionThroughput"]);

        private static readonly ConnectionPolicy ConnectionPolicy = new ConnectionPolicy
        {
            ConnectionMode = ConnectionMode.Direct,
            ConnectionProtocol = Protocol.Tcp
        };

        private DocumentClient client;

        /// <summary>
        /// Initializes a new instance of the <see cref="Program"/> class.
        /// </summary>
        /// <param name="client">The DocumentDB client instance.</param>
        private Program(DocumentClient client)
        {
            this.client = client;
        }

        public static void Main(string[] args)
        {
            Trace.WriteLine("Summary:");
            Trace.WriteLine("--------------------------------------------------------------------- ");
            Trace.WriteLine(String.Format("Endpoint: {0}", EndpointUrl));
            Trace.WriteLine(String.Format("Collection : {0}.{1}", DatabaseName, CollectionName));
            Trace.WriteLine("--------------------------------------------------------------------- ");
            Trace.WriteLine("");

            try
            {
                using (var client = new DocumentClient(
                    new Uri(EndpointUrl),
                    AuthorizationKey,
                    ConnectionPolicy))
                {
                    var program = new Program(client);
                    program.RunBulkImportAsync().Wait();
                }
            }
            catch (AggregateException e)
            {
                Trace.TraceError("Caught AggregateException in Main, Inner Exception:\n" + e);
                Console.ReadKey();
            }

        }

        /// <summary>
        /// Driver function for bulk import.
        /// </summary>
        /// <returns></returns>
        private async Task RunBulkImportAsync()
        {
            // Cleanup on start if set in config.

            DocumentCollection dataCollection = null;
            try
            {
                if (bool.Parse(ConfigurationManager.AppSettings["ShouldCleanupOnStart"]))
                {
                    Database database = Utils.GetDatabaseIfExists(client, DatabaseName);
                    if (database != null)
                    {
                        await client.DeleteDatabaseAsync(database.SelfLink);
                    }

                    Trace.TraceInformation("Creating database {0}", DatabaseName);
                    database = await client.CreateDatabaseAsync(new Database { Id = DatabaseName });

                    Trace.TraceInformation(String.Format("Creating collection {0} with {1} RU/s", CollectionName, CollectionThroughput));
                    dataCollection = await Utils.CreatePartitionedCollectionAsync(client, DatabaseName, CollectionName, CollectionThroughput);
                }
                else
                {
                    dataCollection = Utils.GetCollectionIfExists(client, DatabaseName, CollectionName);
                    if (dataCollection == null)
                    {
                        throw new Exception("The data collection does not exist");
                    }
                }
            }
            catch (Exception de)
            {
                Trace.TraceError("Unable to initialize, exception message: {0}", de.Message);
                throw;
            }

            // Prepare for bulk import.

            // Creating documents with simple partition key here.
            string partitionKeyProperty = dataCollection.PartitionKey.Paths[0].Replace("/", "");
            long numberOfDocumentsToGenerate = long.Parse(ConfigurationManager.AppSettings["NumberOfDocumentsToImport"]);

            // Set retry options high for initialization (default values).
            client.ConnectionPolicy.RetryOptions.MaxRetryWaitTimeInSeconds = 30;
            client.ConnectionPolicy.RetryOptions.MaxRetryAttemptsOnThrottledRequests = 9;

            IBulkExecutor graphbulkExecutor = new GraphBulkExecutor(client, dataCollection);
            await graphbulkExecutor.InitializeAsync();

            // Set retries to 0 to pass control to bulk executor.
            client.ConnectionPolicy.RetryOptions.MaxRetryWaitTimeInSeconds = 0;
            client.ConnectionPolicy.RetryOptions.MaxRetryAttemptsOnThrottledRequests = 0;

            var tokenSource = new CancellationTokenSource();
            var token = tokenSource.Token;

            BulkImportResponse vResponse = null;
            BulkImportResponse eResponse = null;

            try
            {
                vResponse = await graphbulkExecutor.BulkImportAsync(
                        Utils.GenerateVertices(numberOfDocumentsToGenerate),
                        enableUpsert: true,
                        disableAutomaticIdGeneration: true,
                        maxConcurrencyPerPartitionKeyRange: null,
                        maxInMemorySortingBatchSize: null,
                        cancellationToken: token);

                eResponse = await graphbulkExecutor.BulkImportAsync(
                        Utils.GenerateEdges(numberOfDocumentsToGenerate),
                        enableUpsert: true,
                        disableAutomaticIdGeneration: true,
                        maxConcurrencyPerPartitionKeyRange: null,
                        maxInMemorySortingBatchSize: null,
                        cancellationToken: token);
            }
            catch (DocumentClientException de)
            {
                Trace.TraceError("Document client exception: {0}", de);
            }
            catch (Exception e)
            {
                Trace.TraceError("Exception: {0}", e);
            }

            Console.WriteLine("\nSummary for batch");
            Console.WriteLine("--------------------------------------------------------------------- ");
            Console.WriteLine(
                "Inserted {0} graph elements ({1} vertices, {2} edges) @ {3} writes/s, {4} RU/s in {5} sec)",
                vResponse.NumberOfDocumentsImported + eResponse.NumberOfDocumentsImported,
                vResponse.NumberOfDocumentsImported,
                eResponse.NumberOfDocumentsImported,
                Math.Round(
                    (vResponse.NumberOfDocumentsImported) /
                    (vResponse.TotalTimeTaken.TotalSeconds + eResponse.TotalTimeTaken.TotalSeconds)),
                Math.Round(
                    (vResponse.TotalRequestUnitsConsumed + eResponse.TotalRequestUnitsConsumed) /
                    (vResponse.TotalTimeTaken.TotalSeconds + eResponse.TotalTimeTaken.TotalSeconds)),
                vResponse.TotalTimeTaken.TotalSeconds + eResponse.TotalTimeTaken.TotalSeconds);
            Console.WriteLine(
                "Average RU consumption per insert: {0}",
                (vResponse.TotalRequestUnitsConsumed + eResponse.TotalRequestUnitsConsumed) /
                (vResponse.NumberOfDocumentsImported + eResponse.NumberOfDocumentsImported));
            Console.WriteLine("---------------------------------------------------------------------\n ");


            // For responses which failed to import all elements, BulkImportResponse.FailedImports
            // will include the batches of elements that failed along with the cause exception.
            // Here you may want to do programmatic recovery operations based on the type of exception in 
            // BulkImportFailure.BulkImportFailureException, such as skipping or retrying.
            //
            // This sample writes the failed elements to files for review.
            // NOTE: This does not write out the cause exception.
            if (vResponse.FailedImports.Count > 0)
            {
                CreateErrorDump(@".\FailedVertices.txt", vResponse.FailedImports.SelectMany(f => f.DocumentsFailedToImport));
            }

            if (eResponse.FailedImports.Count > 0)
            {
                CreateErrorDump(@".\FailedVertices.txt", eResponse.FailedImports.SelectMany(f => f.DocumentsFailedToImport));
            }


            // BulkImportResponse.BadInputDocuments are for elements whose content was malformed.
            // Generally we don't expect to see errors for these provided GremlinVertex and GremlinEdge
            // objects are used to BulkImportAsync().
            if (vResponse.BadInputDocuments.Count > 0)
            {
                CreateErrorDump(@".\BadVertices.txt", vResponse.BadInputDocuments);
            }

            if (eResponse.BadInputDocuments.Count > 0)
            {
                CreateErrorDump(@".\BadVertices.txt", eResponse.BadInputDocuments);
            }

            // Cleanup on finish if set in config.
            if (bool.Parse(ConfigurationManager.AppSettings["ShouldCleanupOnFinish"]))
            {
                Trace.TraceInformation("Deleting Database {0}", DatabaseName);
                await client.DeleteDatabaseAsync(UriFactory.CreateDatabaseUri(DatabaseName));
            }

            Trace.WriteLine("\nPress any key to exit.");
            Console.ReadKey();
        }

        private static void CreateErrorDump(string fileName, IEnumerable<object> docs)
        {
            using (System.IO.StreamWriter file = new System.IO.StreamWriter(fileName, true))
            {
                foreach (object doc in docs)
                {
                    file.WriteLine(doc);
                }
            }
        }
    }
}
