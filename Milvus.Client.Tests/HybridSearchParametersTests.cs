using Xunit;

namespace Milvus.Client.Tests;

public class HybridSearchParametersTests
{
    [Fact]
    public void IgnoreGrowing_is_serialized_as_global_hybrid_search_param()
    {
        Grpc.HybridSearchRequest request = CreateHybridSearchRequest(
            [CreateAnnSearchRequest()],
            new HybridSearchParameters { IgnoreGrowing = true });

        var ignoreGrowing = Assert.Single(
            request.RankParams,
            parameter => parameter.Key == Constants.IgnoreGrowing);
        Assert.Equal(bool.TrueString, ignoreGrowing.Value);
    }

    [Fact]
    public void AnnSearchRequest_IgnoreGrowing_is_serialized_as_per_leg_search_param()
    {
        Grpc.HybridSearchRequest request = CreateHybridSearchRequest(
            [CreateAnnSearchRequest(ignoreGrowing: true)],
            parameters: null);

        Grpc.SearchRequest subRequest = Assert.Single(request.Requests);
        var ignoreGrowing = Assert.Single(
            subRequest.SearchParams,
            parameter => parameter.Key == Constants.IgnoreGrowing);
        Assert.Equal(bool.TrueString, ignoreGrowing.Value);
    }

    [Fact]
    public void AnnSearchRequest_ExtraParameters_ignore_growing_is_serialized_only_inside_nested_params()
    {
        Grpc.HybridSearchRequest request = CreateHybridSearchRequest(
            [CreateAnnSearchRequest(extraIgnoreGrowing: true)],
            parameters: null);

        Grpc.SearchRequest subRequest = Assert.Single(request.Requests);
        Assert.DoesNotContain(
            subRequest.SearchParams,
            parameter => parameter.Key == Constants.IgnoreGrowing);

        var nestedParams = Assert.Single(
            subRequest.SearchParams,
            parameter => parameter.Key == Constants.Params);
        Assert.Equal("""{"ignore_growing":true}""", nestedParams.Value);
    }

    private static Grpc.HybridSearchRequest CreateHybridSearchRequest(
        IReadOnlyList<AnnSearchRequest> requests,
        HybridSearchParameters? parameters)
    {
        using var client = new MilvusClient("localhost", port: 19530, ssl: false);
        MilvusCollection collection = client.GetCollection("test_collection");

        return collection.CreateHybridSearchRequest(
            requests,
            new RrfReranker(),
            limit: 10,
            parameters);
    }

    private static AnnSearchRequest CreateAnnSearchRequest(
        bool? ignoreGrowing = null,
        bool extraIgnoreGrowing = false)
    {
        var request = new VectorAnnSearchRequest<float>(
            "vector",
            [new[] { 0.1f, 0.2f }],
            SimilarityMetricType.L2,
            limit: 10)
        {
            IgnoreGrowing = ignoreGrowing
        };

        if (extraIgnoreGrowing)
        {
            request.ExtraParameters[Constants.IgnoreGrowing] = "true";
        }

        return request;
    }
}
