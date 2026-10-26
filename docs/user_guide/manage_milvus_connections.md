# Manage Milvus Connections

## Connect to a Milvus server

```c#
var milvusClient = new MilvusClient("localhost", username: "username", password: "password");
```

## Disconnect from a Milvus server

```c#
milvusClient.Dispose();
```

## Optional REST searches and queries

By default, every operation uses gRPC. An optional HTTP client enables REST
over HTTP/1.1 for both SearchAsync overloads, HybridSearchAsync, and QueryAsync.
This provides an alternative for latency-sensitive reads affected by reported
performance issues in the managed .NET gRPC/HTTP/2 stack. REST is not necessarily
faster for every workload; large JSON responses can be expensive.

~~~csharp
using var channel = GrpcChannel.ForAddress("https://milvus.example:19530");
using var restHttpClient = new HttpClient
{
    BaseAddress = new Uri("https://milvus-rest.example/")
};
using var client = new MilvusClient(
    channel,
    database: "default",
    callOptions: default,
    loggerFactory: null,
    restHttpClient: restHttpClient);
~~~

Configure the channel and HTTP client with the authentication required by your
server. REST authentication is separate from gRPC credentials. The HTTP client's
handler pipeline can provide token refresh, endpoint discovery, and client-side
load balancing; the driver has no infrastructure-specific dependencies.

Existing constructors, or a null restHttpClient, retain gRPC. REST requests
explicitly use HTTP/1.1; on modern .NET they require that exact version. The
caller owns both supplied transports, so disposing the Milvus client does not
dispose the channel or HTTP client.

Other operations, including QueryIteratorAsync and collection metadata lookup,
continue using gRPC. The first REST read fetches collection metadata through
DescribeAsync and caches it per client and collection name. Calling DescribeAsync
again refreshes the metadata after external schema or alias changes; successful
local create/drop/rename and alias changes invalidate the cache.

REST has protocol limitations. The implementation was checked against Milvus
2.6.13 source; verify behavior against your deployed server patch:

- Use Strong, BoundedStaleness, or Eventually consistency. Omitted consistency
  uses the collection default. Session and Customized are rejected because REST
  cannot carry the driver's last-mutation guarantee timestamp. Collection
  creation currently defaults to Session, so such collections need an explicit
  supported read consistency.
- Explicit guarantee/time-travel timestamps, requested score rounding other than
  -1, grouped search, and global hybrid IgnoreGrowing are rejected. Per-leg
  IgnoreGrowing is supported for hybrid search.
- Dynamic numeric output at or outside ±2^53 is rejected: the checked server
  converts dynamic JSON numbers through float64 and can round large integers
  before transmission. Use gRPC for these values. Typed Int64 fields and primary
  keys retain their full precision.
- REST search reserves distance as a response field; collections containing
  that field name are rejected for REST searches.
- Per-query counts must be present for multi-query results, including empty
  replies. The checked server can omit earlier hits when the final query is
  empty; without counts, the driver cannot distinguish that from an all-empty
  batch and rejects the ambiguous reply. A single-query empty result is safe. SearchResults.Limit follows the checked ordinary
  search reducer's final query count; hybrid search retains its requested limit.

Unsupported settings fail explicitly; the driver does not silently downgrade
consistency or switch a configured REST read back to gRPC. Cancellation covers
metadata lookup and response reading, and CallOptions.Deadline also bounds REST
reads. gRPC headers and credentials are not copied to HTTP requests.
