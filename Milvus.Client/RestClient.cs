using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Milvus.Client;

internal sealed class RestClient(HttpClient httpClient, string? database, DateTime? deadline) : IDisposable
{
    private readonly ConcurrentDictionary<string, MilvusCollectionDescription> _metadata = new();
    private readonly SemaphoreSlim _metadataLock = new(1, 1);
    private readonly object _metadataSync = new();
    private int _metadataVersion;

    public void Dispose() => _metadataLock.Dispose();

    internal int MetadataVersion => Volatile.Read(ref _metadataVersion);

    internal void InvalidateMetadata()
    {
        lock (_metadataSync)
        {
            _metadataVersion++;
            _metadata.Clear();
        }
    }

    internal void RememberMetadata(string name, MilvusCollectionDescription description, int version)
    {
        lock (_metadataSync)
        {
            // An alias can change while Describe is in flight; do not restore the stale entry.
            if (version == _metadataVersion) _metadata[name] = description;
        }
    }

    internal Task<SearchResults> SearchAsync<T>(MilvusCollection collection, string field,
        IReadOnlyList<ReadOnlyMemory<T>> vectors, SimilarityMetricType metric, int limit,
        SearchParameters? parameters, CancellationToken cancellationToken)
        => SearchAsync(collection, field, writer => WriteDense(writer, vectors), vectors.Count,
            metric, limit, parameters, cancellationToken);

    internal Task<SearchResults> SearchAsync<T>(MilvusCollection collection, string field,
        IReadOnlyList<MilvusSparseVector<T>> vectors, SimilarityMetricType metric, int limit,
        SearchParameters? parameters, CancellationToken cancellationToken)
        => SearchAsync(collection, field, writer => WriteSparse(writer, vectors), vectors.Count,
            metric, limit, parameters, cancellationToken);

    private Task<SearchResults> SearchAsync(MilvusCollection collection, string field,
        Action<Utf8JsonWriter> writeData, int numQueries, SimilarityMetricType metric, int limit,
        SearchParameters? parameters, CancellationToken cancellationToken)
    {
        ValidateSearchOptions(parameters?.RoundDecimal, parameters?.GroupByField,
            parameters?.GroupSize, parameters?.StrictGroupSize);
        return ExecuteAsync(collection, "search", parameters?.ConsistencyLevel,
            parameters?.GuaranteeTimestamp, parameters?.TimeTravelTimestamp,
            (writer, description) =>
            {
                WriteCommon(writer, collection.Name, parameters?.PartitionNamesInternal, parameters?.OutputFieldsInternal);
                writer.WriteString("annsField", field);
                writer.WriteString("filter", parameters?.Expression ?? "");
                writer.WriteNumber("limit", limit);
                if (parameters?.Offset is { } offset) writer.WriteNumber("offset", offset);
                writer.WritePropertyName("data");
                writeData(writer);
                WriteSearchParams(writer, "searchParams", metric, parameters?.ExtraParameters,
                    parameters?.IgnoreGrowing, parameters?.GracefulTime);
                WriteConsistency(writer, parameters?.ConsistencyLevel ?? description.ConsistencyLevel);
            },
            (root, description) => RestResultReader.ReadSearch(root, description, collection.Name, numQueries,
                parameters?.OutputFieldsInternal), cancellationToken);
    }

