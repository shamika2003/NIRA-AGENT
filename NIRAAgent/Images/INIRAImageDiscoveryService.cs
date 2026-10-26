namespace NIRAAgent.Images;

// Keep candidate/retrieval and final chat artifact contracts unchanged as image
// providers evolve. The candidate type name is legacy, not a provider constraint.
public interface INIRAImageDiscoveryService
{
    string LastSearchStatus { get; }
    Task<IReadOnlyList<NIRACommonsImageCandidate>> SearchAsync(
        string query, int limit, CancellationToken cancellationToken = default,
        bool photographsOnly = false);
    Task<NIRACommonsImageDownload> RetrieveAsync(
        string candidateId, CancellationToken cancellationToken = default);
}
