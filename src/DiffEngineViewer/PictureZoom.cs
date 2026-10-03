using System.Globalization;

/// <summary>
/// How far a picture is enlarged past the size that fits its pane, as the step the reader is on.
/// <para>
/// Steps rather than a factor, so the keys, the buttons and a wheel notch all move between the
/// same sizes, and going in three and out three is back where it started. Step 0 is fitted, which
/// is what every picture opens at and what a head draws when it knows nothing about zoom.
/// </para>
/// <para>
/// Relative to the fit rather than to the picture's own pixels: the fit is the one size every
/// head already agrees on, since they all place a picture from <see cref="ImagePane.Width"/> and
/// <see cref="ImagePane.Height"/>, and a step from it is the same enlargement on all three.
/// </para>
/// </summary>
static class PictureZoom
{
    static readonly double[] factors = [1, 1.5, 2, 3, 4, 6, 8, 12, 16];

    /// <summary>
    /// The last step, past which zooming in does nothing.
    /// </summary>
    public static int Last =>
        factors.Length - 1;

    /// <summary>
    /// How many times the fitted size a step is drawn at.
    /// </summary>
    public static double Factor(int step) =>
        factors[Math.Clamp(step, 0, Last)];

    /// <summary>
    /// What the status line calls a step: nothing for a fitted picture, which is what a reader
    /// expects unless told otherwise.
    /// </summary>
    public static string? Describe(int step)
    {
        if (step <= 0)
        {
            return null;
        }

        return string.Create(CultureInfo.InvariantCulture, $"zoom {Factor(step) * 100:0}%");
    }
}

/// <summary>
/// The point of a picture that sits at the middle of what is shown of it, as fractions of its
/// width and height. The same point on both sides, which is the reason for zooming a comparison at
/// all: the two panes show the same part of each picture.
/// <para>
/// Fractions rather than pixels because the two sides can be different sizes, and because only a
/// head knows how many pixels a pane has. A head keeps the point inside what can be shown when it
/// draws, and reports where that left it when the reader drags.
/// </para>
/// </summary>
readonly record struct PanPoint(double X, double Y)
{
    public static PanPoint Centre { get; } = new(0.5, 0.5);
}
