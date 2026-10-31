using System.Reflection;
using System.Text;
using System.Text.Json;

namespace RadioGolha;

/// <summary>
/// Loads and saves the station list.
///
/// * The first run seeds the list from the embedded radios.json (or from a
///   radios.json placed next to the exe, so it stays easy to customize).
/// * After that the list lives in %USERPROFILE%\.radio_golha_stations.json,
///   which is also where added, imported and favorite stations are stored.
/// </summary>
internal static class StationStore
{
    private const string ResourceName = "RadioGolha.radios.json";
    private const string UserFileName = ".radio_golha_stations.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    /// <summary>Per-user station list (JSON array of name/url/favorite objects).</summary>
    public static string UserFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), UserFileName);

    /// <summary>Load the station list, seeding it from radios.json on first run.</summary>
    public static List<RadioStation> Load()
    {
        var existing = TryReadFile(UserFile);
        if (existing is not null)
            return existing;

        var seed = Normalize(LoadSeed());

        // Keep an unreadable user file around instead of silently losing it.
        if (File.Exists(UserFile))
        {
            try
            {
                File.Copy(UserFile, UserFile + ".bak", overwrite: true);
            }
            catch
            {
                // best effort
            }
        }

        Save(seed);
        return seed;
    }

    /// <summary>Write the whole list to the user's station file.</summary>
    public static void Save(IEnumerable<RadioStation> stations)
    {
        try
        {
            var json = JsonSerializer.Serialize(stations.ToList(), JsonOptions);
            File.WriteAllText(UserFile, json, new UTF8Encoding(false));
        }
        catch
        {
            // best effort - same policy as the saved URL
        }
    }

    /// <summary>
    /// Parse a station list. Accepts either a plain JSON array or an object
    /// with a "stations" array, so lists exported by other apps work too.
    /// </summary>
    public static List<RadioStation> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<RadioStation>();

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (!TryGetProperty(root, "stations", out var stations))
                return new List<RadioStation>();
            root = stations;
        }

        if (root.ValueKind != JsonValueKind.Array)
            return new List<RadioStation>();

        List<RadioStation>? parsed;
        try
        {
            parsed = root.Deserialize<List<RadioStation>>(JsonOptions);
        }
        catch (JsonException)
        {
            return new List<RadioStation>();
        }

        return Normalize(parsed ?? new List<RadioStation>());
    }

    /// <summary>Append stations that are not in the list yet; returns how many were added.</summary>
    public static int Merge(List<RadioStation> target, IEnumerable<RadioStation> incoming)
    {
        var added = 0;
        foreach (var station in Normalize(incoming))
        {
            var existing = target.FirstOrDefault(
                s => string.Equals(s.Url, station.Url, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                target.Add(station);
                added++;
            }
            else if (station.Favorite)
            {
                existing.Favorite = true;
            }
        }
        return added;
    }

    private static List<RadioStation> LoadSeed()
    {
        // A radios.json next to the exe wins, so the shipped list can be
        // replaced without rebuilding (still a single exe by default).
        var besideExe = Path.Combine(AppContext.BaseDirectory, "radios.json");
        var external = TryReadFile(besideExe);
        if (external is { Count: > 0 })
            return external;

        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream is null)
                return new List<RadioStation>();

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return Parse(reader.ReadToEnd());
        }
        catch
        {
            return new List<RadioStation>();
        }
    }

    private static List<RadioStation>? TryReadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            return Parse(File.ReadAllText(path, Encoding.UTF8));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Trim values, drop empty URLs and remove duplicate stations.</summary>
    private static List<RadioStation> Normalize(IEnumerable<RadioStation> stations)
    {
        var result = new List<RadioStation>();
        var byUrl = new Dictionary<string, RadioStation>(StringComparer.OrdinalIgnoreCase);

        foreach (var station in stations)
        {
            var url = (station.Url ?? string.Empty).Trim();
            if (url.Length == 0)
                continue;

            var name = (station.Name ?? string.Empty).Trim();
            if (name.Length == 0)
                name = url;

            if (byUrl.TryGetValue(url, out var known))
            {
                known.Favorite |= station.Favorite;
                continue;
            }

            var normalized = new RadioStation { Name = name, Url = url, Favorite = station.Favorite };
            byUrl.Add(url, normalized);
            result.Add(normalized);
        }

        return result;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            value = property.Value;
            return true;
        }

        value = default;
        return false;
    }
}
