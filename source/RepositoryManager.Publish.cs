using LibGit2Sharp;

namespace BetterGit;

public partial class RepositoryManager {
    /* :: :: Commands :: START :: */

    // --- COMMAND: PUBLISH ---
    /// <summary>
    /// Pushes the current branch to configured remotes or establishes a selected remote as its upstream.
    /// </summary>
    public void Publish(string? groupFilter = null, bool? publicFilter = null, string? upstreamRemote = null) {
        if (!IsValidGitRepo()) {
            throw new Exception("Not a valid BetterGit repository. Run 'init' first.");
        }

        using (Repository repo = new Repository(_repoPath)) {
            if (!repo.Network.Remotes.Any()) {
                Console.WriteLine("No remotes configured. Add a remote using 'git remote add <name> <url>' first.");
                return;
            }

            List<RemoteInfo> merged = _remoteService.ListMergedRemotes(repo);
            List<RemoteInfo> targets = merged.Where(r => r.HasGitRemote).ToList();

            if (!string.IsNullOrWhiteSpace(groupFilter)) {
                targets = targets
                    .Where(r => r.Group.Equals(groupFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (publicFilter.HasValue) {
                // For safety, only include remotes that have explicit BetterGit metadata.
                bool wantPublic = publicFilter.Value;
                targets = targets
                    .Where(r => r.HasMetadata && r.IsPublic == wantPublic)
                    .ToList();
            }

            if (targets.Count == 0) {
                Console.WriteLine("No matching remotes found to publish to.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(upstreamRemote)) {
                RemoteInfo? selectedRemote = targets.FirstOrDefault(remote => remote.Name.Equals(upstreamRemote, StringComparison.OrdinalIgnoreCase));
                if (selectedRemote == null) {
                    throw new ArgumentException($"Remote '{upstreamRemote}' does not exist.", nameof(upstreamRemote));
                }

                string localBranchName = repo.Info.IsHeadDetached ? string.Empty : repo.Head.FriendlyName;
                if (string.IsNullOrWhiteSpace(localBranchName)) {
                    throw new InvalidOperationException("Cannot set an upstream while HEAD is detached.");
                }

                Console.WriteLine($"[INFO] Publishing '{localBranchName}' to {selectedRemote.Name} and setting its upstream.");
                RunGitOrThrow(_repoPath, new List<string> { "push", "--set-upstream", selectedRemote.Name, localBranchName });
                Console.WriteLine($"Successfully published to {selectedRemote.Name} and set the upstream branch.");
                return;
            }

            foreach (RemoteInfo remote in targets) {
                Console.WriteLine($"Publishing to {remote.Name}...");

                string localBranchName = repo.Info.IsHeadDetached ? "HEAD" : repo.Head.FriendlyName;
                string remoteBranchName = string.IsNullOrWhiteSpace(remote.Branch) ? localBranchName : remote.Branch;
                bool forcePush = !string.IsNullOrWhiteSpace(remote.Branch);
                string pushSpec = remoteBranchName.Equals(localBranchName, StringComparison.OrdinalIgnoreCase)
                    ? localBranchName
                    : $"{localBranchName}:{remoteBranchName}";

                List<string> arguments = new List<string> { "push" };
                if (forcePush) {
                    arguments.Add("--force");
                }
                arguments.Add(remote.Name);
                arguments.Add(pushSpec);

                (string stdout, string stderr) = RunGitOrThrowWithOutput(_repoPath, arguments);
                if (!string.IsNullOrWhiteSpace(stdout)) {
                    Console.WriteLine(stdout.Trim());
                }
                if (!string.IsNullOrWhiteSpace(stderr)) {
                    Console.Error.WriteLine(stderr.Trim());
                }
                Console.WriteLine($"Successfully published to {remote.Name}.");
            }
        }
    }

    /* :: :: Commands :: END :: */
}
