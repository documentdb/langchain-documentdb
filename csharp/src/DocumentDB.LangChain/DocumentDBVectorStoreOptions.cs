namespace DocumentDB.LangChain;

/// <summary>Configures DocumentDB-specific vector index and search tuning.</summary>
public sealed class DocumentDBVectorStoreOptions
{
    /// <summary>Gets the number of IVF clusters created by the vector index.</summary>
    public int IvfNumLists { get; init; } = 1;

    /// <summary>Gets the number of IVF clusters searched for each query.</summary>
    public int IvfNumProbes { get; init; } = 1;

    /// <summary>Gets the maximum number of HNSW connections per layer.</summary>
    public int HnswM { get; init; } = 16;

    /// <summary>Gets the HNSW candidate count used during index construction.</summary>
    public int HnswEfConstruction { get; init; } = 64;

    /// <summary>Gets the HNSW candidate count used during search.</summary>
    public int HnswEfSearch { get; init; } = 40;

    /// <summary>Gets the maximum number of DiskANN edges per node.</summary>
    public int DiskAnnMaxDegree { get; init; } = 32;

    /// <summary>Gets the DiskANN candidate count used during index construction.</summary>
    public int DiskAnnBuildCandidates { get; init; } = 50;

    /// <summary>Gets the DiskANN candidate count used during search.</summary>
    public int DiskAnnSearchCandidates { get; init; } = 40;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(IvfNumLists, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(IvfNumProbes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(HnswM, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(HnswM, 100);
        ArgumentOutOfRangeException.ThrowIfLessThan(HnswEfConstruction, 4);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(HnswEfConstruction, 1000);
        ArgumentOutOfRangeException.ThrowIfLessThan(HnswEfConstruction, 2 * HnswM);
        ArgumentOutOfRangeException.ThrowIfLessThan(HnswEfSearch, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(DiskAnnMaxDegree, 20);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(DiskAnnMaxDegree, 2048);
        ArgumentOutOfRangeException.ThrowIfLessThan(DiskAnnBuildCandidates, 10);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(DiskAnnBuildCandidates, 500);
        ArgumentOutOfRangeException.ThrowIfLessThan(DiskAnnSearchCandidates, 10);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(DiskAnnSearchCandidates, 1000);
    }
}