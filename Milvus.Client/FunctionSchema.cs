namespace Milvus.Client;

/// <summary>
/// The type of a server-side <see cref="FunctionSchema"/>.
/// </summary>
public enum MilvusFunctionType
{
    /// <summary>Unknown / unspecified.</summary>
    Unknown = 0,

    /// <summary>
    /// BM25 full-text function that derives a sparse vector from one VarChar field (with an
    /// analyzer enabled) and writes it to a sparse-float-vector output field.
    /// </summary>
    BM25 = 1,

    /// <summary>Server-side text embedding function.</summary>
    TextEmbedding = 2,

    /// <summary>Server-side rerank function.</summary>
    Rerank = 3,
}

/// <summary>
/// Describes a server-side function attached to a <see cref="CollectionSchema"/>.
/// Currently used to declare a BM25 function producing a sparse-float-vector output
/// from a VarChar input field with an analyzer enabled.
/// </summary>
public sealed class FunctionSchema
{
    /// <summary>
    /// Creates a new <see cref="FunctionSchema"/>.
    /// </summary>
    public FunctionSchema(
        string name,
        MilvusFunctionType type,
        IReadOnlyList<string> inputFieldNames,
        IReadOnlyList<string> outputFieldNames,
        string? description = null)
    {
        Verify.NotNullOrWhiteSpace(name);
        Verify.NotNull(inputFieldNames);
        Verify.NotNull(outputFieldNames);
        if (inputFieldNames.Count == 0)
        {
            throw new ArgumentException("At least one input field name must be provided", nameof(inputFieldNames));
        }
        if (outputFieldNames.Count == 0)
        {
            throw new ArgumentException("At least one output field name must be provided", nameof(outputFieldNames));
        }

        Name = name;
        Type = type;
        InputFieldNames = inputFieldNames;
        OutputFieldNames = outputFieldNames;
        Description = description;
    }

    /// <summary>The function name.</summary>
    public string Name { get; }

    /// <summary>The function type.</summary>
    public MilvusFunctionType Type { get; }

    /// <summary>Optional description.</summary>
    public string? Description { get; }

    /// <summary>The names of the input fields consumed by the function.</summary>
    public IReadOnlyList<string> InputFieldNames { get; }

    /// <summary>The names of the output fields produced by the function.</summary>
    public IReadOnlyList<string> OutputFieldNames { get; }

    /// <summary>Additional function parameters (e.g. BM25 <c>k1</c>, <c>b</c>).</summary>
    public IDictionary<string, string> Parameters { get; } = new Dictionary<string, string>();

    /// <summary>
    /// Creates a BM25 function that maps a VarChar input field (with analyzer enabled) to a
    /// sparse-float-vector output field.
    /// </summary>
    public static FunctionSchema CreateBm25(
        string name,
        string inputFieldName,
        string outputFieldName,
        string? description = null)
        => new(name, MilvusFunctionType.BM25, [inputFieldName], [outputFieldName], description);
}

