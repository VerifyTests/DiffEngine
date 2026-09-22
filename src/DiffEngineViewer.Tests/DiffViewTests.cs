/// <summary>
/// The mapping between a view's rows and the entry's, which scrolling, selection and switching
/// views all go through. Held as invariants over every row rather than as a few picked cases,
/// because an off by one here moves a selection by a line without anything looking wrong.
/// </summary>
public class DiffViewTests
{
    [Test]
    public async Task Every_row_of_the_entry_finds_the_row_that_shows_it()
    {
        var entry = Entry();
        var view = entry.View(true);

        for (var line = 0; line < entry.TotalRows; line++)
        {
            var row = view.Find(line);
            await Assert.That(view.First(row)).IsLessThanOrEqualTo(line);
            await Assert.That(view.Last(row)).IsGreaterThanOrEqualTo(line);
        }
    }

    [Test]
    public async Task A_shown_row_is_the_entrys_own()
    {
        var entry = Entry();
        var view = entry.View(true);

        for (var row = 0; row < view.Count; row++)
        {
            if (view.IsFolded(row))
            {
                continue;
            }

            await Assert.That(view.Left[row]).IsSameReferenceAs(entry.LeftRows[view.First(row)]);
            await Assert.That(view.Right[row]).IsSameReferenceAs(entry.RightRows[view.First(row)]);
            await Assert.That(view.Last(row)).IsEqualTo(view.First(row));
        }
    }

    /// <summary>
    /// Between them the rows cover the entry once each, the last one running to the end, so no
    /// line is in two rows and none is in no row.
    /// </summary>
    [Test]
    public async Task The_rows_cover_the_entry_exactly_once()
    {
        var entry = Entry();
        var view = entry.View(true);

        var next = 0;
        for (var row = 0; row < view.Count; row++)
        {
            await Assert.That(view.First(row)).IsEqualTo(next);
            next = view.Last(row) + 1;
        }

        await Assert.That(next).IsEqualTo(entry.TotalRows);
    }

    /// <summary>
    /// Changes are never folded, so the minimal view has the same runs of them, only closer
    /// together.
    /// </summary>
    [Test]
    public async Task Both_views_have_the_same_changes()
    {
        var entry = Entry();
        var full = entry.View(false);
        var minimal = entry.View(true);

        var mapped = minimal.Changes.Select(_ => minimal.First(_));

        await Assert.That(string.Join(", ", mapped)).IsEqualTo(string.Join(", ", full.Changes));
        await Assert.That(string.Join(", ", full.Changes)).IsEqualTo("2, 16, 32");
    }

    static QueueEntry Entry() =>
        Fixtures.File(Fixtures.Long(true), Fixtures.Long(false)).Current!;
}
