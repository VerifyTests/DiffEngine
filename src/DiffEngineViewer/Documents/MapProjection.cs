/// <summary>
/// How a map's longitude and latitude are laid out when it is drawn. A view setting like
/// <see cref="DrawingView"/>: it changes what is looked at, never a file, and both sides of a
/// comparison are always drawn the same way.
/// <para>
/// The names are GeoConvert's, since the name is what crosses to the documents assembly: this
/// assembly has no reference to that one, and only BCL types pass between them.
/// </para>
/// </summary>
enum MapProjection
{
    /// <summary>
    /// Chosen from what the map covers: a conic for a region, equirectangular for a continent, an
    /// equal area one for the world.
    /// </summary>
    Auto,
    PlateCarree,
    WebMercator,
    Lambert,
    Goode
}

static class MapProjections
{
    /// <summary>
    /// What a projection is called on the button that switches it.
    /// </summary>
    public static string Name(MapProjection projection) =>
        projection switch
        {
            MapProjection.PlateCarree => "Equirectangular",
            MapProjection.WebMercator => "Web Mercator",
            MapProjection.Lambert => "Lambert conic",
            MapProjection.Goode => "Goode homolosine",
            _ => "Auto"
        };

    /// <summary>
    /// The one after, round to the first again.
    /// </summary>
    public static MapProjection Next(MapProjection projection) =>
        projection switch
        {
            MapProjection.Auto => MapProjection.PlateCarree,
            MapProjection.PlateCarree => MapProjection.WebMercator,
            MapProjection.WebMercator => MapProjection.Lambert,
            MapProjection.Lambert => MapProjection.Goode,
            _ => MapProjection.Auto
        };
}
