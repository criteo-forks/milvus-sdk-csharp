using Xunit;

namespace Milvus.Client.Tests;

public class SearchParametersTests
{
    [Fact]
    public void IgnoreGrowing_is_serialized_as_top_level_search_param()
    {
        Grpc.SearchRequest request = CreateSearchRequest(new SearchParameters
        {
            IgnoreGrowing = true,
            ExtraParameters =
            {
                ["nprobe"] = "10"
            }
        });

        var ignoreGrowing = Assert.Single(
            request.SearchParams,
            parameter => parameter.Key == Constants.IgnoreGrowing);
        Assert.Equal(bool.TrueString, ignoreGrowing.Value);

        var nestedParams = Assert.Single(
            request.SearchParams,
            parameter => parameter.Key == Constants.Params);
        Assert.DoesNotContain(Constants.IgnoreGrowing, nestedParams.Value);
    }

    [Fact]
    public void ExtraParameters_ignore_growing_is_serialized_only_inside_nested_params()
    {
        Grpc.SearchRequest request = CreateSearchRequest(new SearchParameters
        {
            ExtraParameters =
            {
                [Constants.IgnoreGrowing] = "true"
            }
        });

        Assert.DoesNotContain(
            request.SearchParams,
            parameter => parameter.Key == Constants.IgnoreGrowing);

        var nestedParams = Assert.Single(
            request.SearchParams,
            parameter => parameter.Key == Constants.Params);
        Assert.Equal("""{"ignore_growing":true}""", nestedParams.Value);
    }

    private static Grpc.SearchRequest CreateSearchRequest(SearchParameters parameters)
    {
        using var client = new MilvusClient("localhost", port: 19530, ssl: false);
        MilvusCollection collection = client.GetCollection("test_collection");

        return collection.CreateSearchRequest(
            "vector",
            new Grpc.PlaceholderValue { Tag = Constants.VectorTag },
            SimilarityMetricType.L2,
            limit: 10,
            parameters);
    }
}
