/// <summary>
/// The issue URL the tray opens after an error. Its title is built from a message that carries a
/// file path, so it holds whatever characters the path does.
/// </summary>
public class IssueLauncherTests
{
    [Test]
    public async Task A_hash_in_the_title_does_not_start_a_fragment()
    {
        // Perfectly ordinary in a solution directory, and everything after it used to be read by
        // the browser as a fragment: no body, and a title ending at the hash
        const string message = @"Cannot start. Failed to read settings: C:\code\C#\settings.json";

        var url = IssueLauncher.BuildUrl(message);

        await Assert.That(url).DoesNotContain("#");
        await Assert.That(Query(url)["title"]).IsEqualTo(message);
        await Assert.That(Query(url)["body"]).IsNotEmpty();
    }

    [Test]
    public async Task An_ampersand_in_the_title_does_not_truncate_it()
    {
        const string message = @"Could not accept 'R&D.received.txt'";

        var url = IssueLauncher.BuildUrl(message);

        await Assert.That(Query(url)["title"]).IsEqualTo(message);
        await Assert.That(Query(url)["body"]).IsNotEmpty();
    }

    /// <summary>
    /// The body is encoded by its callers and handed over already escaped, so encoding the title
    /// must not have changed what reaches GitHub as the body.
    /// </summary>
    [Test]
    public async Task Keeps_the_body_it_is_given()
    {
        var url = IssueLauncher.BuildUrl("TheTitle", WebUtility.UrlEncode("\n * Action: TheAction"));

        await Assert.That(Query(url)["body"]).Contains("* Action: TheAction");
    }

    static Dictionary<string, string> Query(string url) =>
        url[(url.IndexOf('?') + 1)..]
            .Split('&')
            .Select(_ => _.Split('=', 2))
            .ToDictionary(_ => _[0], _ => WebUtility.UrlDecode(_[1]));
}
