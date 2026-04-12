using LibGit2Sharp;

using System.Diagnostics;

namespace BetterGit;

public partial class RepositoryManager {
    /* :: :: Commands :: START :: */

    // --- COMMAND: PUBLISH ---
    public void Publish(string? groupFilter = null, bool? publicFilter = null) {
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

                ProcessStartInfo processInfo = new ProcessStartInfo(fileName: "git") {
                    WorkingDirectory = _repoPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                foreach (string argument in arguments) {
                    processInfo.ArgumentList.Add(argument);
                }

                Process? process = Process.Start(processInfo);
                if (process != null) {
                    using (process) {
                        process.OutputDataReceived += (_, eventArgs) => {
                            if (!string.IsNullOrWhiteSpace(eventArgs.Data)) {
                                Console.WriteLine(eventArgs.Data);
                            }
                        };

                        process.ErrorDataReceived += (_, eventArgs) => {
                            if (!string.IsNullOrWhiteSpace(eventArgs.Data)) {
                                Console.Error.WriteLine(eventArgs.Data);
                            }
                        };

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();

                        if (process.ExitCode == 0) {
                            Console.WriteLine($"Successfully published to {remote.Name}.");
                        } else {
                            Console.Error.WriteLine($"Failed to publish to {remote.Name}.");
                        }
                    }
                }
            }
        }
    }

    /* :: :: Commands :: END :: */
}
