using System.Diagnostics.CodeAnalysis;

namespace Milvus.Client;

/// <summary>
/// Represents a Milvus collection, and is the starting point for all operations involving one.
/// </summary>
[SuppressMessage("Error", "CA1711", Justification = "Concrete class also disables this rule")]
public interface IMilvusCollection
{
    /// <summary>
    /// The name of the collection.
    /// </summary>
    public string Name { get; }

    // ---- Collection ----

    /// <summary>
    /// Describes a collection, returning information about its configuration and schema.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>A description of the collection.</returns>
    public Task<MilvusCollectionDescription> DescribeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a collection.
    /// </summary>
    /// <param name="newName">The new collection name.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task RenameAsync(string newName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops a collection.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task DropAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the current number of entities in the collection. Call
    /// <see cref="FlushAsync(System.Threading.CancellationToken)" /> before invoking this method to ensure up-to-date
    /// results.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>The number of entities currently in the collection.</returns>
    public Task<int> GetEntityCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a collection into memory so that it can be searched or queried.
    /// </summary>
    /// <param name="replicaNumber">An optional replica number to load.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task LoadAsync(int? replicaNumber = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a collection that has been previously loaded.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public Task ReleaseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the loading progress for a collection, and optionally one or more of its partitions.
    /// </summary>
    /// <param name="partitionNames">
    /// An optional list of partition names for which to check the loading progress.
    /// </param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>The loading progress of the collection.</returns>
    public Task<long> GetLoadingProgressAsync(
        IReadOnlyList<string>? partitionNames = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls Milvus for loading progress of a collection until it is fully loaded.
    /// To perform a single progress check, use <see cref="GetLoadingProgressAsync" />.
    /// </summary>
    /// <param name="partitionNames">
    /// An optional list of partition names for which to check the loading progress.
    /// </param>
    /// <param name="waitingInterval">Waiting interval. Defaults to 500 milliseconds.</param>
    /// <param name="timeout">How long to poll for before throwing a <see cref="TimeoutException" />.</param>
    /// <param name="progress">Provides information about the progress of the loading operation.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task WaitForCollectionLoadAsync(
        IReadOnlyList<string>? partitionNames = null,
        TimeSpan? waitingInterval = null,
        TimeSpan? timeout = null,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compacts the collection.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>The compaction ID.</returns>
    public Task<long> CompactAsync(CancellationToken cancellationToken = default);

    // ---- Entity ----

    /// <summary>
    /// Inserts rows of data into a collection.
    /// </summary>
    /// <param name="data">The field data to insert; each field contains a list of row values.</param>
    /// <param name="partitionName">An optional name of a partition to insert into.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task<MutationResult> InsertAsync(
        IReadOnlyList<FieldData> data,
        string? partitionName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts rows of data into a collection.
    /// </summary>
    /// <param name="data">The field data to upsert; each field contains a list of row values.</param>
    /// <param name="partitionName">An optional name of a partition to upsert into.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task<MutationResult> UpsertAsync(
        IReadOnlyList<FieldData> data,
        string? partitionName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes rows from a collection by given expression.
    /// </summary>
    /// <param name="expression">A boolean expression determining which rows are to be deleted.</param>
    /// <param name="partitionName">An optional name of a partition from which rows are to be deleted.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>A <see cref="MutationResult" /> containing information about the drop operation.</returns>
    public Task<MutationResult> DeleteAsync(
        string expression,
        string? partitionName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Perform a vector similarity search.
    /// </summary>
    /// <param name="vectorFieldName">The name of the vector field to search in.</param>
    /// <param name="vectors">The set of vectors to send as input for the similarity search.</param>
    /// <param name="metricType">
    /// Method used to measure the distance between vectors during search. Must correspond to the metric type specified
    /// when building the index.
    /// </param>
    /// <param name="limit">
    /// The maximum number of records to return, also known as 'topk'. Must be between 1 and 16384.
    /// </param>
    /// <param name="parameters">
    /// Various additional optional parameters to configure the similarity search.
    /// </param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task<SearchResults> SearchAsync<T>(
        string vectorFieldName,
        IReadOnlyList<ReadOnlyMemory<T>> vectors,
        SimilarityMetricType metricType,
        int limit,
        SearchParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Perform a sparse vector similarity search. Available since Milvus v2.4.
    /// </summary>
    /// <param name="vectorFieldName">The name of the sparse vector field to search in.</param>
    /// <param name="vectors">The set of sparse vectors to send as input for the similarity search.</param>
    /// <param name="metricType">
    /// Method used to measure the distance between vectors during search. Must correspond to the metric type specified
    /// when building the index. For sparse vectors, typically <see cref="SimilarityMetricType.Ip" /> is used.
    /// </param>
    /// <param name="limit">
    /// The maximum number of records to return, also known as 'topk'. Must be between 1 and 16384.
    /// </param>
    /// <param name="parameters">
    /// Various additional optional parameters to configure the similarity search.
    /// </param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task<SearchResults> SearchAsync<T>(
        string vectorFieldName,
        IReadOnlyList<MilvusSparseVector<T>> vectors,
        SimilarityMetricType metricType,
        int limit,
        SearchParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Perform a hybrid vector similarity search combining multiple ANN searches with reranking.
    /// </summary>
    /// <param name="requests">The ANN search requests to combine.</param>
    /// <param name="reranker">The reranker to use for combining results.</param>
    /// <param name="limit">
    /// The maximum number of records to return, also known as 'topk'. Must be between 1 and 16384.
    /// </param>
    /// <param name="parameters">Various additional optional parameters to configure the hybrid search.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>The search results.</returns>
    public Task<SearchResults> HybridSearchAsync(
        IReadOnlyList<AnnSearchRequest> requests,
        IReranker reranker,
        int limit,
        HybridSearchParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes collection data to disk, required only in order to get up-to-date statistics.
    /// </summary>
    /// <remarks>
    /// This method will be removed in a future version.
    /// </remarks>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>A <see cref="FlushResult" /> containing information about the flush operation.</returns>
    public Task<FlushResult> FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns sealed segments information of a collection.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public Task<IReadOnlyList<PersistentSegmentInfo>> GetPersistentSegmentInfosAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves rows from a collection via scalar filtering based on a boolean expression.
    /// </summary>
    /// <param name="expression">A boolean expression determining which rows are to be returned.</param>
    /// <param name="parameters">Various additional optional parameters to configure the query.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>A list of <see cref="FieldData{TData}" /> instances with the query results.</returns>
    public Task<IReadOnlyList<FieldData>> QueryAsync(
        string expression,
        QueryParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves rows from a collection via scalar filtering based on a boolean expression using iterator.
    /// </summary>
    /// <param name="expression">A boolean expression determining which rows are to be returned.</param>
    /// <param name="batchSize">Batch size that will be used for every iteration request. Must be between 1 and 16384.</param>
    /// <param name="parameters">Various additional optional parameters to configure the query.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>A list of <see cref="FieldData{TData}" /> instances with the query results.</returns>
    public IAsyncEnumerable<IReadOnlyList<FieldData>> QueryWithIteratorAsync(
        string? expression = null,
        int batchSize = 1000,
        QueryParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get query segment information.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns><see cref="QuerySegmentInfoResult"/></returns>
    public Task<IReadOnlyList<QuerySegmentInfoResult>> GetQuerySegmentInfoAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Flush and polls Milvus for the flush state util it is fully flush.
    /// </summary>
    /// <param name="waitingInterval">Waiting interval. Defaults to 500 milliseconds.</param>
    /// <param name="timeout">How long to poll for before throwing a <see cref="TimeoutException" />.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task WaitForFlushAsync(
        TimeSpan? waitingInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    // ---- Index ----

    /// <summary>
    /// Creates an index.
    /// </summary>
    /// <param name="fieldName">The name of the field in the collection for which the index will be created.</param>
    /// <param name="indexType">The type of the index to be created.</param>
    /// <param name="metricType">Method used to measure the distance between vectors during search.</param>
    /// <param name="indexName">An optional name for the index to be created.</param>
    /// <param name="extraParams">
    /// Extra parameters specific to each index type; consult the documentation for your index type for more details.
    /// </param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task CreateIndexAsync(
        string fieldName,
        IndexType? indexType = null,
        SimilarityMetricType? metricType = null,
        string? indexName = null,
        IDictionary<string, string>? extraParams = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops an index.
    /// </summary>
    /// <param name="fieldName">The name of the field which has the index to be dropped.</param>
    /// <param name="indexName">An optional name of the index to be dropped.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task DropIndexAsync(
        string fieldName,
        string? indexName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Describes an index, returning information about its configuration.
    /// </summary>
    /// <param name="fieldName">The name of the field which has the index to be described.</param>
    /// <param name="indexName">An optional name of the index to be described.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>A list of <see cref="MilvusIndexInfo" /> containing information about the matching indexes.</returns>
    public Task<IList<MilvusIndexInfo>> DescribeIndexAsync(
        string fieldName,
        string? indexName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the state of an index.
    /// </summary>
    /// <param name="fieldName">The name of the field which has the index to get the state for.</param>
    /// <param name="indexName">An optional name of the index to get the state for.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    [Obsolete("Use DescribeIndex instead")]
    public Task<IndexState> GetIndexStateAsync(
        string fieldName,
        string? indexName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the build progress of an index.
    /// </summary>
    /// <param name="fieldName">The name of the field which has the index.</param>
    /// <param name="indexName">An optional name of the index.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>
    /// An <see cref="IndexBuildProgress" /> with the number of rows indexed and the total number of rows.
    /// </returns>
    [Obsolete("Use DescribeIndex instead")]
    public Task<IndexBuildProgress> GetIndexBuildProgressAsync(
        string fieldName,
        string? indexName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls Milvus for building progress of an index until it is fully built.
    /// To perform a single progress check, use <see cref="GetIndexBuildProgressAsync" />.
    /// </summary>
    /// <param name="fieldName">The name of the field which has the index.</param>
    /// <param name="indexName">An optional name of the index.</param>
    /// <param name="waitingInterval">Waiting interval. Defaults to 500 milliseconds.</param>
    /// <param name="timeout">How long to poll for before throwing a <see cref="TimeoutException" />.</param>
    /// <param name="progress">Provides information about the progress of the loading operation.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task WaitForIndexBuildAsync(
        string fieldName,
        string? indexName = null,
        TimeSpan? waitingInterval = null,
        TimeSpan? timeout = null,
        IProgress<IndexBuildProgress>? progress = null,
        CancellationToken cancellationToken = default);

    // ---- Partition ----

    /// <summary>
    /// Creates a partition.
    /// </summary>
    /// <param name="partitionName">The name of partition to be created.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task CreatePartitionAsync(string partitionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a partition exists.
    /// </summary>
    /// <param name="partitionName">The name of the partition to be checked.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>Whether the partition exists or not.</returns>
    public Task<bool> HasPartitionAsync(string partitionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all partitions defined for a collection.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>
    /// A list of <see cref="MilvusPartition" /> instances providing information about all partitions in the collection.
    /// </returns>
    public Task<IList<MilvusPartition>> ShowPartitionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a partition into memory so that it can be searched or queries.
    /// </summary>
    /// <param name="partitionName">The name of the partition to be loaded.</param>
    /// <param name="replicaNumber">An optional replica number to load.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task LoadPartitionAsync(
        string partitionName,
        int replicaNumber = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads partitions into memory so that they can be searched or queries.
    /// </summary>
    /// <param name="partitionNames">The names of the partitions to be loaded.</param>
    /// <param name="replicaNumber">An optional replica number to load.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task LoadPartitionsAsync(
        IReadOnlyList<string> partitionNames,
        int replicaNumber = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a loaded partition from memory.
    /// </summary>
    /// <param name="partitionName">The name of the partition to be released.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task ReleasePartitionAsync(string partitionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases loaded partitions from memory.
    /// </summary>
    /// <param name="partitionNames">The names of the partitions to be released.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task ReleasePartitionsAsync(
        IReadOnlyList<string> partitionNames,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops a partition.
    /// </summary>
    /// <param name="partitionName">The name of the partition to be dropped.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    public Task DropPartitionAsync(string partitionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves statistics for a partition.
    /// </summary>
    /// <param name="partitionName">The name of partition for which statistics are to be retrieved.</param>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>A dictionary containing statistics about the partition.</returns>
    public Task<IDictionary<string, string>> GetPartitionStatisticsAsync(
        string partitionName,
        CancellationToken cancellationToken = default);
}

