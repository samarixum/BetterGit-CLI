using LibGit2Sharp;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Tomlyn;
using Tomlyn.Model;

namespace BetterGit;

/// <summary>
/// Creates a new Git repository and initializes BetterGit configuration files.
/// </summary>
public class ProjectInitService {
    /* :: :: Public API :: START :: */

    /// <summary>
    /// Initializes a repository at the specified path and seeds BetterGit TOML metadata.
    /// </summary>
    public static void InitProject(string path, bool isNode = false) {
        InitProject(path, new WebProjectOptions { IsNodeProject = isNode });
    }

    /// <summary>
    /// Initializes a repository at the specified path and seeds BetterGit TOML metadata.
    /// </summary>
    public static void InitProject(string path, WebProjectOptions webProject) {
        // 1. Create Directory if it doesn't exist
        if (!Directory.Exists(path)) {
            Directory.CreateDirectory(path);
        }

        // 2. Initialize Git (LibGit2Sharp)
        // This is safe: if a repo already exists, it just re-initializes it without deleting data.
        Repository.Init(path);

        using (Repository repo = new Repository(path)) {
            // Force "main" as the default branch if the repo is empty (fresh init)
            if (!repo.Commits.Any()) {
                // set HEAD to point to refs/heads/main
                repo.Refs.UpdateTarget(repo.Refs["HEAD"], "refs/heads/main");
            }
        }

        // 3. Handle web project metadata & determine the initial version.
        WebProjectOptions requestedProject = webProject ?? new WebProjectOptions();
        WebProjectOptions detectedProject = WebProjectSupport.Detect(path);
        WebProjectOptions effectiveProject = WebProjectSupport.Merge(requestedProject, detectedProject);

        string packageJsonPath = WebProjectSupport.GetPackageJsonPath(path);
        string denoConfigPath = WebProjectSupport.GetPreferredDenoConfigPath(path);
        long major = 0, minor = 0, patch = 0;
        bool isAlpha = false, isBeta = false;

        // Read the highest version from any existing web project config file.
        bool hasSeedVersion = false;
        foreach (string configPath in WebProjectSupport.GetExistingConfigPaths(path)) {
            if (!WebProjectSupport.TryReadVersion(configPath, out long candidateMajor, out long candidateMinor, out long candidatePatch, out bool candidateAlpha, out bool candidateBeta)) {
                continue;
            }

            if (!hasSeedVersion || IsHigherVersion(candidateMajor, candidateMinor, candidatePatch, major, minor, patch)) {
                major = candidateMajor;
                minor = candidateMinor;
                patch = candidatePatch;
                isAlpha = candidateAlpha;
                isBeta = candidateBeta;
                hasSeedVersion = true;
            }
        }

        string seedVersion = BuildVersionString(major, minor, patch, isAlpha, isBeta);

        if (effectiveProject.IsNodeProject && !File.Exists(packageJsonPath)) {
            // Create a package.json when Node support is enabled but the file does not exist yet.
            JObject pkg = new JObject {
                ["name"] = new DirectoryInfo(path).Name.ToLower().Replace(" ", "-"),
                ["version"] = seedVersion,
                ["description"] = "Initialized by BetterGit"
            };
            File.WriteAllText(packageJsonPath, JsonConvert.SerializeObject(value: pkg, formatting: Formatting.Indented));
        }

        if (effectiveProject.IsDenoProject && !File.Exists(denoConfigPath)) {
            // Deno accepts JSONC, so we create a comment-friendly config file when one is missing.
            string denoTemplate =
                "// Initialized by BetterGit\n" +
                "{\n" +
                $"    \"version\": \"{seedVersion}\"\n" +
                "}\n";
            File.WriteAllText(denoConfigPath, denoTemplate);
        }

        // 4. Create the BetterGit config folder + TOML files.
        BetterGitConfigPaths.MigrateMetaTomlToProjectTomlIfNeeded(path);
        BetterGitConfigPaths.EnsureBetterGitDirExists(path);

        string projectFile = BetterGitConfigPaths.GetProjectTomlPath(path);
        if (!File.Exists(projectFile)) {
            TomlTable toml = new TomlTable {
                ["major"] = major,
                ["minor"] = minor,
                ["patch"] = patch,
                ["isAlpha"] = isAlpha,
                ["isBeta"] = isBeta,
                ["isNodeProject"] = effectiveProject.IsNodeProject,
                ["isDenoProject"] = effectiveProject.IsDenoProject
            };
            File.WriteAllText(projectFile, TomlSupport.WriteTable(toml));
        }

        // Local-only settings and credentials should never be committed.
        string localFile = BetterGitConfigPaths.GetLocalTomlPath(path);
        if (!File.Exists(localFile)) {
            string localTemplate =
                "# BetterGit local configuration (ignored by git)\n" +
                "# User preferences live here (e.g., default publish group).\n";
            File.WriteAllText(localFile, localTemplate);
        }

        string secretsFile = BetterGitConfigPaths.GetSecretsTomlPath(path);
        if (!File.Exists(secretsFile)) {
            string secretsTemplate =
                "# BetterGit secrets (ignored by git)\n" +
                "# Store credentials here (e.g., provider tokens) - never commit this file.\n";
            File.WriteAllText(secretsFile, secretsTemplate);
        }

        // 5. Ensure .gitignore contains BetterGit ignore rules (recommended)
        string ignoreFile = Path.Combine(path, ".gitignore");
        if (!File.Exists(ignoreFile)) {
            // Ignore the .vs folder, bin/obj, and the archive branches metadata if you ever store it in files
            // Also ignore node_modules if Node support is enabled.
            string ignores = "bin/\nobj/\n.vscode/\n";
            if (effectiveProject.IsNodeProject) {
                ignores += "node_modules/\n";
            }

            ignores += ".betterGit/local.toml\n";
            ignores += ".betterGit/secrets.toml\n";
            File.WriteAllText(ignoreFile, ignores);
        } else {
            EnsureGitignoreRules(ignoreFile, new[] { ".betterGit/local.toml", ".betterGit/secrets.toml" });
        }

        Console.WriteLine($"BetterGit initialized in: {path}");
        Console.WriteLine("Ready for first Save.");
    }

