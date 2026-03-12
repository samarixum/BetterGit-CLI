using LibGit2Sharp;
using Newtonsoft.Json;

namespace BetterGit;

public partial class RepositoryManager {
    /* :: :: Fields :: START :: */

    private readonly string _repoPath;
    private readonly IVersionService _versionService;
    private readonly RemoteService _remoteService;

    /* :: :: Fields :: END :: */
    // //
    /* :: :: Constructors :: START :: */

    public RepositoryManager(string path) {
        _repoPath = path;
        _versionService = new VersionService(path);
        _remoteService = new RemoteService(path);
    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Public API :: START :: */

    public bool IsValidGitRepo() {
        return Repository.IsValid(_repoPath);
    }

    public static void InitProject(string path, bool isNode = false) {
        ProjectInitService.InitProject(path, isNode);
    }

    public static void AddSafeDirectory(string path) {
        RunGitOrThrow(Path.GetDirectoryName(path) ?? path, $"config --global --add safe.directory \"{path.Replace("\\", "/")}\"");
    }

    public void SetChannel(string channel) {
        _versionService.SetChannel(channel);
    }

    public string GetDiffSummary() {
        if (!IsValidGitRepo()) return "Not a git repository.";

        using (Repository repo = new Repository(_repoPath)) {
            // Compare HEAD tree to working directory to get the actual patch/diff
            var patch = repo.Diff.Compare<Patch>(repo.Head.Tip.Tree, DiffTargets.WorkingDirectory);

            if (patch == null || !patch.Any()) {
                return "No changes detected.";
            }

            string diffContent = patch.Content;

            // Protect the AI's context window from massive diffs
            if (diffContent.Length > 20000) {
                diffContent = diffContent.Substring(0, 20000) + "\n... [Diff truncated due to size]";
            }

            return diffContent;
        }
    }

    public string GetVersionInfo() {
        var v = _versionService.GetCurrentVersion();
        string current = $"{v.Major}.{v.Minor}.{v.Patch}";
        if (v.IsAlpha) current += "-A";
        else if (v.IsBeta) current += "-B";

        string last = "None";
        if (IsValidGitRepo()) {
            using (Repository repo = new Repository(_repoPath)) {
                var tip = repo.Head.Tip;
                if (tip != null) {
                    last = ExtractVersion(tip.Message);
                    if (string.IsNullOrEmpty(last) || last == "v?") last = "None";
                }
            }
        }

        return JsonConvert.SerializeObject(new { currentVersion = current, lastCommitVersion = last });
    }

    /* :: :: Public API :: END :: */
}
