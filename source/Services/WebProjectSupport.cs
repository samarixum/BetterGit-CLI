using System.Text.Json;
using System.Text.RegularExpressions;

namespace BetterGit;

/// <summary>
/// Shared helpers for detecting and updating Node.js and Deno web project metadata files.
/// </summary>
internal static class WebProjectSupport {
    private static readonly Regex VersionRegex = new Regex("(\"version\"\\s*:\\s*\")(.*?)(\")", RegexOptions.Compiled);

    internal static string PackageJsonFileName => "package.json";
    internal static string DenoJsonFileName => "deno.json";
    internal static string DenoJsoncFileName => "deno.jsonc";

    internal static WebProjectOptions Detect(string repoPath) {
        return new WebProjectOptions {
            IsNodeProject = File.Exists(Path.Combine(repoPath, PackageJsonFileName)),
            IsDenoProject = File.Exists(Path.Combine(repoPath, DenoJsonFileName)) || File.Exists(Path.Combine(repoPath, DenoJsoncFileName))
        };
    }

    internal static WebProjectOptions Merge(WebProjectOptions requested, WebProjectOptions detected) {
        return new WebProjectOptions {
            IsNodeProject = requested.IsNodeProject || detected.IsNodeProject,
            IsDenoProject = requested.IsDenoProject || detected.IsDenoProject
        };
    }

    internal static string GetPackageJsonPath(string repoPath) {
        return Path.Combine(repoPath, PackageJsonFileName);
    }

    internal static string GetPreferredDenoConfigPath(string repoPath) {
        string denoJsonPath = Path.Combine(repoPath, DenoJsonFileName);
        if (File.Exists(denoJsonPath)) {
            return denoJsonPath;
        }

        string denoJsoncPath = Path.Combine(repoPath, DenoJsoncFileName);
        if (File.Exists(denoJsoncPath)) {
            return denoJsoncPath;
        }

        return denoJsoncPath;
    }

    internal static IReadOnlyList<string> GetExistingConfigPaths(string repoPath) {
        List<string> paths = new List<string>();

        string packageJsonPath = GetPackageJsonPath(repoPath);
        if (File.Exists(packageJsonPath)) {
            paths.Add(packageJsonPath);
        }

        string denoJsonPath = Path.Combine(repoPath, DenoJsonFileName);
        if (File.Exists(denoJsonPath)) {
            paths.Add(denoJsonPath);
        }

        string denoJsoncPath = Path.Combine(repoPath, DenoJsoncFileName);
        if (File.Exists(denoJsoncPath)) {
            paths.Add(denoJsoncPath);
        }

        return paths;
    }

    internal static bool TryReadVersion(string filePath, out long major, out long minor, out long patch, out bool isAlpha, out bool isBeta) {
        major = 0;
        minor = 0;
        patch = 0;
        isAlpha = false;
        isBeta = false;

        if (!File.Exists(filePath)) {
            return false;
        }

        try {
            string content = File.ReadAllText(filePath);
            JsonDocumentOptions options = new JsonDocumentOptions {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            using (JsonDocument document = JsonDocument.Parse(content, options)) {
                if (!document.RootElement.TryGetProperty("version", out JsonElement versionElement)) {
                    return false;
                }

                string? version = versionElement.GetString() ?? versionElement.ToString();
                if (string.IsNullOrWhiteSpace(version)) {
                    return false;
                }

                ApplyVersionString(version, ref major, ref minor, ref patch, ref isAlpha, ref isBeta);
                return true;
            }
        } catch {
            return false;
        }
    }

    internal static void UpdateVersionField(string filePath, string versionString) {
        if (!File.Exists(filePath)) {
            return;
        }

        try {
            string content = File.ReadAllText(filePath);
            string pattern = "(\"version\"\\s*:\\s*\")(.*?)(\")";
            Regex regex = new Regex(pattern);
            string newContent = regex.Replace(content, $"${{1}}{versionString}$3", 1);
            if (newContent != content) {
                File.WriteAllText(filePath, newContent);
            }
        } catch {
            // Ignore malformed project files so version bumps remain non-destructive.
        }
    }

    private static void ApplyVersionString(string version, ref long major, ref long minor, ref long patch, ref bool isAlpha, ref bool isBeta) {
        string normalized = version.Trim();

        if (normalized.EndsWith("-A", StringComparison.OrdinalIgnoreCase)) {
            isAlpha = true;
            normalized = normalized[..^2];
        } else if (normalized.EndsWith("-B", StringComparison.OrdinalIgnoreCase)) {
            isBeta = true;
            normalized = normalized[..^2];
        }

        string[] parts = normalized.Split('.');
        if (parts.Length >= 1) long.TryParse(parts[0], out major);
        if (parts.Length >= 2) long.TryParse(parts[1], out minor);
        if (parts.Length >= 3) long.TryParse(parts[2], out patch);
    }
}