    internal Task<SearchResults> HybridSearchAsync(MilvusCollection collection,
        IReadOnlyList<AnnSearchRequest> requests, IReranker reranker, int limit,
        HybridSearchParameters? parameters, CancellationToken cancellationToken)
    {
        Verify.NotNullOrEmpty(requests);
        Verify.NotNull(reranker);
        if (limit < 1 || limit > 16384) throw new ArgumentOutOfRangeException(nameof(limit));
        if (reranker is WeightedReranker weighted && weighted.Weights.Count != requests.Count)
            throw new ArgumentException("The number of weights must match the number of searches.", nameof(reranker));
        ValidateSearchOptions(parameters?.RoundDecimal, parameters?.GroupByField,
            parameters?.GroupSize, parameters?.StrictGroupSize);
        if (parameters?.IgnoreGrowing is not null)
            throw new NotSupportedException("REST hybrid search requires IgnoreGrowing on individual requests.");
        int numQueries = GetQueryCount(requests[0]);
        if (requests.Any(request => GetQueryCount(request) != numQueries))
            throw new ArgumentException("All hybrid searches must have the same number of query vectors.", nameof(requests));

        return ExecuteAsync(collection, "hybrid_search", parameters?.ConsistencyLevel,
            parameters?.GuaranteeTimestamp, parameters?.TimeTravelTimestamp,
            (writer, description) =>
            {
                WriteCommon(writer, collection.Name, parameters?.PartitionNamesInternal, parameters?.OutputFieldsInternal);
                writer.WriteNumber("limit", limit);
                WriteConsistency(writer, parameters?.ConsistencyLevel ?? description.ConsistencyLevel);
                writer.WriteStartArray("search");
                foreach (AnnSearchRequest request in requests)
                {
                    writer.WriteStartObject();
                    writer.WriteString("annsField", request.VectorFieldName);
                    writer.WriteString("filter", request.Expression ?? "");
                    writer.WriteNumber("limit", request.Limit);
                    writer.WritePropertyName("data");
                    WriteAnnData(writer, request);
                    WriteSearchParams(writer, "params", request.MetricType, request.ExtraParameters,
                        request.IgnoreGrowing, gracefulTime: null);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteStartObject("rerank");
                foreach (Grpc.KeyValuePair parameter in reranker.ToRankParams())
                {
                    writer.WritePropertyName(parameter.Key);
                    if (parameter.Key == "params")
                    {
                        using JsonDocument value = JsonDocument.Parse(parameter.Value);
                        value.RootElement.WriteTo(writer);
                    }
                    else writer.WriteStringValue(parameter.Value);
                }
                writer.WriteEndObject();
            },
            (root, description) => RestResultReader.ReadSearch(root, description, collection.Name, numQueries,
                parameters?.OutputFieldsInternal, hybridLimit: limit), cancellationToken);
    }

    internal Task<IReadOnlyList<FieldData>> QueryAsync(MilvusCollection collection, string expression,
        QueryParameters? parameters, CancellationToken cancellationToken)
    {
        if (parameters?.Limit is <= 0)
            throw new ArgumentOutOfRangeException(nameof(parameters), "Query limit must be positive when specified.");
        if (parameters?.Limit is not null && parameters.Offset is < 0)
            throw new ArgumentOutOfRangeException(nameof(parameters), "Query offset cannot be negative.");
        if (parameters?.Limit is not null && parameters.OutputFieldsInternal is { Count: 1 } outputs
            && string.Equals(outputs[0].Trim(), "count(*)", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Count queries do not support a limit.", nameof(parameters));
        return ExecuteAsync(collection, "query", parameters?.ConsistencyLevel,
            parameters?.GuaranteeTimestamp, parameters?.TimeTravelTimestamp,
            (writer, description) =>
            {
                // REST otherwise defaults to all fields and a 100-row limit, unlike gRPC.
                WriteCommon(writer, collection.Name, parameters?.PartitionNamesInternal,
                    parameters?.OutputFieldsInternal ?? (IReadOnlyList<string>)Array.Empty<string>());
                writer.WriteString("filter", expression);
                writer.WriteNumber("limit", parameters?.Limit ?? 0);
                if (parameters?.Offset is { } offset) writer.WriteNumber("offset", offset);
                WriteConsistency(writer, parameters?.ConsistencyLevel ?? description.ConsistencyLevel);
            },
            (root, description) => RestResultReader.ReadFields(root.GetProperty("data"), description,
                searchOutputFields: null, isSearch: false), cancellationToken);
    }

    private async Task<T> ExecuteAsync<T>(MilvusCollection collection, string operation,
        ConsistencyLevel? consistency, ulong? guaranteeTimestamp, ulong? travelTimestamp,
        Action<Utf8JsonWriter, MilvusCollectionDescription> writeRequest,
        Func<JsonElement, MilvusCollectionDescription, T> readResponse, CancellationToken cancellationToken)
    {
        if (guaranteeTimestamp is not null || travelTimestamp is not null)
            throw new NotSupportedException("REST reads cannot carry guarantee or time-travel timestamps.");
        if (consistency is { } level) ValidateConsistency(level);
        using CancellationTokenSource? timeout = deadline is null ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout is not null)
        {
            TimeSpan remaining = deadline!.Value - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) throw new OperationCanceledException(new CancellationToken(canceled: true));
            else timeout.CancelAfter(remaining);
            cancellationToken = timeout.Token;
        }
        cancellationToken.ThrowIfCancellationRequested();
        MilvusCollectionDescription description = await DescribeAsync(collection, cancellationToken).ConfigureAwait(false);
        ValidateConsistency(consistency ?? description.ConsistencyLevel);
        if (operation != "query" && description.Schema.Fields.Any(field => field.Name == "distance"))
            throw new NotSupportedException("REST search reserves the response field name 'distance'.");

        using var body = new MemoryStream();
        using (var writer = new Utf8JsonWriter(body))
        {
            writer.WriteStartObject();
            if (database is not null) writer.WriteString("dbName", database);
            writeRequest(writer, description);
            writer.WriteEndObject();
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "v2/vectordb/entities/" + operation)
        {
            Version = HttpVersion.Version11,
#if NET5_0_OR_GREATER
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
#endif
            Content = new ByteArrayContent(body.GetBuffer(), 0, checked((int)body.Length))
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("Accept-Type-Allow-Int64", "true");
        using HttpResponseMessage response = await httpClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
#if NET5_0_OR_GREATER
        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
        using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
        using JsonDocument json = await JsonDocument.ParseAsync(stream,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        JsonElement root = json.RootElement;
        int code = root.GetProperty("code").GetInt32();
        if (code != 0)
            throw new MilvusException((MilvusErrorCode)code, root.GetProperty("message").GetString() ?? "REST read failed.");
        cancellationToken.ThrowIfCancellationRequested();
        return readResponse(root, description);
    }

    private async Task<MilvusCollectionDescription> DescribeAsync(MilvusCollection collection, CancellationToken cancellationToken)
    {
        if (_metadata.TryGetValue(collection.Name, out MilvusCollectionDescription? description)) return description;
        await _metadataLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                if (_metadata.TryGetValue(collection.Name, out description)) return description;
                int version = MetadataVersion;
                description = await collection.DescribeAsync(cancellationToken).ConfigureAwait(false);
                if (version == MetadataVersion) return description;
            }
        }
        finally { _metadataLock.Release(); }
    }

    private static void ValidateConsistency(ConsistencyLevel level)
    {
        if (level is not (ConsistencyLevel.Strong or ConsistencyLevel.BoundedStaleness or ConsistencyLevel.Eventually))
            throw new NotSupportedException($"REST cannot preserve {level} consistency; use gRPC or select a supported level explicitly.");
    }

    private static void WriteConsistency(Utf8JsonWriter writer, ConsistencyLevel level)
        => writer.WriteString("consistencyLevel", level == ConsistencyLevel.BoundedStaleness ? "Bounded" : level.ToString());

    private static void ValidateSearchOptions(long? roundDecimal, string? groupField, int? groupSize, bool? strictGroupSize)
    {
        if (roundDecimal is not null and not -1)
            throw new NotSupportedException("REST search does not support RoundDecimal.");
        if (groupField is not null || groupSize is not null || strictGroupSize is not null)
            throw new NotSupportedException("REST grouped search cannot preserve all SearchResults metadata; use gRPC.");
    }

    private static void WriteCommon(Utf8JsonWriter writer, string collection,
        IReadOnlyList<string>? partitions, IReadOnlyList<string>? outputs)
    {
        writer.WriteString("collectionName", collection);
        if (partitions is not null) WriteStrings(writer, "partitionNames", partitions);
        if (outputs is not null) WriteStrings(writer, "outputFields", outputs);
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(name);
        foreach (string value in values) writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static void WriteSearchParams(Utf8JsonWriter writer, string name, SimilarityMetricType metric,
        IDictionary<string, string>? parameters, bool? ignoreGrowing, ulong? gracefulTime)
    {
        writer.WriteStartObject(name);
        writer.WriteString("metric_type", metric.ToString().ToUpperInvariant());
        if (ignoreGrowing is { } ignore) writer.WriteBoolean("ignoreGrowing", ignore);
        if (gracefulTime is { } grace) writer.WriteNumber("graceful_time", grace);
        writer.WriteStartObject("params");
        if (parameters is not null)
        {
            foreach (KeyValuePair<string, string> parameter in parameters)
            {
                writer.WritePropertyName(parameter.Key);
                using JsonDocument value = JsonDocument.Parse(parameter.Value);
                value.RootElement.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteDense<T>(Utf8JsonWriter writer, IReadOnlyList<ReadOnlyMemory<T>> vectors)
    {
        writer.WriteStartArray();
        foreach (ReadOnlyMemory<T> vector in vectors)
        {
            if (vector is ReadOnlyMemory<byte> bytes) writer.WriteBase64StringValue(bytes.Span);
            else
            {
                writer.WriteStartArray();
                if (vector is ReadOnlyMemory<float> floats)
                    foreach (float value in floats.Span) writer.WriteNumberValue(value);
#if NET8_0_OR_GREATER
                else if (vector is ReadOnlyMemory<Half> halves)
                    foreach (Half value in halves.Span) writer.WriteNumberValue((float)value);
#endif
                else throw new ArgumentException("Only float, byte, or Half vectors are supported.", nameof(vectors));
                writer.WriteEndArray();
            }
        }
        writer.WriteEndArray();
    }

    private static void WriteSparse<T>(Utf8JsonWriter writer, IReadOnlyList<MilvusSparseVector<T>> vectors)
    {
        if (vectors is not IReadOnlyList<MilvusSparseVector<float>> floats)
            throw new ArgumentException("Only sparse float vectors are supported.", nameof(vectors));
        writer.WriteStartArray();
        foreach (MilvusSparseVector<float> vector in floats)
        {
            writer.WriteStartObject();
            for (int i = 0; i < vector.Count; i++)
                writer.WriteNumber(vector.Indices.Span[i].ToString(CultureInfo.InvariantCulture), vector.Values.Span[i]);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static int GetQueryCount(AnnSearchRequest request) => request switch
    {
        VectorAnnSearchRequest<float> value => value.Vectors.Count,
        VectorAnnSearchRequest<byte> value => value.Vectors.Count,
#if NET8_0_OR_GREATER
        VectorAnnSearchRequest<Half> value => value.Vectors.Count,
#endif
        SparseVectorAnnSearchRequest<float> value => value.Vectors.Count,
        TextAnnSearchRequest value => value.QueryTexts.Count,
        _ => throw new ArgumentException("Unsupported hybrid search request.", nameof(request))
    };

    private static void WriteAnnData(Utf8JsonWriter writer, AnnSearchRequest request)
    {
        switch (request)
        {
            case VectorAnnSearchRequest<float> value: WriteDense(writer, value.Vectors); break;
            case VectorAnnSearchRequest<byte> value: WriteDense(writer, value.Vectors); break;
#if NET8_0_OR_GREATER
            case VectorAnnSearchRequest<Half> value: WriteDense(writer, value.Vectors); break;
#endif
            case SparseVectorAnnSearchRequest<float> value: WriteSparse(writer, value.Vectors); break;
            case TextAnnSearchRequest value:
                writer.WriteStartArray();
                foreach (string text in value.QueryTexts) writer.WriteStringValue(text);
                writer.WriteEndArray();
                break;
            default: throw new ArgumentException("Unsupported hybrid search request.", nameof(request));
        }
    }
}
