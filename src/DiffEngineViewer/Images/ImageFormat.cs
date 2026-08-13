/// <summary>
/// What an image side's bytes turned out to be. Not the same question as
/// <see cref="ImageExtensions"/>, which answers whether a path is an image at all: a file named
/// <c>.png</c> holding something else is an image side with an unrecognized format, and says so.
/// </summary>
enum ImageFormat
{
    Png,
    Jpeg,
    Gif,
    Bmp,
    Webp,
    Ico
}