    /* :: :: Public API :: END :: */

    private static bool IsHigherVersion(long candidateMajor, long candidateMinor, long candidatePatch, long currentMajor, long currentMinor, long currentPatch) {
        if (candidateMajor > currentMajor) {
            return true;
        }

        if (candidateMajor < currentMajor) {
            return false;
        }

        if (candidateMinor > currentMinor) {
            return true;
        }

        if (candidateMinor < currentMinor) {
            return false;
        }

        return candidatePatch > currentPatch;
    }

    private static string BuildVersionString(long major, long minor, long patch, bool isAlpha, bool isBeta) {
        string version = $"{major}.{minor}.{patch}";
        if (isAlpha) {
            version += "-A";
        } else if (isBeta) {
            version += "-B";
        }

        return version;
    }

    private static void EnsureGitignoreRules(string gitignorePath, IReadOnlyList<string> rules) {
        string content;
        try {
            content = File.ReadAllText(gitignorePath);
        } catch {
            return;
        }

        string normalized = content.Replace("\r\n", "\n");
        HashSet<string> existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in normalized.Split('\n')) {
            string trimmed = line.Trim();
            if (trimmed.Length > 0) {
                existing.Add(trimmed);
            }
        }

        List<string> missing = new List<string>();
        foreach (string rule in rules) {
            if (!existing.Contains(rule)) {
                missing.Add(rule);
            }
        }
        if (missing.Count == 0) {
            return;
        }

        using (StreamWriter writer = new StreamWriter(gitignorePath, append: true)) {
            if (!normalized.EndsWith("\n")) {
                writer.WriteLine();
            }
            writer.WriteLine();
            writer.WriteLine("# BetterGit (local/private files)");
            foreach (string rule in missing) {
                writer.WriteLine(rule);
            }
        }
    }
}
