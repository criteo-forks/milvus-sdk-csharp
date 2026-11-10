using System.Net;
using System.Net.Http.Headers;
using Google.Protobuf;
using Grpc.Net.Client;
using Xunit;

namespace Milvus.Client.Tests;

public sealed class CollectionPropertiesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Properties_are_part_of_creation_in_both_overloads(bool useSchema)
    {
        using var server = new Server();
        using var client = new MilvusClient(server.Channel, database: "tenant_db");
        var properties = new Dictionary<string, string>
        {
            ["partitionkey.isolation"] = "true",
            ["custom.property"] = "Case-Sensitive Value"
        };
        FieldSchema[] fields = Fields();
        var collection = useSchema
            ? await client.CreateCollectionAsync("items", properties, new CollectionSchema(fields), ConsistencyLevel.Strong, 2)
            : await client.CreateCollectionAsync("items", properties, fields, ConsistencyLevel.Strong, 2);

        Assert.Equal("items", collection.Name);
        var request = Assert.Single(server.Requests);
        Assert.Equal(properties, request.Properties.ToDictionary(p => p.Key, p => p.Value));
        Assert.Equal("items", request.CollectionName);
        Assert.Equal(2, request.ShardsNum);
        Assert.Equal(Grpc.ConsistencyLevel.Strong, request.ConsistencyLevel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_positional_default_remains_unambiguous(bool useSchema)
    {
        using var server = new Server();
        using var client = new MilvusClient(server.Channel, database: "tenant_db");
        FieldSchema[] fields = Fields();
        var collection = useSchema
            ? await client.CreateCollectionAsync("items", new CollectionSchema(fields), default)
            : await client.CreateCollectionAsync("items", fields, default);
        Assert.Equal("items", collection.Name);
        var request = Assert.Single(server.Requests);
        Assert.Empty(request.Properties);
        Assert.Equal((Grpc.ConsistencyLevel)default(ConsistencyLevel), request.ConsistencyLevel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_null_and_empty_properties_preserve_defaults(bool useSchema)
    {
        using var server = new Server();
        using var client = new MilvusClient(server.Channel, database: "tenant_db");
        FieldSchema[] fields = Fields();
        var schema = new CollectionSchema(fields);
        if (useSchema)
        {
            await client.CreateCollectionAsync("items", schema);
            await client.CreateCollectionAsync("items", null, schema);
            await client.CreateCollectionAsync("items", new Dictionary<string, string>(), schema);
        }
        else
        {
            await client.CreateCollectionAsync("items", fields);
            await client.CreateCollectionAsync("items", null, fields);
            await client.CreateCollectionAsync("items", new Dictionary<string, string>(), fields);
        }
        Assert.Equal(3, server.Requests.Count);
        Assert.All(server.Requests, request =>
        {
            Assert.Empty(request.Properties);
            Assert.Equal(1, request.ShardsNum);
            Assert.Equal(Grpc.ConsistencyLevel.Session, request.ConsistencyLevel);
        });
        Assert.Equal(server.Requests[0].ToByteArray(), server.Requests[1].ToByteArray());
        Assert.Equal(server.Requests[0].ToByteArray(), server.Requests[2].ToByteArray());
    }

    private static FieldSchema[] Fields() =>
    [
        FieldSchema.Create<long>("id", isPrimaryKey: true),
        FieldSchema.CreateVarchar("tenant", maxLength: 128, isPartitionKey: true),
        FieldSchema.CreateFloatVector("vector", dimension: 2)
    ];

    private sealed class Server : HttpMessageHandler
    {
        internal readonly List<Grpc.CreateCollectionRequest> Requests = [];
        internal GrpcChannel Channel { get; }

        internal Server() => Channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = this });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.EndsWith("/CreateCollection", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal("tenant_db", Assert.Single(request.Headers.GetValues("dbname")));
            byte[] body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(Grpc.CreateCollectionRequest.Parser.ParseFrom(body.AsSpan(5).ToArray()));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Version = HttpVersion.Version20, Content = new ByteArrayContent(new byte[5]) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.TrailingHeaders.Add("grpc-status", "0");
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Channel.Dispose();
            base.Dispose(disposing);
        }
    }
}
