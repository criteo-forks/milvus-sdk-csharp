using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using Google.Protobuf;
using Grpc.Net.Client;
using Xunit;

namespace Milvus.Client.Tests;

public sealed class FieldSchemaMatchTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Match_and_analyzer_settings_survive_all_creation_overloads(bool useSchema, bool useProperties)
    {
        using var server = new Server();
        using var client = new MilvusClient(server.Channel);
        const string analyzerJson = "{\"tokenizer\":\"standard\"}";
        var match = FieldSchema.CreateVarchar("brand", 200, nullable: true, enableAnalyzer: true,
            analyzerParams: analyzerJson);
        match.EnableMatch = true;
        FieldSchema[] fields =
        [
            FieldSchema.Create<long>("id", isPrimaryKey: true),
            FieldSchema.CreateFloatVector("vector", 2),
            match,
            FieldSchema.CreateVarchar("bm25_text", 256, enableAnalyzer: true),
            FieldSchema.CreateVarchar("plain_text", 256)
        ];
        var schema = new CollectionSchema(fields);
        var properties = new Dictionary<string, string> { ["custom.property"] = "unchanged" };

        if (useSchema)
        {
            if (useProperties) await client.CreateCollectionAsync("items", properties, schema);
            else await client.CreateCollectionAsync("items", schema);
        }
        else
        {
            if (useProperties) await client.CreateCollectionAsync("items", properties, fields);
            else await client.CreateCollectionAsync("items", fields);
        }

        var request = Assert.IsType<Grpc.CreateCollectionRequest>(Assert.Single(server.Requests));
        var sentSchema = Grpc.CollectionSchema.Parser.ParseFrom(request.Schema);
        var sentMatch = sentSchema.Fields.Single(f => f.Name == "brand");
        Assert.True(sentMatch.Nullable);
        Assert.Equal(new Dictionary<string, string>
        {
            ["max_length"] = "200",
            ["enable_analyzer"] = "true",
            ["analyzer_params"] = analyzerJson,
            ["enable_match"] = "true"
        }, sentMatch.TypeParams.ToDictionary(p => p.Key, p => p.Value));
        Assert.All(sentSchema.Fields.Where(f => f.Name != "brand"),
            f => Assert.DoesNotContain(f.TypeParams, p => p.Key == "enable_match"));
        Assert.Contains(sentSchema.Fields.Single(f => f.Name == "bm25_text").TypeParams,
            p => p.Key == "enable_analyzer" && p.Value == "true");
        Assert.Equal(useProperties ? properties : new Dictionary<string, string>(),
            request.Properties.ToDictionary(p => p.Key, p => p.Value));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    public async Task Describe_reads_match_without_losing_analyzer_configuration(string? matchValue, bool expected)
    {
        using var server = new Server();
        using var client = new MilvusClient(server.Channel);
        var field = new Grpc.FieldSchema { Name = "brand", DataType = Grpc.DataType.VarChar };
        field.TypeParams.Add(new Grpc.KeyValuePair { Key = "max_length", Value = "200" });
        field.TypeParams.Add(new Grpc.KeyValuePair { Key = "enable_analyzer", Value = "true" });
        field.TypeParams.Add(new Grpc.KeyValuePair { Key = "analyzer_params", Value = "{\"tokenizer\":\"standard\"}" });
        if (matchValue is not null)
        {
            field.TypeParams.Add(new Grpc.KeyValuePair { Key = "enable_match", Value = matchValue });
        }
        server.Schema.Fields.Add(field);

        var description = await client.GetCollection("items").DescribeAsync();

        var described = Assert.Single(description.Schema.Fields);
        Assert.Equal(expected, described.EnableMatch);
        Assert.True(described.EnableAnalyzer);
        Assert.Equal("{\"tokenizer\":\"standard\"}", described.AnalyzerParams);
        Assert.Equal(200, described.MaxLength);
    }

    private sealed class Server : HttpMessageHandler
    {
        internal List<IMessage> Requests { get; } = [];
        internal Grpc.CollectionSchema Schema { get; } = new() { Name = "items" };
        internal GrpcChannel Channel { get; }

        internal Server() => Channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = this });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            byte[] body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            IMessage result;
            if (request.RequestUri!.AbsolutePath.EndsWith("/CreateCollection", StringComparison.Ordinal))
            {
                Requests.Add(Grpc.CreateCollectionRequest.Parser.ParseFrom(body.AsSpan(5).ToArray()));
                result = new Grpc.Status();
            }
            else
            {
                Assert.EndsWith("/DescribeCollection", request.RequestUri.AbsolutePath, StringComparison.Ordinal);
                Requests.Add(Grpc.DescribeCollectionRequest.Parser.ParseFrom(body.AsSpan(5).ToArray()));
                result = new Grpc.DescribeCollectionResponse
                {
                    Status = new Grpc.Status(), CollectionName = "items", Schema = Schema
                };
            }
            byte[] payload = result.ToByteArray();
            byte[] frame = new byte[payload.Length + 5];
            BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1, 4), payload.Length);
            payload.CopyTo(frame, 5);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = HttpVersion.Version20, Content = new ByteArrayContent(frame)
            };
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
