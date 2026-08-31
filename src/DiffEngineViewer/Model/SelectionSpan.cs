/// <summary>
/// A run of selected characters inside one <see cref="Row"/>, measured in characters of
/// <see cref="RowText.Flatten"/>ed text so a renderer can turn it into cells by multiplying.
/// <para>
/// <see cref="Length"/> is zero when nothing on the row is selected, which is every row of almost
/// every frame. There is no separate "none", because a zero length run is already nothing to draw
/// and a nullable would have to be unwrapped by three heads and flattened across the ABI anyway.
/// </para>
/// </summary>
readonly record struct SelectionSpan(int Start, int Length);
