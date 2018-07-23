<img src="https://raw.githubusercontent.com/dennyglee/azure-cosmosdb-spark/master/docs/images/azure-cosmos-db-icon.png" width="75">  &nbsp; Azure Cosmos DB BulkExecutor library for .NET
==========================================

The Azure Cosmos DB BulkExecutor library for .NET acts as an extension library to the [Cosmos DB .NET SDK](https://docs.microsoft.com/en-us/azure/cosmos-db/sql-api-sdk-dotnet) and provides developers out-of-the-box functionality to perform bulk operations in [Azure Cosmos DB](http://cosmosdb.com).

------------------------------------------

## A few key concepts before you start 

* [Partitioning For Graph API](https://docs.microsoft.com/en-us/azure/cosmos-db/graph-partitioning) : A collection or a graph represent same underlying CosmosDB concept, and we will use graph and collection interchangeably in this document. Additionally please note that a partitioned graph/collection is equivalent to a unlimited collection/graph.

Unlimited partitions scale automatically, i.e., they start with a fixed number of partitions and then employs more partition as the data grows. Note that the initial number of partitions a graph/collection is configured with is not something a user can control. Users simply provision the RUs (request unit) for a graph, and based on the provisioned RUs, a fixed number of partitions are assigned. Roughly, if you create a collection with X Rus, you will get a collection with max(10, floor(X/6000)) partitions to start with. However, this is an internal formula can change over time.

It is important to note that the above bheavior of an unlimited graph is best suited for organically growing a graph, and may not be the best configuration if one plans to pre-load the graph with large amount of data. For example, if you create an unlimited collection with 1 partition and if you try to upload 1TB data: Firstly, we will be missing out on writing to multiple partitons in parallel, and Secondly, the system will try to provision more partitions at runtime which can in effect slow down the ingestion rate. So for importing large graph, it is recommended that you create a graph with appropriate number of partitions. Also, just to reiterate, as a user you can do this by provisioning right amount of resquest unit during the creation of the graph.


* The tool doesn’t check for the existence of the source or destination vertices while adding an edge. So one can create edges before the corresponding vertices, but it is on the user to make sure that they import the source and destination vertex eventually. 

* BulkImportAsync() supports upsert mode through the usage of a boolean parameter. If you enable this flag, this will let you replace a vertex/edge if they are already present. 
Whether a vertex/edge is already present is determined by whether there already exist a vertex/edge with same id (or same [id, partitionkey] pair for a unlimited collection). 'id' is unique for a fixed collection, while [id, partitionkey] pair is unique for an unlimited collection. We will call this as unique key for a graph. 

Note that, if you have the enableUpsert = false, trying to add vertices/edges with existing id (or, [id, partitionkey] pair) will throw an exception. On the other hand doing the same thing with enableUpsert = true, will replace the vertex/edge. 

So, these need to be handled carefully. With enableUpsert = true, there is no way for the tool to know whether the original intention is to UpSert the vertex or it was due to an error in the application logic that generated two vertices with same id (or [id, partitionkey] pair).

```csharp
BulkImportResponse vResponse =
                await graphBulkImporter.BulkImportAsync(iVertices, enableUpsert:true).ConfigureAwait(false);
                
BulkImportResponse eResponse =
                await graphBulkImporter.BulkImportAsync(iEdges, enableUpsert:true).ConfigureAwait(false);
``` 

------------------------------------------


## Consuming the Microsoft Azure Cosmos DB BulkExecutor .NET library

  This project includes samples, documentation and performance tips for consuming the BulkExecutor library. You can download the official public NuGet package from [here](https://www.nuget.org/packages/Microsoft.Azure.CosmosDB.BulkExecutor/).

------------------------------------------


## Graph Bulk Import API

  Below is the primary API for graph bulk import API. The same API can be used for both vertices and edges as it accepts IEnumerable<object> as input. However these objects needs be of type Microsoft.Azure.CosmosDB.BulkExecutor.Graph.Element.GremlinVertex or Microsoft.Azure.CosmosDB.BulkExecutor.Graph.Element.GremlinEdge. If any other types of objects are provided, the API will reject them by throw an error.

  * With list of JSON-serialized documents
  ```csharp
  Task<BulkImportResponse> BulkImportAsync(
            IEnumerable<object> verticesOrEdges,
            bool enableUpsert = false,
            bool disableAutomaticIdGeneration = true,
            int? maxConcurrencyPerPartitionKeyRange = null,
            int? maxInMemorySortingBatchSize = null,
            CancellationToken cancellationToken = default(CancellationToken));
```

------------------------------------------

## Configurable parameters

* *enableUpsert* : A flag to enable upsert of the documents if document with given id already exists - default value is false.
* *disableAutomaticIdGeneration* : A flag to disable automatic generation of id if absent in the document - default value is true.
* *maxConcurrencyPerPartitionKeyRange* : The maximum degree of concurrency per partition key range, setting to null will cause library to use default value of 20.
* *maxInMemorySortingBatchSize* : The maximum number of documents pulled from the document enumerator passed to the API call in each stage for in-memory pre-processing sorting phase prior to bulk importing, setting to null will cause library to use default value of min(documents.count, 1000000).
* *cancellationToken* : The cancellation token to gracefully exit bulk import.

------------------------------------------


## Bulk import response object definition

The result of the bulk import API call contains the following attributes:
* *NumberOfDocumentsImported* (long) : The total number of documents which were successfully imported out of the documents supplied to the bulk import API call.
* *TotalRequestUnitsConsumed* (double) : The total request units (RU) consumed by the bulk import API call.
* *TotalTimeTaken* (TimeSpan) : The total time taken by the bulk import API call to complete execution.
* *BadInputDocuments* (List\<object\>) : The list of bad-format documents which were not successfully imported in the bulk import API call. User needs to fix the documents returned and retry import. Bad-format documents include documents whose *id* value is not a string (null or any other datatype is considered invalid).

------------------------------------------

## Getting started with bulk import

* Initialize DocumentClient set to Direct TCP connection mode
```csharp
ConnectionPolicy connectionPolicy = new ConnectionPolicy
{
    ConnectionMode = ConnectionMode.Direct,
    ConnectionProtocol = Protocol.Tcp
};
DocumentClient client = new DocumentClient(
    new Uri(endpointUrl),
    authorizationKey,
    connectionPolicy)
```

* Initialize BulkExecutor with high retry option values for the client SDK and then set to 0 to pass congestion control to BulkExector for its lifetime
```csharp
// Set retry options high during initialization (default values).
client.ConnectionPolicy.RetryOptions.MaxRetryWaitTimeInSeconds = 30;
client.ConnectionPolicy.RetryOptions.MaxRetryAttemptsOnThrottledRequests = 9;

IBulkExecutor bulkExecutor = new BulkExecutor(client, dataCollection);
await bulkExecutor.InitializeAsync();

// Set retries to 0 to pass complete control to bulk executor.
client.ConnectionPolicy.RetryOptions.MaxRetryWaitTimeInSeconds = 0;
client.ConnectionPolicy.RetryOptions.MaxRetryAttemptsOnThrottledRequests = 0;
```

* Call BulkImportAsync API
```csharp
BulkImportResponse bulkImportResponse = await bulkExecutor.BulkImportAsync(
    documents: documentsToImportInBatch,
    enableUpsert: true,
    disableAutomaticIdGeneration: true,
    maxConcurrencyPerPartitionKeyRange: null,
    maxInMemorySortingBatchSize: null,
    cancellationToken: token);
```

You can find the complete sample application program consuming the bulk import API [here](https://github.com/Azure/azure-cosmosdb-graph-bulkexecutor-dotnet-getting-started/blob/master/GraphBulkExecutorSample/GraphBulkExecutorSample/Program.cs) - which generates random vertices and edges to be then bulk imported into an Azure Cosmos DB collection. You can configure the application settings in *appSettings* [here](https://github.com/Azure/azure-cosmosdb-graph-bulkexecutor-dotnet-getting-started/blob/master/GraphBulkExecutorSample/GraphBulkExecutorSample/App.config).

You can download the Microsoft.Azure.CosmosDB.BulkExecutor nuget package from [here](https://www.nuget.org/packages/Microsoft.Azure.CosmosDB.BulkExecutor/).

------------------------------------------

## Performance of bulk import sample

- Database location: West US
- Client location: Local machine@ West US
- Client Configuration: Intel i7 @3.6GHz, 4 cores, 8 Logical processor. RAM 32 GB. 
- Number of vertex properties: 2
- Number of edge properties: 1

| collection Type  | RUs provisioned | #Vertices | #Edges | Total time(s) | Writes/s | Average RU/s | Average RU/insert
| ------------- | ------------- | ------------- | ------------- | ------------- |------------- | ------------- | ------------- |
| Fixed (10GB)  | 10,000  | 200K | ~200K | 207.28 | 1930 | 10184 | 10.55 |
| unlimited (100GB)  | 100,000  | 200K | ~200K | 21.28 | 18679 | 83019 | 8.88 |
| Unlimited (830GB)  | 500,000  | 200K | ~200K | 9.63 | 41495 | 163019 | 12.70 |

- Database location: West US
- Client location: Local machine@ West US
- Client Configuration: Intel i7 @3.6GHz, 4 cores, 8 Logical processor. RAM 32 GB. 
- Number of vertex properties: 10
- Number of edge properties: 5

| collection Type  | RUs provisioned | #Vertices | #Edges | Total time(s) | Writes/s | Average RU/s | Average RU/insert
| ------------- | ------------- | ------------- | ------------- | ------------- |------------- | ------------- | ------------- |
| Fixed (10GB)  | 10,000  | 200K | ~200K | 220.28 | 1814 | 10130 | 11.17 |
| unlimited (100 GB)  | 100,000  | 200K | ~200K | 27.7 | 14436 | 92268 | 12.78 |
| Unlimited (830 GB)  | 500,000  | 200K | ~200K | 10.22 | 39120 | 250052 | 12.78 |

* A k GB graph has k/10 partitions each of size 10GB. So, the 830GB graph has 83 partition. 
* These numbers may vary depending on available network bandwidth

------------------------------------------

## API implementation details

When a bulk import API is triggered with a batch of documents, on the client-side, they are first shuffled into buckets corresponding to their target Cosmos DB partition key range. Within each partiton key range bucket, they are broken down into mini-batches and each mini-batch of documents acts as a payload that is committed transactionally.

We have built in optimizations for the concurrent execution of these mini-batches both within and across partition key ranges to maximally utilize the allocated collection throughput. We have designed an [AIMD-style congestion control](https://academic.microsoft.com/#/detail/2158700277?FORM=DACADP) mechanism for each Cosmos DB partition key range **to efficiently handle throttling and timeouts**.

These client-side optimizations augment server-side features specific to the BulkExecutor library which together make maximal consumption of available throughput possible.

------------------------------------------

## Performance tips

* For best performance, run your application **from an Azure VM in the same region as your Cosmos DB account write region**.
* It is advised to instantiate a single *BulkExecutor* object for the entirety of the application within a single VM corresponding to a specific Cosmos DB collection.
* Since a single bulk operation API execution consumes a large chunk of the client machine's CPU and network IO by spawning multiple tasks internally, avoid spawning multiple concurrent tasks within your application process each executing bulk operation API calls. If a single bulk operation API call running on a single VM is unable to consume your entire collection's throughput (if your collection's throughput > 1 million RU/s), preferably spin up separate VMs to concurrently execute bulk operation API calls.
* Ensure *InitializeAsync()* is invoked after instantiating a *BulkExecutor* object to fetch the target Cosmos DB collection partition map.
* In your application's *App.Config*, ensure **gcServer** is enabled for better performance
```csharp
  <runtime>
    <gcServer enabled="true" />
  </runtime>
```
* The library emits traces which can be collected either into a log file or on the console. To enable both, add the following to your application's *App.Config*.
```csharp
  <system.diagnostics>
    <trace autoflush="false" indentsize="4">
      <listeners>
        <add name="logListener" type="System.Diagnostics.TextWriterTraceListener" initializeData="application.log" />
        <add name="consoleListener" type="System.Diagnostics.ConsoleTraceListener" />
      </listeners>
    </trace>
  </system.diagnostics>
```

------------------------------------------

## Troubleshooting

1. Slow Ingestion rate: 
	- Check the distance between the client location and the Azure region where the database is hosted. 
	- Check the configured throughput, ingestion can be slow if the tool is getting [throttled](https://docs.microsoft.com/en-us/azure/cosmos-db/request-units).  It is recommended that you increase the RU/s 
during ingestion and then scale it down later. This can be done programmatically via the [ReplaceOfferAsync() API] (https://docs.microsoft.com/en-us/azure/cosmos-db/set-throughput). 
	- Use a client with high memory, otherwise GC pressure might interrupt the ingestion. 
	- Turn server GC on. 
	- Do you have fixed collection/graph (10GB)? Ingestion can be a bit slower for such collection compared to unlimited collection/graph. Ingestion to a unlimited collection/graph is faster as multiple partitions can be 
filled in parallel, while a single partition is filled in a serial fashion. If you need even faster ingestion for fixed collection, you can partition your data locally and make multiple parallel calls to the
bulk import API.  

2. Are you seeing these exceptions: 
	- Resource already exists: This means a vertex or edge with same unique key (see key concepts 4.) is already present.
	- Provide partition key while adding vertices/edges: For an unlimited graph, you must provide the partition key as a property of the vertex (or partition key property of the source and destination 
	vertex of an edge).
	- Request Rate is Too Large: If you are trying to bulk ingest, while a significant workload is running on the same graph, the tool might get throttled. While the tool can handle such throttling to some 
extent, and prolonged period of throttling might lead the tool to give up. 	
	- Request size is too large: A CosmosDB vertex and edge can have a maximum size of 2MB (please contact the team if you need bigger vertices/edges).
	- Out of Memory Exception: If you have a large number of partitions, and are ingesting a lot of data, the tool would require more memory to operate. We recommend moving with a machine with higher memory. 
	Alternatively, you can split the workload and put multiple machines to work. 

------------------------------------------

# Contributing & feedback

  This project has adopted the [Microsoft Open Source Code of
  Conduct](https://opensource.microsoft.com/codeofconduct/).  For more information
  see the [Code of Conduct
  FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact
  [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional
  questions or comments.

  To give feedback and/or report an issue, open a [GitHub
  Issue](https://help.github.com/articles/creating-an-issue/).

  ------------------------------------------

  ## Other relevant projects

  * [Cosmos DB BulkExecutor library for SQl API ](https://github.com/Azure/azure-cosmosdb-bulkexecutor-dotnet-getting-started)
