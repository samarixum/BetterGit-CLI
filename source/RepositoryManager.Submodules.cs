using LibGit2Sharp;

namespace BetterGit;

public partial class RepositoryManager {
    /* :: :: Commands :: START :: */

    /// <summary>
    /// Converts an existing nested repository into a submodule of this repository without deleting its working tree.
    /// </summary>
    public void ConvertNestedRepositoryToSubmodule(string relativePath, string url) {
        if (!IsValidGitRepo()) {
            throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
        }
        if (string.IsNullOrWhiteSpace(relativePath)) {
            throw new ArgumentException("Nested repository path is required.", nameof(relativePath));
        }
        if (string.IsNullOrWhiteSpace(url)) {
            throw new ArgumentException("Submodule URL is required.", nameof(url));
        }

        string normalizedPath = NormalizeRepositoryRelativePath(relativePath);
        string repositoryRoot = Path.GetFullPath(_repoPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string childPath = Path.GetFullPath(Path.Combine(repositoryRoot, normalizedPath));
        if (!IsPathInsideRepository(repositoryRoot, childPath)) {
            throw new ArgumentException("Nested repository path must be inside the parent repository.", nameof(relativePath));
        }
        if (!Repository.IsValid(childPath)) {
            throw new ArgumentException("The selected path is not a valid nested Git repository.", nameof(relativePath));
        }

        Console.WriteLine($"[INFO] Converting nested repository '{normalizedPath}' into a submodule using its existing working tree.");
        RunGitOrThrow(_repoPath, new List<string> { "submodule", "add", "--force", url, normalizedPath });
        Console.WriteLine($"[INFO] Converted '{normalizedPath}' to a submodule. Git may replace its .git directory with a gitdir file; its working files remain in place.");
    }

    /* :: :: Commands :: END :: */
    // //
    /* :: :: Private Helpers :: START :: */

    // Converts external user input into a Git repository-relative path and rejects empty roots.
    private static string NormalizeRepositoryRelativePath(string relativePath) {
        if (Path.IsPathRooted(relativePath)) {
            throw new ArgumentException("Nested repository path must be repository-relative.", nameof(relativePath));
        }

        string normalizedPath = relativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .Trim()
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalizedPath) || normalizedPath == ".") {
            throw new ArgumentException("Nested repository path must identify a child directory.", nameof(relativePath));
        }

        return normalizedPath;
    }

    // Ensures the target path remains under the parent repository with a directory-separator boundary.
    private static bool IsPathInsideRepository(string repositoryRoot, string candidatePath) {
        string rootWithSeparator = repositoryRoot + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    /* :: :: Private Helpers :: END :: */
}
