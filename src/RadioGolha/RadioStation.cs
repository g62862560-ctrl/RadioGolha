namespace RadioGolha;

/// <summary>
/// One entry of the station list (radios.json / the user's saved list):
/// a display name, the stream URL and whether it is a favorite.
/// </summary>
internal sealed class RadioStation
{
    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public bool Favorite { get; set; }

    /// <summary>What the station combo box shows - favorites get a star.</summary>
    public override string ToString() => Favorite ? "★ " + Name : Name;
}
