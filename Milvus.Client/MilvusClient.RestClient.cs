using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Milvus.Client;

public sealed partial class MilvusClient
{
    /// <summary>Creates a client with optional REST searches and queries.</summary>
    /// <param name="grpcChannel">The caller-owned channel used for other operations.</param>
    /// <param name="database">The database to connect to.</param>
    /// <param name="callOptions">Default gRPC options; deadlines also apply to REST calls.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="restHttpClient">
    /// Optional caller-owned HTTP client. REST over HTTP/1.1 provides an alternative
    /// for latency-sensitive reads affected by managed .NET gRPC/HTTP/2 performance
    /// issues. Null retains gRPC. Configure REST authentication on this HTTP client.
    /// </param>
    public MilvusClient(GrpcChannel grpcChannel, string? database, CallOptions callOptions,
        ILoggerFactory? loggerFactory, HttpClient? restHttpClient)
        : this(grpcChannel, database, callOptions, loggerFactory)
    {
        if (restHttpClient is not null && restHttpClient.BaseAddress is null)
            throw new ArgumentException("The REST HTTP client requires a base address.", nameof(restHttpClient));
        RestClient = restHttpClient is null ? null : new RestClient(restHttpClient, database, callOptions.Deadline);
    }

    internal RestClient? RestClient { get; }
}
