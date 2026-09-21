namespace NIRAAgent.Authorization;

public sealed record NIRACredentialMetadata
{
    public Guid Id { get; init; }
    public string Origin { get; init; } = string.Empty;
    public string AccountLabel { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset LastUsedAtUtc { get; init; }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(AccountLabel)
            ? Username
            : $"{AccountLabel} ({Username})";
}

public sealed record NIRACredentialMaterial
{
    public required NIRACredentialMetadata Metadata { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Secret { get; init; } = string.Empty;
    // Transient proof of new credential entry via the trusted UI, never persisted.
    public bool FreshTrustedCredential { get; init; }
}

public sealed record NIRACredentialPromptRequest
{
    public string Origin { get; init; } = string.Empty;
    public string RequestedAccountLabel { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public bool ForceReplacement { get; init; }
    public IReadOnlyList<NIRACredentialMetadata> ExistingCredentials { get; init; } =
        Array.Empty<NIRACredentialMetadata>();
}

public enum NIRACredentialPromptChoice
{
    Cancel = 0,
    UseStored = 1,
    UseOnce = 2,
    SaveAndUse = 3
}

public sealed record NIRACredentialPromptResponse
{
    public NIRACredentialPromptChoice Choice { get; init; }
    public Guid? CredentialId { get; init; }
    public string AccountLabel { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string Secret { get; init; } = string.Empty;
}


