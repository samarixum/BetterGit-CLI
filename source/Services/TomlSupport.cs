using Tomlyn;
using Tomlyn.Model;

namespace BetterGit;

/// <summary>
/// Provides shared Tomlyn 2.x helpers for loading and saving <see cref="TomlTable"/> values.
/// </summary>
internal static class TomlSupport {
    /// <summary>
    /// Loads a TOML table from disk and falls back to an empty table when the file is missing or invalid.
    /// </summary>
    public static TomlTable ReadTable(string filePath) {
        if (!File.Exists(filePath)) {
            return new TomlTable();
        }

        try {
            string content = File.ReadAllText(filePath);
            object? model = TomlSerializer.Deserialize(
                toml: content,
                returnType: typeof(TomlTable),
                options: new TomlSerializerOptions()
            );

            if (model is TomlTable table) {
                return table;
            }
        } catch {
            // Keep behavior forgiving when TOML is corrupt or partially written.
        }

        return new TomlTable();
    }

    /// <summary>
    /// Serializes a TOML table using Tomlyn's runtime serializer.
    /// </summary>
    public static string WriteTable(TomlTable table) {
        return TomlSerializer.Serialize(
            value: table,
            inputType: typeof(TomlTable),
            options: new TomlSerializerOptions()
        );
    }
}