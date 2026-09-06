using LibGit2Sharp;

namespace BetterGit;

public partial class RepositoryManager {
    /* :: :: Commands :: START :: */

    // --- COMMAND: SAVE ---
    /// <summary>
    /// Stages and commits parent repository changes while handling child repositories in the background.
    /// </summary>
    public void Save(
        string message,
        VersionChangeType changeType = VersionChangeType.Patch,
        string? manualVersion = null
    ) {
        if (!IsValidGitRepo()) {
            throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
        }

        using (Repository repo = new Repository(_repoPath)) {
            List<(string path, string status)> changes = GetChangesSafe(repo, _repoPath, includeUntracked: true);
            List<string> childRepositoryPaths = GetChangedChildRepositoryPaths(changes);
            List<(string path, string status)> parentChanges = changes
                .Where(change => !childRepositoryPaths.Contains(NormalizeRepositoryPath(change.path), StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (parentChanges.Count == 0) {
                Console.WriteLine("[INFO] No parent changes to save; child repository changes are handled independently.");
                return;
            }

            if (childRepositoryPaths.Count > 0) {
                Console.WriteLine($"[INFO] Saving parent changes with {childRepositoryPaths.Count} child repository reference(s): {string.Join(", ", childRepositoryPaths)}");
            }

            string? version = null;
            try {
                StageAllChanges();
                CommitStagedChanges(message, changeType, manualVersion, changes, ref version);
            } catch (Exception ex) {
                if (childRepositoryPaths.Count == 0) {
                    throw;
                }

                Console.WriteLine($"[INFO] Parent save including child repositories failed: {ex.Message}");
                Console.WriteLine($"[INFO] Retrying parent save without child repositories: {string.Join(", ", childRepositoryPaths)}");
                StageChangesExcluding(childRepositoryPaths);
                CommitStagedChanges(message, changeType, manualVersion, parentChanges, ref version);
                Console.WriteLine("[INFO] Parent save retry completed without child repository references.");
            }
        }
    }

    /* :: :: Commands :: END :: */
    // //
    /* :: :: Private Helpers :: START :: */

    // Commits the current index and writes the version only after included parent changes have been staged.
    private void CommitStagedChanges(
        string message,
        VersionChangeType changeType,
        string? manualVersion,
        List<(string path, string status)> entries,
        ref string? version
    ) {
        if (!HasStagedChanges(_repoPath)) {
            Console.WriteLine("[INFO] No included parent changes were staged.");
            return;
        }

        version ??= _versionService.IncrementVersion(changeType, manualVersion);
        StageMetadataFiles();

        string commitMessage = BuildCommitMessage(message, entries);
        List<string> commitArguments = new List<string>();
        if (!HasGitCommitIdentity()) {
            // Git's --author does not set committer identity, so both values must be supplied for this process.
            commitArguments.AddRange(new[] { "-c", "user.name=BetterGit User", "-c", "user.email=user@bettergit.local" });
        }
        commitArguments.AddRange(new[] { "commit", "-m", $"[{version}] {commitMessage}" });
        RunGitOrThrowWithOutput(_repoPath, commitArguments);
        Console.WriteLine($"Saved successfully: [{version}] {commitMessage}");
    }

    // Builds the human-readable commit summary from the paths included in the final commit attempt.
    private static string BuildCommitMessage(string message, List<(string path, string status)> entries) {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        if (string.IsNullOrWhiteSpace(message)) {
            sb.AppendLine($"changes: {entries.Count}");
        } else {
            sb.AppendLine($"changes: {entries.Count}, {message}");
        }

        sb.AppendLine("Files changed in this commit:");

        foreach ((string path, string status) entry in entries) {
            string stateStr = "modified";
            string s = entry.status;
            if (s.Contains("New") || s.Contains("Added")) {
                stateStr = "added";
            } else if (s.Contains("Deleted")) {
                stateStr = "deleted";
            } else if (s.Contains("Renamed")) {
                stateStr = "renamed";
            }

            sb.AppendLine($"\t{stateStr}:   {entry.path}");
        }
        return sb.ToString().TrimEnd();
    }

    // Stages version metadata with Git so staging follows the same robust path as child repository references.
    private void StageMetadataFiles() {
        List<string> metadataPaths = new List<string> { ".betterGit/project.toml" };
        metadataPaths.AddRange(WebProjectSupport.GetExistingConfigPaths(_repoPath)
            .Select(Path.GetFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Select(fileName => fileName!));

        foreach (string metadataPath in metadataPaths.Distinct(StringComparer.OrdinalIgnoreCase)) {
            RunGitOrThrow(_repoPath, new List<string> { "add", "--", metadataPath });
        }
    }

    // Uses Git rather than LibGit2Sharp because Git reliably stages gitlinks and long working-tree paths.
    private void StageAllChanges() {
        RunGitOrThrow(_repoPath, new List<string> { "add", "-A" });
    }

    // Removes child repository paths from the index without altering their working trees, then stages all other paths.
    private void StageChangesExcluding(List<string> childRepositoryPaths) {
        List<string> resetArguments = new List<string> { "reset", "HEAD", "--" };
        resetArguments.AddRange(childRepositoryPaths);
        RunGitOrThrow(_repoPath, resetArguments);

        List<string> addArguments = new List<string> { "add", "-A", "--", "." };
        foreach (string childRepositoryPath in childRepositoryPaths) {
            addArguments.Add($":(exclude){childRepositoryPath}");
        }
        RunGitOrThrow(_repoPath, addArguments);
    }

    // Finds changed nested repositories and submodules without treating ordinary directories as child repositories.
    private List<string> GetChangedChildRepositoryPaths(List<(string path, string status)> changes) {
        List<string> childPaths = new List<string>();
        foreach ((string path, _) in changes) {
            string normalizedPath = NormalizeRepositoryPath(path);
            if (string.IsNullOrWhiteSpace(normalizedPath)) {
                continue;
            }

            string targetPath = Path.Combine(_repoPath, normalizedPath);
            string gitPath = Path.Combine(targetPath, ".git");
            if (Directory.Exists(targetPath) && (Directory.Exists(gitPath) || File.Exists(gitPath))) {
                childPaths.Add(normalizedPath);
            }
        }

        return childPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Converts a Git status path into a consistent repository-relative pathspec.
    private static string NormalizeRepositoryPath(string path) {
        return path.Replace('\\', '/').Trim().TrimEnd('/');
    }

    // Checks the index instead of the working tree because excluded submodule paths intentionally remain dirty.
    private static bool HasStagedChanges(string repoPath) {
        (int exitCode, string stderr) = RunGit(repoPath, new List<string> { "diff", "--cached", "--quiet" });
        if (exitCode == 0) {
            return false;
        }
        if (exitCode == 1) {
            return true;
        }

        throw new Exception(string.IsNullOrWhiteSpace(stderr) ? "Failed to inspect staged changes." : stderr.Trim());
    }

    // Uses Git's configured identity when available and retains BetterGit's fallback commit identity otherwise.
    private bool HasGitCommitIdentity() {
        (int exitCode, string stdout, _) = RunGitWithOutput(_repoPath, new List<string> { "var", "GIT_AUTHOR_IDENT" });
        return exitCode == 0 && !string.IsNullOrWhiteSpace(stdout);
    }

    /* :: :: Private Helpers :: END :: */
}
