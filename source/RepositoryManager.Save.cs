using LibGit2Sharp;

namespace BetterGit;

public partial class RepositoryManager {
    /* :: :: Commands :: START :: */

    // --- COMMAND: SAVE ---
    /// <summary>
    /// Stages and commits the repository's changes, optionally leaving selected repository-relative paths unstaged.
    /// </summary>
    public void Save(
        string message,
        VersionChangeType changeType = VersionChangeType.Patch,
        string? manualVersion = null,
        IEnumerable<string>? excludedPaths = null
    ) {
        if (!IsValidGitRepo()) {
            throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
        }

        using (Repository repo = new Repository(_repoPath)) {
            StageChanges(excludedPaths);

            // Excluded paths can leave the working tree dirty while the index has no included changes.
            if (!HasStagedChanges(_repoPath)) {
                Console.WriteLine("No included changes to save.");
                return;
            }

            // Generate final message format
            List<(string path, string status)> entries = GetChangesSafe(repo, _repoPath, includeUntracked: true);
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
            message = sb.ToString().TrimEnd();

            // 3. Update Version
            string version = _versionService.IncrementVersion(changeType, manualVersion);

            // Stage the metadata files explicitly to be sure
            try {
                Commands.Stage(repo, ".betterGit/project.toml");
            } catch (Exception ex) {
                if (IsPathTooLongError(ex)) {
                    RunGitOrThrow(_repoPath, "add .betterGit/project.toml");
                } else {
                    throw;
                }
            }
            foreach (string configPath in WebProjectSupport.GetExistingConfigPaths(_repoPath)) {
                string fileName = Path.GetFileName(configPath);
                try {
                    Commands.Stage(repo, fileName);
                } catch (Exception ex) {
                    if (IsPathTooLongError(ex)) {
                        RunGitOrThrow(_repoPath, $"add {fileName}");
                    } else {
                        throw;
                    }
                }
            }

            // 4. Commit
            Signature author = repo.Config.BuildSignature(DateTime.Now);
            if (author == null) {
                author = new Signature(name: "BetterGit User", email: "user@bettergit.local", when: DateTime.Now);
            }

            repo.Commit($"[{version}] {message}", author, author);

            Console.WriteLine($"Saved successfully: [{version}] {message}");
        }
    }

    /* :: :: Commands :: END :: */
    // //
    /* :: :: Private Helpers :: START :: */

    // Stages every change except explicitly excluded repository-relative paths, including gitlink entries.
    private void StageChanges(IEnumerable<string>? excludedPaths) {
        List<string> normalizedExcludedPaths = NormalizeExcludedPaths(excludedPaths);
        if (normalizedExcludedPaths.Count == 0) {
            RunGitOrThrow(_repoPath, new List<string> { "add", "-A" });
            return;
        }

        // First unstage selected paths so a gitlink that was staged before this save cannot be committed accidentally.
        List<string> resetArguments = new List<string> { "reset", "HEAD", "--" };
        resetArguments.AddRange(normalizedExcludedPaths);
        RunGitOrThrow(_repoPath, resetArguments);

        List<string> addArguments = new List<string> { "add", "-A", "--", "." };
        foreach (string excludedPath in normalizedExcludedPaths) {
            addArguments.Add($":(exclude){excludedPath}");
        }
        RunGitOrThrow(_repoPath, addArguments);
    }

    // Validates external CLI paths and converts them to Git's repository-relative separator style.
    private List<string> NormalizeExcludedPaths(IEnumerable<string>? excludedPaths) {
        List<string> normalizedPaths = new List<string>();
        if (excludedPaths == null) {
            return normalizedPaths;
        }

        string repositoryRoot = Path.GetFullPath(_repoPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (string excludedPath in excludedPaths) {
            if (string.IsNullOrWhiteSpace(excludedPath) || Path.IsPathRooted(excludedPath)) {
                throw new ArgumentException("Excluded paths must be non-empty repository-relative paths.");
            }

            string fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, excludedPath));
            string relativePath = Path.GetRelativePath(repositoryRoot, fullPath);
            if (relativePath == ".." || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) {
                throw new ArgumentException("Excluded paths must remain inside the repository.");
            }

            string gitPath = relativePath
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/')
                .TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(gitPath) && !normalizedPaths.Contains(gitPath, StringComparer.Ordinal)) {
                normalizedPaths.Add(gitPath);
            }
        }

        return normalizedPaths;
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

    /* :: :: Private Helpers :: END :: */
}
