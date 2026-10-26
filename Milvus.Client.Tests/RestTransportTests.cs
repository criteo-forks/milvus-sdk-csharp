using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Net.Client;
using Xunit;

namespace Milvus.Client.Tests;

public sealed class RestTransportTests
{
    private static readonly ReadOnlyMemory<float>[] Vectors = [new float[] { 0.1f, 0.2f }];
    private static readonly MilvusSparseVector<float>[] SparseVectors = [new(new[] { 1 }, new[] { 0.5f })];
    private static readonly string[] Outputs = ["key", "value"];

    [Fact]
    public void Existing_constructors_do_not_enable_REST()
    {
        using GrpcChannel channel = GrpcChannel.ForAddress("http://localhost");
        Func<MilvusClient>[] constructors =
        [
            () => new("localhost"),
            () => new("localhost", "user", "password"),
            () => new("localhost", "token"),
            () => new(new Uri("http://localhost")),
            () => new(new Uri("http://localhost"), username: "user", password: "password"),
            () => new(new Uri("http://localhost"), apiKey: "token"),
            () => new(channel),
            () => new(channel, username: "user", password: "password"),
            () => new(channel, apiKey: "token")
        ];
        foreach (Func<MilvusClient> construct in constructors)
        {
            using MilvusClient client = construct();
            Assert.Null(client.RestClient);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Default_and_explicit_null_reads_use_generated_GRPC_calls(bool explicitNull)
    {
        using var server = new ReadServer();
        using var client = explicitNull
            ? new MilvusClient(server.Channel, "test_db", default, null, null)
            : new MilvusClient(server.Channel, database: "test_db");
        MilvusCollection collection = client.GetCollection("items");
        await ReadAll(collection);
        Assert.Equal("test-version", await client.GetVersionAsync());
        Assert.Equal(new[] { "Search", "Search", "HybridSearch", "Query", "GetVersion" }, server.GrpcMethods);
        Assert.Empty(server.RestRequests);
    }

    [Fact]
    public async Task Opt_in_reads_use_HTTP_1_1_even_when_the_supplied_client_defaults_to_HTTP_2()
    {
        using var server = new ReadServer();
        server.HttpClient.DefaultRequestVersion = HttpVersion.Version20;
        server.HttpClient.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
        using MilvusClient client = server.CreateClient(rest: true);
        await ReadAll(client.GetCollection("items"));
        Assert.Equal("test-version", await client.GetVersionAsync());
        Assert.Equal(new[] { "DescribeCollection", "GetVersion" }, server.GrpcMethods);
        Assert.Equal(new[] { "search", "search", "hybrid_search", "query" }, server.RestRequests.Select(request => request.Operation));
        Assert.All(server.RestRequests, request =>
        {
            Assert.Equal(HttpVersion.Version11, request.Version);
            Assert.Equal(HttpVersionPolicy.RequestVersionExact, request.VersionPolicy);
            using JsonDocument body = JsonDocument.Parse(request.Body);
            Assert.Equal("test_db", body.RootElement.GetProperty("dbName").GetString());
            Assert.Equal("items", body.RootElement.GetProperty("collectionName").GetString());
            Assert.Equal("Strong", body.RootElement.GetProperty("consistencyLevel").GetString());
        });
    }

    [Theory]
    [InlineData("dense")]
    [InlineData("sparse")]
    [InlineData("hybrid")]
    [InlineData("query")]
    public async Task GRPC_and_REST_return_identical_results(string operation)
    {
        using var server = new ReadServer();
        using MilvusClient grpc = server.CreateClient(rest: false);
        using MilvusClient rest = server.CreateClient(rest: true);
        if (operation == "query")
            AssertFieldsEqual(await Query(grpc.GetCollection("items")), await Query(rest.GetCollection("items")));
        else
            AssertSearchEqual(await Search(grpc.GetCollection("items"), operation), await Search(rest.GetCollection("items"), operation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Primary_key_type_and_full_Int64_precision_survive_both_transports(bool stringIds)
    {
        using var server = new ReadServer(stringIds: stringIds);
        using MilvusClient grpc = server.CreateClient(rest: false);
        using MilvusClient rest = server.CreateClient(rest: true);
        SearchResults expected = await Search(grpc.GetCollection("items"), "dense");
        SearchResults actual = await Search(rest.GetCollection("items"), "dense");
        AssertSearchEqual(expected, actual);
        if (!stringIds) Assert.Equal(9007199254740993L, actual.Ids.LongIds![0]);
        Assert.All(server.RestRequests, request => Assert.True(request.AllowInt64));
    }

    [Theory]
    [MemberData(nameof(FieldCases))]
    public async Task Typed_fields_match_GRPC_for_search_and_query(FieldSchema? schema, FieldData field, string valuesJson)
    {
        using var server = new ReadServer(schema, field, valuesJson);
        using MilvusClient grpc = server.CreateClient(rest: false);
        using MilvusClient rest = server.CreateClient(rest: true);
        AssertSearchEqual(await Search(grpc.GetCollection("items"), "dense"), await Search(rest.GetCollection("items"), "dense"));
        AssertFieldsEqual(await Query(grpc.GetCollection("items")), await Query(rest.GetCollection("items")));
    }

    public static IEnumerable<object?[]> FieldCases()
    {
        yield return [FieldSchema.Create<bool>("value"), FieldData.Create("value", new[] { true, false }), "[true,false]"];
        yield return [FieldSchema.Create<sbyte>("value"), FieldData.Create("value", new sbyte[] { 1, 2 }), "[1,2]"];
        yield return [FieldSchema.Create<short>("value"), FieldData.Create("value", new short[] { 1, 2 }), "[1,2]"];
        yield return [FieldSchema.Create<int>("value"), FieldData.Create("value", new[] { 1, 2 }), "[1,2]"];
        yield return [FieldSchema.Create<long>("value"), FieldData.Create("value", new[] { 1L, 2L }), "[1,2]"];
        yield return [FieldSchema.Create<float>("value"), FieldData.Create("value", new[] { 1.25f, 2.5f }), "[1.25,2.5]"];
        yield return [FieldSchema.Create<double>("value"), FieldData.Create("value", new[] { 1.25, 2.5 }), "[1.25,2.5]"];
        yield return [FieldSchema.Create<int?>("value"), FieldData.Create("value", new int?[] { 1, null }), "[1,null]"];
        yield return [FieldSchema.Create<bool?>("value"), FieldData.Create("value", new bool?[] { true, null }), "[true,null]"];
        yield return [FieldSchema.Create<sbyte?>("value"), FieldData.Create("value", new sbyte?[] { 1, null }), "[1,null]"];
        yield return [FieldSchema.Create<short?>("value"), FieldData.Create("value", new short?[] { 1, null }), "[1,null]"];
        yield return [FieldSchema.Create<long?>("value"), FieldData.Create("value", new long?[] { 1, null }), "[1,null]"];
        yield return [FieldSchema.Create<float?>("value"), FieldData.Create("value", new float?[] { 1.25f, null }), "[1.25,null]"];
        yield return [FieldSchema.Create<double?>("value"), FieldData.Create("value", new double?[] { 1.25, null }), "[1.25,null]"];
        yield return [FieldSchema.CreateArray<int>("value", 10, nullable: true), FieldData.CreateArray("value", new IReadOnlyList<int>?[] { new[] { 1 }, null }), "[[1],null]"];
        yield return [FieldSchema.CreateVarchar("value", 20, nullable: true), FieldData.CreateVarChar("value", new string?[] { "one", null }), """["one",null]"""];
        yield return [FieldSchema.CreateJson("value"), FieldData.CreateJson("value", ["{}", """{"a":1}"""]), """["{}","{\"a\":1}"]"""];
        yield return [FieldSchema.CreateFloatVector("value", 2), FieldData.CreateFloatVector("value", [new float[] { 1, 2 }, new float[] { 3, 4 }]), "[[1,2],[3,4]]"];
        yield return [FieldSchema.CreateBinaryVector("value", 8), FieldData.CreateBinaryVectors("value", [new byte[] { 1 }, new byte[] { 2 }]), """["AQ==","Ag=="]"""];
        yield return [FieldSchema.CreateFloat16Vector("value", 1), FieldData.CreateFloat16Vector("value", [new Half[] { (Half)1 }, new Half[] { (Half)2 }]), """["ADw=","AEA="]"""];
        yield return [FieldSchema.CreateSparseFloatVector("value"), FieldData.CreateSparseFloatVector("value", [new(new[] { 1 }, new[] { 1f }), new(new[] { 2 }, new[] { 2f })]), """[{"1":1},{"2":2}]"""];
        yield return [FieldSchema.CreateArray<bool>("value", 10), FieldData.CreateArray("value", new[] { new[] { true }, Array.Empty<bool>() }), "[[true],[]]"];
        yield return [FieldSchema.CreateArray<int>("value", 10), FieldData.CreateArray("value", new[] { new[] { 1 }, new[] { 2 } }), """[{"Data":{"IntData":{"data":[1]}}},{"Data":{"IntData":{"data":[2]}}}]"""];
        yield return [FieldSchema.CreateArray<long>("value", 10), FieldData.CreateArray("value", new[] { new[] { 1L }, Array.Empty<long>() }), "[[1],[]]"];
        yield return [FieldSchema.CreateArray<float>("value", 10), FieldData.CreateArray("value", new[] { new[] { 1f }, Array.Empty<float>() }), "[[1],[]]"];
        yield return [FieldSchema.CreateArray<double>("value", 10), FieldData.CreateArray("value", new[] { new[] { 1.0 }, Array.Empty<double>() }), "[[1],[]]"];
        yield return [FieldSchema.CreateArray<string>("value", 10), FieldData.CreateArray("value", new[] { new[] { "a" }, Array.Empty<string>() }), """[["a"],[]]"""];
        yield return [null, FieldData.CreateJson("$meta", ["""{"value":1}""", """{"value":2}"""], isDynamic: true), "[1,2]"];
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unequal_and_empty_query_groups_match_GRPC(bool empty)
    {
        using var server = new ReadServer { Limits = empty ? [0] : [0, 2], Empty = empty };
        using MilvusClient grpc = server.CreateClient(rest: false);
        using MilvusClient rest = server.CreateClient(rest: true);
        ReadOnlyMemory<float>[] vectors = empty ? [Vectors[0]] : [Vectors[0], Vectors[0]];
        var parameters = SearchOptions();
        AssertSearchEqual(await grpc.GetCollection("items").SearchAsync("vector", vectors, SimilarityMetricType.L2, 2, parameters),
            await rest.GetCollection("items").SearchAsync("vector", vectors, SimilarityMetricType.L2, 2, parameters));
    }

    [Fact]
    public async Task Auto_returned_primary_key_is_not_an_unrequested_output_field()
    {
        using var server = new ReadServer();
        using MilvusClient grpc = server.CreateClient(rest: false);
        using MilvusClient rest = server.CreateClient(rest: true);
        var options = new SearchParameters { ConsistencyLevel = ConsistencyLevel.Strong };
        SearchResults expected = await grpc.GetCollection("items").SearchAsync("vector", Vectors, SimilarityMetricType.L2, 2, options);
        SearchResults actual = await rest.GetCollection("items").SearchAsync("vector", Vectors, SimilarityMetricType.L2, 2, options);
        Assert.Empty(actual.FieldsData);
        AssertSearchEqual(expected, actual);
    }

    [Theory]
    [InlineData(ConsistencyLevel.Session)]
    [InlineData(ConsistencyLevel.Customized)]
    public async Task Unsupported_explicit_consistency_fails_before_any_network_call(ConsistencyLevel level)
    {
        using var server = new ReadServer();
        using MilvusClient client = server.CreateClient(rest: true);
        await Assert.ThrowsAsync<NotSupportedException>(() => client.GetCollection("items").SearchAsync("vector", Vectors,
            SimilarityMetricType.L2, 2, new SearchParameters { ConsistencyLevel = level }));
        await Assert.ThrowsAsync<NotSupportedException>(() => client.GetCollection("items").QueryAsync("key > 0",
            new QueryParameters { ConsistencyLevel = level }));
        Assert.Empty(server.GrpcMethods);
        Assert.Empty(server.RestRequests);
    }

    [Fact]
    public async Task Collection_default_session_is_not_silently_downgraded()
    {
        using var server = new ReadServer { Consistency = ConsistencyLevel.Session };
        using MilvusClient client = server.CreateClient(rest: true);
        await Assert.ThrowsAsync<NotSupportedException>(() => client.GetCollection("items").SearchAsync("vector", Vectors, SimilarityMetricType.L2, 2));
        Assert.Equal(new[] { "DescribeCollection" }, server.GrpcMethods);
        Assert.Empty(server.RestRequests);
    }

    [Fact]
    public async Task Metadata_is_shared_across_collection_handles_and_failed_fetches_can_retry()
    {
        using var server = new ReadServer { DescribeFailures = 1 };
        using MilvusClient client = server.CreateClient(rest: true);
        await Assert.ThrowsAsync<MilvusException>(() => Query(client.GetCollection("items")));
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Query(client.GetCollection("items"))));
        Assert.Equal(2, server.GrpcMethods.Count(method => method == "DescribeCollection"));
        await client.GetCollection("items").DescribeAsync();
        await Query(client.GetCollection("items"));
        Assert.Equal(3, server.GrpcMethods.Count(method => method == "DescribeCollection"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HTTP_and_application_errors_are_not_returned_as_empty_success(bool httpError)
    {
        using var server = new ReadServer { RestError = httpError ? "http" : "application" };
        using MilvusClient client = server.CreateClient(rest: true);
        if (httpError) await Assert.ThrowsAsync<HttpRequestException>(() => Query(client.GetCollection("items")));
        else
        {
            MilvusException error = await Assert.ThrowsAsync<MilvusException>(() => Query(client.GetCollection("items")));
            Assert.Equal(MilvusErrorCode.CollectionNotFound, error.ErrorCode);
        }
    }

    [Fact]
    public async Task Cancellation_and_expired_deadlines_prevent_reads()
    {
        using var server = new ReadServer();
        using MilvusClient client = server.CreateClient(rest: true);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetCollection("items").QueryAsync("key > 0",
            cancellationToken: cancelled.Token));
        using var expired = new MilvusClient(server.Channel, "test_db",
            new global::Grpc.Core.CallOptions(deadline: DateTime.UtcNow.AddSeconds(-1)), null, server.HttpClient);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Query(expired.GetCollection("items")));
        Assert.Empty(server.GrpcMethods);
        Assert.Empty(server.RestRequests);
    }

    [Theory]
    [InlineData("float")]
    [InlineData("binary")]
    [InlineData("half")]
    [InlineData("sparse")]
    public async Task REST_serializes_the_existing_vector_formats_and_typed_tuning_parameters(string type)
    {
        using var server = new ReadServer();
        using MilvusClient client = server.CreateClient(rest: true);
        var options = SearchOptions();
        options.ExtraParameters["ef"] = "12";
        options.ExtraParameters["flag"] = "true";
        options.IgnoreGrowing = true;
        options.PartitionNames.Add("partition");
        options.Offset = 1;
        options.Expression = "key > 0";
        MilvusCollection collection = client.GetCollection("items");
        switch (type)
        {
            case "float": await collection.SearchAsync("vector", Vectors, SimilarityMetricType.L2, 2, options); break;
            case "binary": await collection.SearchAsync("vector", new ReadOnlyMemory<byte>[] { new byte[] { 1, 2 } }, SimilarityMetricType.Hamming, 2, options); break;
            case "half": await collection.SearchAsync("vector", new ReadOnlyMemory<Half>[] { new Half[] { (Half)1 } }, SimilarityMetricType.L2, 2, options); break;
            case "sparse": await collection.SearchAsync("sparse", SparseVectors, SimilarityMetricType.Ip, 2, options); break;
        }
        using JsonDocument json = JsonDocument.Parse(Assert.Single(server.RestRequests).Body);
        JsonElement body = json.RootElement;
        Assert.Equal("key > 0", body.GetProperty("filter").GetString());
        Assert.Equal(1, body.GetProperty("offset").GetInt32());
        Assert.Equal("partition", body.GetProperty("partitionNames")[0].GetString());
        JsonElement searchParams = body.GetProperty("searchParams");
        Assert.Equal(12, searchParams.GetProperty("params").GetProperty("ef").GetInt32());
        Assert.True(searchParams.GetProperty("params").GetProperty("flag").GetBoolean());
        Assert.True(searchParams.GetProperty("ignoreGrowing").GetBoolean());
        JsonElement vector = body.GetProperty("data")[0];
        switch (type)
        {
            case "float": Assert.Equal(0.1f, vector[0].GetSingle()); break;
            case "binary": Assert.Equal(new byte[] { 1, 2 }, vector.GetBytesFromBase64()); break;
            case "half": Assert.Equal(1f, vector[0].GetSingle()); break;
            case "sparse": Assert.Equal(0.5f, vector.GetProperty("1").GetSingle()); break;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hybrid_short_results_preserve_requested_Limit_and_encode_rankers(bool weighted)
    {
        using var server = new ReadServer();
        using MilvusClient grpc = server.CreateClient(rest: false);
        using MilvusClient rest = server.CreateClient(rest: true);
        AnnSearchRequest[] requests =
        [
            new SparseVectorAnnSearchRequest<float>("sparse", SparseVectors, SimilarityMetricType.Ip, 3),
            new TextAnnSearchRequest("text_sparse", ["some words"], 3)
        ];
        IReranker ranker = weighted ? new WeightedReranker(0.25f, 0.75f) : new RrfReranker(40);
        var parameters = new HybridSearchParameters { ConsistencyLevel = ConsistencyLevel.Strong, OutputFields = { "key", "value" } };
        SearchResults expected = await grpc.GetCollection("items").HybridSearchAsync(requests, ranker, 5, parameters);
        SearchResults actual = await rest.GetCollection("items").HybridSearchAsync(requests, ranker, 5, parameters);
        AssertSearchEqual(expected, actual);
        Assert.Equal(5, actual.Limit);
        Assert.Equal(new long[] { 2 }, actual.Limits);
        using JsonDocument json = JsonDocument.Parse(Assert.Single(server.RestRequests).Body);
        JsonElement body = json.RootElement;
        Assert.Equal("some words", body.GetProperty("search")[1].GetProperty("data")[0].GetString());
        Assert.Equal("BM25", body.GetProperty("search")[1].GetProperty("params").GetProperty("metric_type").GetString());
        JsonElement rerank = body.GetProperty("rerank");
        Assert.Equal(weighted ? "weighted" : "rrf", rerank.GetProperty("strategy").GetString());
        if (weighted) Assert.Equal(0.25f, rerank.GetProperty("params").GetProperty("weights")[0].GetSingle());
        else Assert.Equal(40f, rerank.GetProperty("params").GetProperty("k").GetSingle());
    }

    [Fact]
    public async Task Query_count_matches_GRPC()
    {
        using var server = new ReadServer();
        using MilvusClient grpc = server.CreateClient(rest: false);
        using MilvusClient rest = server.CreateClient(rest: true);
        var options = new QueryParameters { ConsistencyLevel = ConsistencyLevel.Strong, OutputFields = { "count(*)" } };
        AssertFieldsEqual(await grpc.GetCollection("items").QueryAsync("key > 0", options),
            await rest.GetCollection("items").QueryAsync("key > 0", options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Unsupported_search_options_are_not_silently_ignored(int option)
    {
        using var server = new ReadServer();
        using MilvusClient client = server.CreateClient(rest: true);
        var options = SearchOptions();
        switch (option)
        {
            case 0: options.GuaranteeTimestamp = 12; break;
            case 1: options.TimeTravelTimestamp = 12; break;
            case 2: options.RoundDecimal = 2; break;
            case 3: options.GroupByField = "value"; break;
        }
        await Assert.ThrowsAsync<NotSupportedException>(() => client.GetCollection("items")
            .SearchAsync("vector", Vectors, SimilarityMetricType.L2, 2, options));
        Assert.Empty(server.RestRequests);
        Assert.Empty(server.GrpcMethods);
    }

    [Fact]
    public async Task Local_collection_and_alias_changes_invalidate_metadata()
    {
        using var server = new ReadServer();
        using MilvusClient client = server.CreateClient(rest: true);
        await Query(client.GetCollection("items"));
        Func<Task>[] changes =
        [
            () => client.CreateAliasAsync("items", "alias"),
            () => client.AlterAliasAsync("items", "alias"),
            () => client.DropAliasAsync("alias"),
            () => client.GetCollection("items").RenameAsync("renamed"),
            () => client.GetCollection("items").DropAsync(),
            () => client.CreateCollectionAsync("items", [FieldSchema.Create<long>("key", isPrimaryKey: true)])
        ];
        foreach (Func<Task> change in changes)
        {
            int descriptions = server.GrpcMethods.Count(method => method == "DescribeCollection");
            await change();
            await Query(client.GetCollection("items"));
            Assert.Equal(descriptions + 1, server.GrpcMethods.Count(method => method == "DescribeCollection"));
        }
    }

    [Fact]
    public async Task Disposing_driver_does_not_dispose_caller_owned_transports()
    {
        using var server = new ReadServer();
        using (MilvusClient client = server.CreateClient(rest: true)) await Query(client.GetCollection("items"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "v2/vectordb/entities/query")
        {
            Content = new StringContent("""{"collectionName":"items","outputFields":["key","value"]}""")
        };
        request.Headers.Add("Accept-Type-Allow-Int64", "true");
        using HttpResponseMessage response = await server.HttpClient.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode);
        using MilvusClient grpc = server.CreateClient(rest: false);
        Assert.Equal("test-version", await grpc.GetVersionAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_or_deadline_interrupts_an_inflight_REST_read(bool deadline)
    {
        using var server = new ReadServer { BlockRest = true };
        using var cancel = new CancellationTokenSource();
        var options = deadline ? new global::Grpc.Core.CallOptions(deadline: DateTime.UtcNow.AddSeconds(1)) : default;
        using var client = new MilvusClient(server.Channel, "test_db", options, null, server.HttpClient);
        Task<IReadOnlyList<FieldData>> read = client.GetCollection("items").QueryAsync("key > 0",
            new QueryParameters { ConsistencyLevel = ConsistencyLevel.Strong }, cancel.Token);
        await server.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (!deadline) cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    [Fact]
    public async Task Actual_network_requests_for_search_and_query_are_HTTP_1_1()
    {
        using var server = new ReadServer();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var requestLines = new List<string>();
        Task respond = RespondAsync();
        using var handler = new SocketsHttpHandler { UseProxy = false };
        using var http = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
        };
        using var client = new MilvusClient(server.Channel, "test_db", default, null, http);
        await Search(client.GetCollection("items"), "dense");
        await Query(client.GetCollection("items"));
        await respond;
        Assert.Equal(new[]
        {
            "POST /v2/vectordb/entities/search HTTP/1.1",
            "POST /v2/vectordb/entities/query HTTP/1.1"
        }, requestLines);

        async Task RespondAsync()
        {
            for (int i = 0; i < 2; i++)
            {
                using TcpClient connection = await listener.AcceptTcpClientAsync(timeout.Token);
                using NetworkStream stream = connection.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                requestLines.Add((await reader.ReadLineAsync(timeout.Token))!);
                int contentLength = 0;
                while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 } header)
                {
                    if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        contentLength = int.Parse(header.Substring("Content-Length:".Length), System.Globalization.CultureInfo.InvariantCulture);
                }
                await reader.ReadBlockAsync(new char[contentLength], timeout.Token);
                string body = i == 0
                    ? """{"code":0,"data":[{"key":9007199254740993,"value":"one","distance":0.25},{"key":9007199254740995,"value":"two","distance":0.5}],"topks":[2]}"""
                    : """{"code":0,"data":[{"key":9007199254740993,"value":"one"},{"key":9007199254740995,"value":"two"}]}""";
                byte[] response = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
                await stream.WriteAsync(response, timeout.Token);
            }
        }
    }

    [Fact]
    public async Task Ambiguous_empty_multi_query_response_is_not_reported_as_success()
    {
        using var server = new ReadServer { Empty = true };
        using MilvusClient rest = server.CreateClient(rest: true);
        await Assert.ThrowsAsync<MilvusException>(() => rest.GetCollection("items").SearchAsync("vector",
            new[] { Vectors[0], Vectors[0] }, SimilarityMetricType.L2, 2, SearchOptions()));
    }

    [Fact]
    public async Task Inconsistent_query_counts_are_rejected()
    {
        using var server = new ReadServer { Limits = [1] };
        using MilvusClient rest = server.CreateClient(rest: true);
        await Assert.ThrowsAsync<MilvusException>(() => Search(rest.GetCollection("items"), "dense"));
    }

    [Fact]
    public async Task Inflight_description_cannot_restore_metadata_invalidated_by_an_alias_change()
    {
        using var server = new ReadServer();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.BeforeDescribeReply = async () =>
        {
            started.SetResult();
            await release.Task;
        };
        using MilvusClient client = server.CreateClient(rest: true);
        Task<IReadOnlyList<FieldData>> read = client.GetCollection("items").QueryAsync("key > 0");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        server.Consistency = ConsistencyLevel.Session;
        await client.AlterAliasAsync("other", "items");
        release.SetResult();
        await Assert.ThrowsAsync<NotSupportedException>(() => read);
        Assert.Equal(2, server.GrpcMethods.Count(method => method == "DescribeCollection"));
        Assert.Empty(server.RestRequests);
    }

    [Fact]
    public async Task Omitted_consistency_maps_the_supported_collection_default()
    {
        using var server = new ReadServer();
        using MilvusClient client = server.CreateClient(rest: true);
        await client.GetCollection("items").QueryAsync("key > 0", new QueryParameters { Limit = 2, Offset = 1 });
        using JsonDocument body = JsonDocument.Parse(Assert.Single(server.RestRequests).Body);
        Assert.Equal("Bounded", body.RootElement.GetProperty("consistencyLevel").GetString());
        Assert.Equal(2, body.RootElement.GetProperty("limit").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("offset").GetInt32());
    }

    [Fact]
    public void A_non_null_REST_client_requires_a_base_address()
    {
        using var server = new ReadServer();
        using var http = new HttpClient();
        Assert.Throws<ArgumentException>(() => new MilvusClient(server.Channel, "test_db", default, null, http));
    }

    [Fact]
    public async Task Query_without_output_fields_matches_the_GRPC_primary_key_default()
    {
        using var server = new ReadServer();
        using MilvusClient grpc = server.CreateClient(rest: false);
        using MilvusClient rest = server.CreateClient(rest: true);
        IReadOnlyList<FieldData> expected = await grpc.GetCollection("items").QueryAsync("key > 0");
        IReadOnlyList<FieldData> actual = await rest.GetCollection("items").QueryAsync("key > 0");
        Assert.Equal("key", Assert.Single(actual).FieldName);
        AssertFieldsEqual(expected, actual);
    }

    private static async Task ReadAll(MilvusCollection collection)
    {
        await Search(collection, "dense");
        await Search(collection, "sparse");
        await Search(collection, "hybrid");
        await Query(collection);
    }

    private static SearchParameters SearchOptions()
    {
        var options = new SearchParameters { ConsistencyLevel = ConsistencyLevel.Strong };
        foreach (string output in Outputs) options.OutputFields.Add(output);
        return options;
    }

    private static Task<SearchResults> Search(MilvusCollection collection, string operation) => operation switch
    {
        "dense" => collection.SearchAsync("vector", Vectors, SimilarityMetricType.L2, 2, SearchOptions()),
        "sparse" => collection.SearchAsync("sparse", SparseVectors, SimilarityMetricType.Ip, 2, SearchOptions()),
        "hybrid" => collection.HybridSearchAsync([new VectorAnnSearchRequest<float>("vector", Vectors, SimilarityMetricType.L2, 2)],
            new RrfReranker(), 2, new HybridSearchParameters { ConsistencyLevel = ConsistencyLevel.Strong, OutputFields = { "key", "value" } }),
        _ => throw new ArgumentException("Unknown test operation.", nameof(operation))
    };

    private static Task<IReadOnlyList<FieldData>> Query(MilvusCollection collection)
        => collection.QueryAsync("key > 0", new QueryParameters { ConsistencyLevel = ConsistencyLevel.Strong, OutputFields = { "key", "value" } });

    private static void AssertSearchEqual(SearchResults expected, SearchResults actual)
    {
        Assert.Equal(expected.CollectionName, actual.CollectionName);
        Assert.Equal(expected.NumQueries, actual.NumQueries);
        Assert.Equal(expected.Limit, actual.Limit);
        Assert.Equal(expected.Limits, actual.Limits);
        Assert.Equal(expected.Ids.LongIds, actual.Ids.LongIds);
        Assert.Equal(expected.Ids.StringIds, actual.Ids.StringIds);
        Assert.Equal(expected.Scores, actual.Scores);
        AssertFieldsEqual(expected.FieldsData, actual.FieldsData);
    }

    private static void AssertFieldsEqual(IReadOnlyList<FieldData> expected, IReadOnlyList<FieldData> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (FieldData field in expected)
        {
            FieldData other = Assert.Single(actual, value => value.FieldName == field.FieldName);
            Assert.Equal(field.GetType(), other.GetType());
            Assert.Equal(field.RowCount, other.RowCount);
            Assert.Equal(field.IsDynamic, other.IsDynamic);
            Assert.Equal(field.ToGrpcFieldData().ToByteArray(), other.ToGrpcFieldData().ToByteArray());
        }
    }

    private sealed record RestRequest(string Operation, Version Version, HttpVersionPolicy VersionPolicy, string Body, bool AllowInt64);

    private sealed class ReadServer : IDisposable
    {
        private readonly FieldSchema? _valueSchema;
        private readonly FieldData _valueField;
        private readonly JsonElement[] _values;
        private readonly bool _stringIds;
        private readonly Handler _restHandler;
        internal readonly ConcurrentQueue<string> GrpcMethods = new();
        internal readonly ConcurrentQueue<RestRequest> RestRequests = new();
        internal readonly GrpcChannel Channel;
        internal readonly HttpClient HttpClient;
        internal ConsistencyLevel Consistency = ConsistencyLevel.BoundedStaleness;
        internal long[] Limits = [2];
        internal bool Empty;
        internal int DescribeFailures;
        internal string? RestError;
        internal bool BlockRest;
        internal Func<Task>? BeforeDescribeReply;
        internal readonly TaskCompletionSource RequestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ReadServer(FieldSchema? schema = null, FieldData? field = null,
            string valuesJson = "[\"one\",\"two\"]", bool stringIds = false)
        {
            _valueSchema = field is null ? FieldSchema.CreateVarchar("value", 20) : schema;
            _valueField = field ?? FieldData.CreateVarChar("value", ["one", "two"]);
            using JsonDocument values = JsonDocument.Parse(valuesJson);
            _values = values.RootElement.EnumerateArray().Select(value => value.Clone()).ToArray();
            _stringIds = stringIds;
            Channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = new Handler(GrpcAsync) });
            _restHandler = new Handler(RestAsync);
            HttpClient = new HttpClient(_restHandler, disposeHandler: false) { BaseAddress = new Uri("https://milvus.invalid/") };
        }

        internal MilvusClient CreateClient(bool rest) => rest
            ? new(Channel, "test_db", default, null, HttpClient) : new(Channel, database: "test_db");

        public void Dispose()
        {
            Channel.Dispose();
            HttpClient.Dispose();
            _restHandler.Dispose();
        }

        private async Task<HttpResponseMessage> GrpcAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string method = request.RequestUri!.Segments.Last();
            GrpcMethods.Enqueue(method);
            byte[] body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            byte[] payload = body.AsSpan(5).ToArray();
            IMessage response = method switch
            {
                "DescribeCollection" => Describe(),
                "Search" => SearchResponse(Grpc.SearchRequest.Parser.ParseFrom(payload).OutputFields),
                "HybridSearch" => HybridResponse(Grpc.HybridSearchRequest.Parser.ParseFrom(payload)),
                "Query" => QueryResponse(Grpc.QueryRequest.Parser.ParseFrom(payload).OutputFields),
                "GetVersion" => new Grpc.GetVersionResponse { Status = new(), Version = "test-version" },
                _ => new Grpc.Status()
            };
            if (method == "DescribeCollection" && BeforeDescribeReply is { } beforeReply)
            {
                BeforeDescribeReply = null;
                await beforeReply();
            }
            byte[] message = response.ToByteArray();
            byte[] frame = new byte[message.Length + 5];
            BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1), message.Length);
            message.CopyTo(frame, 5);
            var result = new HttpResponseMessage(HttpStatusCode.OK) { Version = HttpVersion.Version20, Content = new ByteArrayContent(frame) };
            result.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            result.TrailingHeaders.Add("grpc-status", "0");
            return result;
        }

        private Grpc.DescribeCollectionResponse Describe()
        {
            if (DescribeFailures-- > 0) return new() { Status = new() { Code = 100, Reason = "temporary missing collection" } };
            var response = new Grpc.DescribeCollectionResponse
            {
                Status = new(), CollectionName = "items", ConsistencyLevel = (Grpc.ConsistencyLevel)Consistency,
                Schema = new() { Name = "items" }
            };
            response.Schema.Fields.Add(new Grpc.FieldSchema { Name = "key", IsPrimaryKey = true,
                DataType = _stringIds ? Grpc.DataType.VarChar : Grpc.DataType.Int64 });
            if (_valueSchema is { } schema)
            {
                var field = new Grpc.FieldSchema { Name = "value", DataType = (Grpc.DataType)schema.DataType,
                    ElementType = (Grpc.DataType)schema.ElementDataType, Nullable = schema.Nullable };
                if (schema.Dimension is { } dimension) field.TypeParams.Add(new Grpc.KeyValuePair { Key = "dim", Value = dimension.ToString(System.Globalization.CultureInfo.InvariantCulture) });
                response.Schema.Fields.Add(field);
            }
            return response;
        }

        private IEnumerable<Grpc.FieldData> Fields(IReadOnlyCollection<string> outputs)
        {
            if (Empty) yield break;
            if (outputs.Contains("key") || outputs.Contains("*"))
                yield return _stringIds ? FieldData.CreateVarChar("key", ["first", "second"]).ToGrpcFieldData()
                    : FieldData.Create("key", new[] { 9007199254740993L, 9007199254740995L }).ToGrpcFieldData();
            if (outputs.Contains("value") || outputs.Contains("*")) yield return _valueField.ToGrpcFieldData();
        }

        private Grpc.SearchResults SearchResponse(IReadOnlyCollection<string> outputs)
        {
            var response = new Grpc.SearchResults { Status = new(), CollectionName = "items",
                Results = new() { NumQueries = Limits.Length, TopK = Empty ? 0 : Limits.Last(), Ids = new() } };
            response.Results.Topks.AddRange(Limits);
            if (!Empty)
            {
                if (_stringIds) response.Results.Ids.StrId = new() { Data = { "first", "second" } };
                else response.Results.Ids.IntId = new() { Data = { 9007199254740993L, 9007199254740995L } };
                response.Results.Scores.AddRange(new[] { 0.25f, 0.5f });
                response.Results.FieldsData.AddRange(Fields(outputs));
            }
            return response;
        }

        private Grpc.SearchResults HybridResponse(Grpc.HybridSearchRequest request)
        {
            Grpc.SearchResults response = SearchResponse(request.OutputFields);
            response.Results.TopK = long.Parse(request.RankParams.Single(pair => pair.Key == "limit").Value,
                System.Globalization.CultureInfo.InvariantCulture);
            return response;
        }

        private Grpc.QueryResults QueryResponse(IReadOnlyCollection<string> outputs)
        {
            var response = new Grpc.QueryResults { Status = new() };
            if (outputs.Contains("count(*)"))
                response.FieldsData.Add(FieldData.Create("count(*)", new[] { 2L }).ToGrpcFieldData());
            else response.FieldsData.AddRange(Fields(outputs.Count == 0 ? new[] { "key" } : outputs));
            return response;
        }

        private async Task<HttpResponseMessage> RestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string operation = request.RequestUri!.Segments.Last();
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            RestRequests.Enqueue(new(operation, request.Version, request.VersionPolicy, body,
                request.Headers.GetValues("Accept-Type-Allow-Int64").Single() == "true"));
            RequestStarted.TrySetResult();
            if (BlockRest) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (RestError == "http") return new(HttpStatusCode.ServiceUnavailable);
            if (RestError == "application") return JsonResponse(new { code = 100, message = "collection missing" });
            using JsonDocument json = JsonDocument.Parse(body);
            string[] outputs = json.RootElement.TryGetProperty("outputFields", out JsonElement fields)
                ? fields.EnumerateArray().Select(field => field.GetString()!).ToArray() : [];
            if (outputs.Contains("count(*)")) return JsonResponse(new { code = 0, data = new[] { new Dictionary<string, long> { ["count(*)"] = 2 } } });
            var rows = new List<Dictionary<string, object?>>();
            if (!Empty)
            {
                for (int i = 0; i < _values.Length; i++)
                {
                    var row = new Dictionary<string, object?>();
                    if (operation != "query" || outputs.Length == 0 || outputs.Contains("key") || outputs.Contains("*"))
                        row["key"] = _stringIds ? (i == 0 ? "first" : "second") : 9007199254740993L + i * 2;
                    if (outputs.Contains("value") || outputs.Contains("*")) row["value"] = _values[i];
                    if (operation != "query") row["distance"] = i == 0 ? 0.25f : 0.5f;
                    rows.Add(row);
                }
            }
            return Empty ? JsonResponse(new { code = 0, data = rows })
                : JsonResponse(new { code = 0, data = rows, topks = Limits });
        }

        private static HttpResponseMessage JsonResponse(object body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
