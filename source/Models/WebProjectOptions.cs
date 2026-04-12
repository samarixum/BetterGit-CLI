namespace BetterGit;

/// <summary>
/// Represents the web project configuration BetterGit should manage for a repository.
/// </summary>
public sealed record WebProjectOptions {
    /// <summary>
    /// Gets or sets whether Node.js support is enabled for the repository.
    /// </summary>
    public bool IsNodeProject { get; init; }

    /// <summary>
    /// Gets or sets whether Deno support is enabled for the repository.
    /// </summary>
    public bool IsDenoProject { get; init; }
}