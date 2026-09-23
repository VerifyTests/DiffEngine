/// <summary>
/// The wire format between DiffEngine, DiffEngineTray and DiffEngineViewer.
/// <para>
/// Every value is base64 for the same reason <see cref="InlinePatchFile"/>'s are: snapshot text
/// routinely contains quotes, braces and newlines, and the `inline` body carries a whole
/// InlinePatchFile payload verbatim rather than escaped into something else. These tests are the
/// only thing pinning that shape, and they run on every framework DiffEngine targets, which is
/// where the async socket paths differ.
/// </para>
/// </summary>
public class ViewerProtocolTests
{
    // These pin the wire shape rather than what a reviewer reads, so nothing here is named. The
    // one test that is about the name says so itself.
    static InlinePatch Patch(
        string source,
        int line,
        string? expression,
        string content,
        InlinePatchMode mode = InlinePatchMode.Set,
        string? framework = null) =>
        new(source, line, expression, content, mode)
        {
            TestName = null,
            Framework = framework
        };

    [Test]
    public async Task InlineMessageRoundTrips()
    {
        var patch = Patch("Tests.cs", 42, "\"old\"", "new content");

        var payload = new ViewerMessage(ViewerVerb.Inline, Body: InlinePatchFile.Build(patch)).Build();

        await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
        await Assert.That(message!.Verb).IsEqualTo(ViewerVerb.Inline);
        await Assert.That(InlinePatchFile.TryParse(message.Body!, out var roundTripped)).IsTrue();
        await Assert.That(roundTripped!.SourceFile).IsEqualTo("Tests.cs");
        await Assert.That(roundTripped.LineHint).IsEqualTo(42);
        await Assert.That(roundTripped.OriginalExpression).IsEqualTo("\"old\"");
        await Assert.That(roundTripped.NewContent).IsEqualTo("new content");
    }

    /// <summary>
    /// Including text that looks like the protocol itself, which is what the encoding is for.
    /// </summary>
    [Test]
    public async Task AwkwardSnapshotTextSurvivesTheRoundTrip()
    {
        var content = "line \"one\"\n\tbraces {} and | pipes\r\nversion: 1\nverb: quit\n";
        var patch = Patch("Tests.cs", 1, null, content);

        var payload = new ViewerMessage(ViewerVerb.Inline, Body: InlinePatchFile.Build(patch)).Build();

        await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
        await Assert.That(InlinePatchFile.TryParse(message!.Body!, out var roundTripped)).IsTrue();
        await Assert.That(roundTripped!.NewContent).IsEqualTo(content);
        await Assert.That(roundTripped.OriginalExpression).IsNull();
    }

    /// <summary>
    /// The mode is written by name, so every member has to survive the trip. Enumerated rather
    /// than listed, so one added later is covered without anyone remembering to add it here.
    /// </summary>
    [Test]
    public async Task EveryModeRoundTrips()
    {
        foreach (var name in Enum.GetNames(typeof(InlinePatchMode)))
        {
            var mode = (InlinePatchMode) Enum.Parse(typeof(InlinePatchMode), name);
            var patch = Patch("Tests.cs", 1, null, "content", mode);
            var payload = new ViewerMessage(ViewerVerb.Inline, Body: InlinePatchFile.Build(patch)).Build();

            await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
            await Assert.That(InlinePatchFile.TryParse(message!.Body!, out var roundTripped)).IsTrue();
            await Assert.That(roundTripped!.Mode).IsEqualTo(mode);
        }
    }

    /// <summary>
    /// Verbs go on the wire lower cased and parse back case insensitively, so a two word one like
    /// acceptall is not quietly a different verb at each end.
    /// </summary>
    [Test]
    public async Task EveryVerbRoundTrips()
    {
        foreach (var name in Enum.GetNames(typeof(ViewerVerb)))
        {
            var verb = (ViewerVerb) Enum.Parse(typeof(ViewerVerb), name);

            await Assert.That(new ViewerMessage(verb).Build()).Contains($"verb: {name.ToLowerInvariant()}\n");
            await Assert.That(ViewerMessage.TryParse(new ViewerMessage(verb).Build(), out var message)).IsTrue();
            await Assert.That(message!.Verb).IsEqualTo(verb);
        }
    }

    [Test]
    public async Task SettleCarriesTheKey()
    {
        // Already lower case, so what the key survives is the round trip rather than the folding
        var payload = new ViewerMessage(ViewerVerb.Settle, InlineKey.For("tests.cs", 42)).Build();

        await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
        await Assert.That(message!.Verb).IsEqualTo(ViewerVerb.Settle);
        await Assert.That(message.Key).IsEqualTo("tests.cs|42");
    }

    /// <summary>
    /// Settling only works if the sender and the queue owner derive the same key from the same
    /// call site, so the format is pinned rather than left to whatever ToLower happens to do.
    /// </summary>
    /// These are already lower case, so they say the same thing wherever they run.
    [Test]
    [Arguments("tests.cs", 42, "tests.cs|42")]
    [Arguments(@"c:\repo\some.tests\sample.cs", 1, @"c:\repo\some.tests\sample.cs|1")]
    [Arguments("/home/user/sample.cs", 9999, "/home/user/sample.cs|9999")]
    public async Task KeyFormat(string sourceFile, int line, string expected) =>
        await Assert.That(InlineKey.For(sourceFile, line)).IsEqualTo(expected);

    /// <summary>
    /// A Windows path reaches here from several sources with several casings, and every one of
    /// them is the same call site.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    [Arguments("Tests.cs", 42, "tests.cs|42")]
    [Arguments(@"C:\Repo\Some.Tests\Sample.cs", 1, @"c:\repo\some.tests\sample.cs|1")]
    [Arguments("MiXeDCase.CS", 7, "mixedcase.cs|7")]
    public async Task KeyIsFoldedWhereThePathsAre(string sourceFile, int line, string expected) =>
        await Assert.That(InlineKey.For(sourceFile, line)).IsEqualTo(expected);

    /// <summary>
    /// And not where they are not. On Linux these are two files, and one key for both meant the
    /// second patch took over the first's entry and settling either settled both.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Linux)]
    public async Task KeysDifferingOnlyInCaseStayApartWhereTheFilesDo()
    {
        await Assert.That(InlineKey.For("/home/user/Sample.cs", 1)).IsEqualTo("/home/user/Sample.cs|1");
        await Assert.That(InlineKey.For("/home/user/sample.cs", 1))
            .IsNotEqualTo(InlineKey.For("/home/user/Sample.cs", 1));
    }

    /// <summary>
    /// A newer sender can add a field without breaking an older owner.
    /// </summary>
    [Test]
    public async Task AnUnknownFieldIsIgnored()
    {
        await Assert.That(ViewerMessage.TryParse("version: 1\nverb: quit\nwat: nope\n", out var message)).IsTrue();
        await Assert.That(message!.Verb).IsEqualTo(ViewerVerb.Quit);
    }

    [Test]
    [Arguments("")]
    [Arguments("verb: quit\n")]
    [Arguments("version: 99\nverb: quit\n")]
    [Arguments("version: 1\nverb: nonsense\n")]
    [Arguments("version: 1\n")]
    public async Task UnreadableRequestsAreRejected(string text) =>
        await Assert.That(ViewerMessage.TryParse(text, out _)).IsFalse();

    /// <summary>
    /// DiffEngine's client treats this literal as the acknowledgement, so the owner has to keep
    /// emitting it.
    /// </summary>
    [Test]
    public async Task TheAcknowledgementIsWhatTheClientLooksFor()
    {
        await Assert.That(ViewerResponse.Success().Build()).Contains("status: ok");
        await Assert.That(ViewerResponse.Success("queued 1").Build()).Contains("status: ok");
        await Assert.That(ViewerResponse.Error("nope").Build()).DoesNotContain("status: ok");
    }

    [Test]
    public async Task AListingItemCarriesKeyNameAndStatus()
    {
        var text = ViewerResponse.Listing([new("the key", "Sample.cs:42", "locked")]).Build();

        var parts = Fields(text, "item: ").Single();
        await Assert.That(parts.Length).IsEqualTo(3);
        await Assert.That(Decoded(parts[0])).IsEqualTo("the key");
        await Assert.That(Decoded(parts[1])).IsEqualTo("Sample.cs:42");
        await Assert.That(Decoded(parts[2])).IsEqualTo("locked");
    }

    /// <summary>
    /// A full listing carries the payload each entry was queued from, which is what lets a viewer
    /// showing a queue it does not own rebuild every pane without a diff crossing the wire.
    /// </summary>
    [Test]
    public async Task AFullListingRoundTripsThePatch()
    {
        var patch = Patch("Tests.cs", 42, "\"old\"", "new content");
        var listing = ViewerResponse.Listing(
        [
            new("tests.cs|42", "Tests.cs:42", "locked", InlinePatchFile.Build(patch))
        ]);

        await Assert.That(ViewerResponse.TryParse(listing.Build(), out var parsed)).IsTrue();
        var item = parsed!.Items.Single();
        await Assert.That(item.Key).IsEqualTo("tests.cs|42");
        await Assert.That(item.Status).IsEqualTo("locked");
        await Assert.That(InlinePatchFile.TryParse(item.Patch!, out var roundTripped)).IsTrue();
        await Assert.That(roundTripped!.SourceFile).IsEqualTo("Tests.cs");
        await Assert.That(roundTripped.LineHint).IsEqualTo(42);
        await Assert.That(roundTripped.NewContent).IsEqualTo("new content");
    }

    /// <summary>
    /// Patches ride on their own line name, so a reader that only wants a listing skips them
    /// rather than tripping over a fourth field.
    /// </summary>
    [Test]
    public async Task AFullListingHasNoItemLines()
    {
        var patch = InlinePatchFile.Build(Patch("Tests.cs", 1, null, "content"));
        var text = ViewerResponse.Listing([new("key", "Tests.cs:1", null, patch)]).Build();

        await Assert.That(Fields(text, "item: ")).IsEmpty();
        await Assert.That(Fields(text, "full: ").Single().Length).IsEqualTo(5);
    }

    [Test]
    public async Task MetadataRidesTheInlineBody()
    {
        var patch = new InlinePatch("Tests.cs", 42, "\"old\"", "new content")
        {
            TestName = "Compare handles nulls",
            Framework = "net9.0"
        };

        var payload = new ViewerMessage(ViewerVerb.Inline, Body: InlinePatchFile.Build(patch)).Build();

        await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
        await Assert.That(InlinePatchFile.TryParse(message!.Body!, out var roundTripped)).IsTrue();
        await Assert.That(roundTripped!.TestName).IsEqualTo("Compare handles nulls");
        await Assert.That(roundTripped.Framework).IsEqualTo("net9.0");
    }

    /// <summary>
    /// The body is the settling framework, so a multi-targeted run only settles its own variant.
    /// </summary>
    [Test]
    public async Task SettleCarriesTheOriginInTheBody()
    {
        var payload = new ViewerMessage(ViewerVerb.Settle, InlineKey.For("tests.cs", 42), "net9.0").Build();

        await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
        await Assert.That(message!.Key).IsEqualTo("tests.cs|42");
        await Assert.That(message.Body).IsEqualTo("net9.0");
    }

    [Test]
    public async Task SettleCarriesTheMember()
    {
        var payload = new ViewerMessage(ViewerVerb.Settle, InlineKey.For("tests.cs", 42), "net9.0", "MyTest")
            .Build();

        await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
        await Assert.That(message!.Key).IsEqualTo("tests.cs|42");
        await Assert.That(message.Body).IsEqualTo("net9.0");
        await Assert.That(message.Member).IsEqualTo("MyTest");
    }

    /// <summary>
    /// The member is an added field, so a payload written before it existed still reads, which is
    /// what lets a newer sender talk to an older owner.
    /// </summary>
    [Test]
    public async Task SettleWithoutAMemberParses()
    {
        var payload = new ViewerMessage(ViewerVerb.Settle, InlineKey.For("tests.cs", 42), "net9.0").Build();

        await Assert.That(ViewerMessage.TryParse(payload, out var message)).IsTrue();
        await Assert.That(message!.Member).IsNull();
    }

    [Test]
    public async Task AFullListingCarriesThePrimaryOrigins()
    {
        var patch = InlinePatchFile.Build(Patch("Tests.cs", 42, "\"old\"", "new content"));
        var listing = ViewerResponse.Listing(
        [
            new("tests.cs|42", "Tests.cs:42", null, patch)
            {
                Origins = ["net8.0", "net9.0"]
            }
        ]);

        var text = listing.Build();
        await Assert.That(Decoded(Fields(text, "full: ").Single()[3])).IsEqualTo("net8.0,net9.0");
        await Assert.That(ViewerResponse.TryParse(text, out var parsed)).IsTrue();
        await Assert.That(parsed!.Items.Single().Origins).IsEquivalentTo(["net8.0", "net9.0"]);
    }

    /// <summary>
    /// Non-primary variants ride their own line name, keyed back to their entry, so an entry line
    /// stays one per call site however many frameworks disagree about it.
    /// </summary>
    [Test]
    public async Task AVariantLineRoundTrips()
    {
        var primary = InlinePatchFile.Build(Patch("Tests.cs", 42, "\"old\"", "eight"));
        var other = InlinePatchFile.Build(Patch("Tests.cs", 42, "\"old\"", "nine"));
        var listing = ViewerResponse.Listing(
        [
            new("tests.cs|42", "Tests.cs:42", null, primary)
            {
                Origins = ["net8.0"],
                Variants = [new(["net9.0"], other)]
            }
        ]);

        var text = listing.Build();
        await Assert.That(Fields(text, "variant: ").Single().Length).IsEqualTo(3);
        await Assert.That(ViewerResponse.TryParse(text, out var parsed)).IsTrue();
        var variant = parsed!.Items.Single().Variants.Single();
        await Assert.That(variant.Origins).IsEquivalentTo(["net9.0"]);
        await Assert.That(InlinePatchFile.TryParse(variant.Patch, out var roundTripped)).IsTrue();
        await Assert.That(roundTripped!.NewContent).IsEqualTo("nine");
    }

    [Test]
    public async Task AMoveLineRoundTrips()
    {
        var listing = ViewerResponse.Listing(
            [],
            moves: [new(@"move:c:\temp\a.received.txt", "Sample.Test (txt)", "MySolution", @"c:\temp\a.received.txt", @"c:\code\a.verified.txt")]);

        var text = listing.Build();
        await Assert.That(Fields(text, "move: ").Single().Length).IsEqualTo(5);
        await Assert.That(ViewerResponse.TryParse(text, out var parsed)).IsTrue();
        var move = parsed!.Moves.Single();
        await Assert.That(move.Key).IsEqualTo(@"move:c:\temp\a.received.txt");
        await Assert.That(move.Group).IsEqualTo("MySolution");
        await Assert.That(move.Temp).IsEqualTo(@"c:\temp\a.received.txt");
        await Assert.That(move.Target).IsEqualTo(@"c:\code\a.verified.txt");
    }

    [Test]
    public async Task ADeleteLineRoundTrips()
    {
        var listing = ViewerResponse.Listing(
            [],
            deletes: [new(@"delete:c:\code\extra.verified.txt", "extra.verified.txt", null, @"c:\code\extra.verified.txt")]);

        var text = listing.Build();
        await Assert.That(Fields(text, "delete: ").Single().Length).IsEqualTo(4);
        await Assert.That(ViewerResponse.TryParse(text, out var parsed)).IsTrue();
        var delete = parsed!.Deletes.Single();
        await Assert.That(delete.Group).IsNull();
        await Assert.That(delete.File).IsEqualTo(@"c:\code\extra.verified.txt");
    }

    [Test]
    public async Task AListingWithoutTrackedItemsParsesEmpty()
    {
        var text = ViewerResponse.Listing([new("key", "Sample.cs:42", null)]).Build();

        await Assert.That(ViewerResponse.TryParse(text, out var parsed)).IsTrue();
        await Assert.That(parsed!.Moves).IsEmpty();
        await Assert.That(parsed.Deletes).IsEmpty();
        await Assert.That(parsed.Items.Single().Variants).IsEmpty();
    }

    // Field counts are strict, like item and full: growth means a new line name, never a new field
    [Test]
    public async Task AMalformedMoveLineRejectsTheResponse()
    {
        var valid = ViewerResponse.Listing(
            [],
            moves: [new("move:x", "x", null, "x", "y")]).Build();
        var truncated = valid.Replace(
            $"|{ViewerPayload.Encode("y")}\n",
            "\n");

        await Assert.That(ViewerResponse.TryParse(truncated, out _)).IsFalse();
    }

    /// <summary>
    /// The routing contract: only the tray parses keys, and it tells its collections apart by
    /// prefix, which an inline key can never carry because a Windows path cannot put a colon
    /// there.
    /// <para>
    /// The path is folded the way InlineKey folds it, which is not the same on every platform: on
    /// Linux two paths differing only in case are two files. So the expectation is built from that
    /// rule rather than written out lower cased, which pinned the Windows answer everywhere and
    /// failed on Linux. That the two rules agree is TrackedKeyCaseTests; this is about the prefix.
    /// </para>
    /// </summary>
    [Test]
    public async Task TrackedKeysCannotCollideWithInlineKeys()
    {
        var move = @"C:\Temp\A.txt";
        var delete = @"C:\Code\B.txt";
        await Assert.That(TrackedKeys.ForMove(move)).IsEqualTo("move:" + InlineKey.FoldPath(move));
        await Assert.That(TrackedKeys.ForDelete(delete)).IsEqualTo("delete:" + InlineKey.FoldPath(delete));
        await Assert.That(TrackedKeys.IsTracked(@"move:c:\temp\a.txt")).IsTrue();
        await Assert.That(TrackedKeys.IsTracked(@"delete:c:\code\b.txt")).IsTrue();
        await Assert.That(TrackedKeys.IsTracked(InlineKey.For(@"C:\Repo\Tests.cs", 42))).IsFalse();
        await Assert.That(TrackedKeys.TryStrip(@"move:c:\temp\a.txt", TrackedKeys.MovePrefix, out var path)).IsTrue();
        await Assert.That(path).IsEqualTo(@"c:\temp\a.txt");
    }

    /// <summary>
    /// The shared projection both hosts list through. Without patches the conflict has to ride the
    /// status, because there are no variant lines to carry it; with them it stays structural and
    /// the real status is preserved.
    /// </summary>
    [Test]
    public async Task AConflictedEntryListsItsStatus()
    {
        var eight = Patch("Tests.cs", 42, "\"old\"", "eight", framework: "net8.0");
        var nine = Patch("Tests.cs", 42, "\"old\"", "nine", framework: "net9.0");
        var entry = new PendingInline([new(eight, ["net8.0"]), new(nine, ["net9.0"])]);

        var listed = ViewerListing.Items([entry], withPatches: false).Single();
        await Assert.That(listed.Status).IsEqualTo("Conflicting snapshots (net8.0 / net9.0)");

        var full = ViewerListing.Items([entry], withPatches: true).Single();
        await Assert.That(full.Status).IsNull();
        await Assert.That(full.Origins).IsEquivalentTo(["net8.0"]);
        await Assert.That(full.Variants).HasSingleItem();
    }

    /// <summary>
    /// The wire mapping for refusals: an entry that exists but was not acted on answers an error
    /// carrying the reason, distinct from the unknown-key error, so a remote surface shows why
    /// nothing happened.
    /// </summary>
    [Test]
    public async Task ARefusedAcceptGoesOnTheWireAsAnError()
    {
        var refusing = new FakeOwner((false, "Conflicting snapshots (net8.0 / net9.0), resolve in the viewer"));
        var refused = ViewerMessageHandler.Handle(refusing, new(ViewerVerb.Accept, "key"));
        await Assert.That(refused.Ok).IsFalse();
        await Assert.That(refused.Message).IsEqualTo("Conflicting snapshots (net8.0 / net9.0), resolve in the viewer");

        var unknown = ViewerMessageHandler.Handle(new FakeOwner((false, null)), new(ViewerVerb.Accept, "key"));
        await Assert.That(unknown.Ok).IsFalse();
        await Assert.That(unknown.Message).IsEqualTo("No pending snapshot for key");

        var done = ViewerMessageHandler.Handle(new FakeOwner((true, "Applied Tests.cs:42")), new(ViewerVerb.Accept, "key"));
        await Assert.That(done.Ok).IsTrue();
        await Assert.That(done.Message).IsEqualTo("Applied Tests.cs:42");
    }

    /// <summary>
    /// Whether an accept left its snapshot in the source, which ok cannot say: a patch whose call
    /// site moved is attempted, so ok, and dropped unwritten. A surface accepting a group from
    /// someone else's queue sends the group's deletes on it. A reply without it, from an owner
    /// that predates it, reads as null, which that surface takes as not written.
    /// </summary>
    [Test]
    public async Task AnAcceptSaysWhetherTheSnapshotWasWritten()
    {
        static ViewerResponse RoundTrip(ViewerResponse response)
        {
            if (!ViewerResponse.TryParse(response.Build(), out var parsed))
            {
                throw new("Unreadable response.");
            }

            return parsed;
        }

        var written = RoundTrip(ViewerMessageHandler.Handle(new FakeOwner((true, "Applied Tests.cs:42"), written: true), new(ViewerVerb.Accept, "key")));
        await Assert.That(written.Written).IsTrue();

        var stale = RoundTrip(ViewerMessageHandler.Handle(new FakeOwner((true, "Not written")), new(ViewerVerb.Accept, "key")));
        await Assert.That(stale.Ok).IsTrue();
        await Assert.That(stale.Written).IsFalse();

        var discarded = RoundTrip(ViewerMessageHandler.Handle(new FakeOwner((true, null), written: true), new(ViewerVerb.Discard, "key")));
        await Assert.That(discarded.Written).IsNull();

        await Assert.That(RoundTrip(ViewerResponse.Success("Applied Tests.cs:42")).Written).IsNull();
    }

    /// <summary>
    /// An attached viewer asks for the full listing five times a second, and it is every patch
    /// serialized. Sent the tag of the one it holds, an owner whose queue has not changed since
    /// answers that it has not, with nothing else. An owner with no tag to give, and a reader
    /// sending none, get the listing in full as before tags.
    /// </summary>
    [Test]
    public async Task AnUnchangedListingIsNotSentAgain()
    {
        static ViewerResponse RoundTrip(ViewerResponse response)
        {
            if (!ViewerResponse.TryParse(response.Build(), out var parsed))
            {
                throw new("Unreadable response.");
            }

            return parsed;
        }

        var owner = new FakeOwner((true, null))
        {
            Tag = "one"
        };

        var full = RoundTrip(ViewerMessageHandler.Handle(owner, new(ViewerVerb.ListFull)));
        await Assert.That(full.Tag).IsEqualTo("one");
        await Assert.That(full.Unchanged).IsFalse();

        var same = RoundTrip(ViewerMessageHandler.Handle(owner, new(ViewerVerb.ListFull, Body: "one")));
        await Assert.That(same.Unchanged).IsTrue();
        await Assert.That(same.Tag).IsEqualTo("one");

        owner.Tag = "two";
        var changed = RoundTrip(ViewerMessageHandler.Handle(owner, new(ViewerVerb.ListFull, Body: "one")));
        await Assert.That(changed.Unchanged).IsFalse();
        await Assert.That(changed.Tag).IsEqualTo("two");

        owner.Tag = null;
        var untagged = RoundTrip(ViewerMessageHandler.Handle(owner, new(ViewerVerb.ListFull, Body: "two")));
        await Assert.That(untagged.Unchanged).IsFalse();
        await Assert.That(untagged.Tag).IsNull();
    }

    /// <summary>
    /// A pending file with no tray running. The paths ride key and body rather than an encoded
    /// payload, because that is all a tracked move or delete is.
    /// </summary>
    [Test]
    public async Task MoveAndDeleteReachTheOwner()
    {
        var owner = new FakeOwner((true, null));

        var move = ViewerMessageHandler.Handle(owner, new(ViewerVerb.Move, @"c:\temp\a.received.txt", @"c:\code\a.verified.txt"));
        var delete = ViewerMessageHandler.Handle(owner, new(ViewerVerb.Delete, @"c:\code\b.verified.txt"));

        await Assert.That(move.Ok).IsTrue();
        await Assert.That(delete.Ok).IsTrue();
        await Assert.That(owner.Tracked).IsEquivalentTo(
        [
            @"move c:\temp\a.received.txt > c:\code\a.verified.txt",
            @"delete c:\code\b.verified.txt"
        ]);
    }

    /// <summary>
    /// The pair whose diff tool is the viewer itself: tracked exactly as a move, and then raised,
    /// which is the whole difference between the two verbs. The focus names the entry just
    /// tracked, so an owner with a window selects it and one without starts a viewer over it.
    /// </summary>
    [Test]
    public async Task ADiffTracksThePairAndRaisesAWindow()
    {
        var owner = new FakeOwner((true, null));

        var response = ViewerMessageHandler.Handle(owner, new(ViewerVerb.Diff, @"c:\temp\a.received.txt", @"c:\code\a.verified.txt"));

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(owner.Tracked).IsEquivalentTo([@"move c:\temp\a.received.txt > c:\code\a.verified.txt"]);
        await Assert.That(owner.Windowed).IsEquivalentTo([$"{WindowCommand.Focus} {TrackedKeys.ForMove(@"c:\temp\a.received.txt")}"]);
    }

    /// <summary>
    /// And a move stays silent, which is what lets the two coexist: every other tool has already
    /// opened its own window for the pair by the time its move arrives.
    /// </summary>
    [Test]
    public async Task AMoveRaisesNothing()
    {
        var owner = new FakeOwner((true, null));

        ViewerMessageHandler.Handle(owner, new(ViewerVerb.Move, @"c:\temp\a.received.txt", @"c:\code\a.verified.txt"));

        await Assert.That(owner.Windowed).IsEmpty();
    }

    [Test]
    public async Task ADiffWithoutBothPathsIsRefused()
    {
        var owner = new FakeOwner((true, null));

        var noTarget = ViewerMessageHandler.Handle(owner, new(ViewerVerb.Diff, @"c:\temp\a.received.txt"));

        await Assert.That(noTarget.Ok).IsFalse();
        await Assert.That(noTarget.Message).IsEqualTo("Diff requires a key and a body");
        await Assert.That(owner.Tracked).IsEmpty();
        await Assert.That(owner.Windowed).IsEmpty();
    }

    [Test]
    public async Task AMoveWithoutBothPathsIsRefused()
    {
        var owner = new FakeOwner((true, null));

        var noTarget = ViewerMessageHandler.Handle(owner, new(ViewerVerb.Move, @"c:\temp\a.received.txt"));
        var noFile = ViewerMessageHandler.Handle(owner, new(ViewerVerb.Delete));

        await Assert.That(noTarget.Ok).IsFalse();
        await Assert.That(noTarget.Message).IsEqualTo("Move requires a key and a body");
        await Assert.That(noFile.Ok).IsFalse();
        await Assert.That(noFile.Message).IsEqualTo("Delete requires a key");
        await Assert.That(owner.Tracked).IsEmpty();
    }

    /// <summary>
    /// The accept body is the variant origin a reviewer picked, and it has to reach the owner.
    /// </summary>
    [Test]
    public async Task AnAcceptForwardsItsOriginToTheOwner()
    {
        var owner = new FakeOwner((true, null));
        ViewerMessageHandler.Handle(owner, new(ViewerVerb.Accept, "key", "net9.0"));

        await Assert.That(owner.AcceptedOrigin).IsEqualTo("net9.0");
    }

    class FakeOwner((bool ok, string? message) act, bool written = false) :
        IQueueOwner
    {
        public string? AcceptedOrigin { get; private set; }

        public int Enqueue(InlinePatch patch) => 1;

        public void Settle(string key, string? origin, string? member, string? value)
        {
        }

        public List<string> Tracked { get; } = [];

        public void TrackMove(string temp, string target) =>
            Tracked.Add($"move {temp} > {target}");

        public void TrackDelete(string file) =>
            Tracked.Add($"delete {file}");

        public ViewerResponse Listing(bool withPatches) => ViewerResponse.Listing([]);

        public string? Tag { get; set; }

        public string? ListingTag() => Tag;

        public bool Has(string key) => true;

        public (bool ok, string? message, bool written) Accept(string key, string? origin)
        {
            AcceptedOrigin = origin;
            return (act.ok, act.message, written);
        }

        public (bool ok, string? message) Discard(string key) => act;

        public string? AcceptAll() => null;

        public string? DiscardAll() => null;

        public List<string> Windowed { get; } = [];

        public void Window(WindowCommand command, string? key) =>
            Windowed.Add($"{command} {key}");
    }

    /// <summary>
    /// How an owner with no window of its own drives one: answered on a listing, so there is still
    /// one port and no discovery order.
    /// </summary>
    [Test]
    public async Task EveryWindowCommandRidesOnAListing()
    {
        foreach (var name in Enum.GetNames(typeof(WindowCommand)))
        {
            var command = (WindowCommand) Enum.Parse(typeof(WindowCommand), name);
            var text = ViewerResponse.Listing([], command).Build();

            await Assert.That(text).Contains($"window: {name.ToLowerInvariant()}\n");
            await Assert.That(ViewerResponse.TryParse(text, out var parsed)).IsTrue();
            await Assert.That(parsed!.Window).IsEqualTo(command);
        }
    }

    [Test]
    public async Task AListingWithNoWindowCommandSaysNothing()
    {
        var text = ViewerResponse.Listing([]).Build();

        await Assert.That(text).DoesNotContain("window:");
        await Assert.That(ViewerResponse.TryParse(text, out var parsed)).IsTrue();
        await Assert.That(parsed!.Window).IsNull();
    }

    /// <summary>
    /// How a viewer displaying someone else's queue learns how far the owner's accept-all has got:
    /// on a listing taken while it runs, since the accept itself is one exchange that answers only
    /// once the batch is done.
    /// </summary>
    [Test]
    public async Task AcceptProgressRidesOnAListing()
    {
        var text = ViewerResponse.Listing([], progress: new(3, 40)).Build();

        await Assert.That(text).Contains("progress: 3|40\n");
        await Assert.That(ViewerResponse.TryParse(text, out var parsed)).IsTrue();
        await Assert.That(parsed!.Progress).IsEqualTo(new AcceptProgress(3, 40));
    }

    [Test]
    public async Task AListingWithNoAcceptRunningSaysNothingOfProgress()
    {
        var text = ViewerResponse.Listing([]).Build();

        await Assert.That(text).DoesNotContain("progress:");
        await Assert.That(ViewerResponse.TryParse(text, out var parsed)).IsTrue();
        await Assert.That(parsed!.Progress).IsNull();
    }

    /// <summary>
    /// A progress line is only a count, so one that does not parse is a response that does not,
    /// the way a malformed move line is.
    /// </summary>
    [Test]
    public async Task AMalformedProgressLineRejectsTheResponse()
    {
        var text = ViewerResponse.Listing([], progress: new(3, 40)).Build()
            .Replace("progress: 3|40\n", "progress: 3\n");

        await Assert.That(ViewerResponse.TryParse(text, out _)).IsFalse();
    }

    /// <summary>
    /// The entry being worked on rather than the count finished: the first is "1 of 40" while it
    /// is applying, and the last is never "41 of 40".
    /// </summary>
    [Test]
    public async Task ProgressNamesTheEntryInHand()
    {
        await Assert.That(new AcceptProgress(0, 40).Describe()).IsEqualTo("Accepting 1 of 40");
        await Assert.That(new AcceptProgress(39, 40).Describe()).IsEqualTo("Accepting 40 of 40");
        await Assert.That(new AcceptProgress(40, 40).Describe()).IsEqualTo("Accepting 40 of 40");
    }

    /// <summary>
    /// The client's three second default is what a real caller uses to decide the owner has died.
    /// The tests below are about what the owner answers rather than how fast, and CI starts six
    /// test assemblies at once on a two core runner, where an answer arriving on a scheduled task
    /// has twice missed that deadline. <see cref="ASlowExchangeDoesNotBlockTheNext"/> keeps the
    /// default, because being answered inside it while another exchange is held is the point there.
    /// </summary>
    static readonly TimeSpan underLoad = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Bind, serve and exchange for real. The one test here that is not pure string work, because
    /// the async socket calls take a different path on the frameworks without a token overload.
    /// </summary>
    [Test]
    public async Task AnOwnerAnswersAClient()
    {
        await Assert.That(ViewerServer.TryBind(0, out var bound)).IsTrue();
        using var server = bound!;
        using var cancel = new CancelSource();
        var listening = server.Listen(_ => ViewerResponse.Success($"heard {_.Verb}"), cancel.Token);

        var sent = ViewerClient.TrySend(new(ViewerVerb.List), out var response, server.Port, underLoad);

        await Assert.That(sent).IsTrue();
        await Assert.That(response!.Ok).IsTrue();
        await Assert.That(response.Message).IsEqualTo("heard List");

        await cancel.CancelAsync();
        await Wait(listening);
    }

    /// <summary>
    /// The Windows viewer starts listening on its UI thread, which carries a WinForms context by
    /// then, and that thread is the render loop: it pumps between frames, and not at all while an
    /// accept on it waits up to ten seconds on InlineApplier's mutex. The accept loop resumed on
    /// it, so every connection went unanswered for as long as the render thread was busy - a
    /// tray's listing, an attached viewer's poll, the next failing snapshot.
    /// <para>
    /// A context that is never pumped is that thread at its worst, and the owner still answers.
    /// </para>
    /// </summary>
    [Test]
    public async Task AnOwnerAnswersWhileTheThreadThatStartedItIsBusy()
    {
        await Assert.That(ViewerServer.TryBind(0, out var bound)).IsTrue();
        using var server = bound!;
        using var cancel = new CancelSource();
        Task listening;
        var previous = SynchronizationContext.Current;
        // Only around the call, and with nothing awaited inside it: a continuation of this test
        // posted to a context nobody pumps would never run
        SynchronizationContext.SetSynchronizationContext(new UnpumpedContext());
        try
        {
            listening = server.Listen(_ => ViewerResponse.Success($"heard {_.Verb}"), cancel.Token);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var sent = ViewerClient.TrySend(new(ViewerVerb.List), out var response, server.Port, underLoad);

        await Assert.That(sent).IsTrue();
        await Assert.That(response!.Message).IsEqualTo("heard List");

        await cancel.CancelAsync();
        await Wait(listening);
    }

    /// <summary>
    /// The one thread of a context whose owner is too busy to pump it, so whatever is posted to it
    /// waits for good.
    /// </summary>
    sealed class UnpumpedContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
        }

        public override void Send(SendOrPostCallback callback, object? state) =>
            throw new NotSupportedException("A thread that is not pumping cannot be sent to.");
    }

    /// <summary>
    /// Connections are handled concurrently, so one slow exchange does not stop the next from
    /// being answered. Accepting an inline snapshot legitimately takes seconds, and a client
    /// whose listing goes unanswered for that long concludes the owner has died.
    /// </summary>
    [Test]
    public async Task ASlowExchangeDoesNotBlockTheNext()
    {
        // These margins are about the thread pool rather than about the protocol. The handler
        // blocks a pool thread for the whole hold, so answering the second exchange needs the pool
        // to hand out another one - and when it is at its minimum and busy, which is a two core CI
        // runner with tests running in parallel, injection is throttled to roughly one thread every
        // half second. The second exchange used to run on the client's default three seconds, which
        // is not reliably longer than that, and the test failed as though the server were serial.
        //
        // What is asserted does not change. The claim is that the second exchange is answered while
        // the first is still held, so any client timeout shorter than the hold proves it: a server
        // answering one at a time would not reply until the hold ended.
        var hold = TimeSpan.FromSeconds(30);
        var secondExchange = TimeSpan.FromSeconds(10);

        await Assert.That(ViewerServer.TryBind(0, out var bound)).IsTrue();
        using var server = bound!;
        using var cancel = new CancelSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var listening = server.Listen(
            _ =>
            {
                if (_.Verb == ViewerVerb.Accept)
                {
                    entered.Set();
                    release.Wait(hold);
                }

                return ViewerResponse.Success($"heard {_.Verb}");
            },
            cancel.Token);

        try
        {
            // No token on Task.Run: it cancels the scheduling rather than the delegate, so a pool
            // that had not picked this up before the cancel in the finally would leave the task
            // Canceled and the await below throwing. TrySend is blocking and takes no token anyway
            // ReSharper disable once MethodSupportsCancellation
            var accepting = Task.Run(() =>
                ViewerClient.TrySend(new(ViewerVerb.Accept, "key"), out var slow, server.Port, hold)
                    ? slow
                    : null);

            // Asserted rather than assumed. If the accept is not actually being held then nothing
            // below is a test of concurrency, and it would read as one
            await Assert.That(entered.Wait(TimeSpan.FromSeconds(30))).IsTrue();

            var sent = ViewerClient.TrySend(new(ViewerVerb.List), out var response, server.Port, secondExchange);

            await Assert.That(sent).IsTrue();
            await Assert.That(response!.Message).IsEqualTo("heard List");

            release.Set();
            var accepted = await accepting;
            await Assert.That(accepted!.Message).IsEqualTo("heard Accept");
        }
        finally
        {
            release.Set();
            await cancel.CancelAsync();
            await Wait(listening);
        }
    }

    /// <summary>
    /// Connections run on untracked tasks, so a throwing handler is answered as an error rather
    /// than vanishing silently and leaving the client to wait out its timeout.
    /// </summary>
    [Test]
    public async Task AThrowingHandlerAnswersAnError()
    {
        await Assert.That(ViewerServer.TryBind(0, out var bound)).IsTrue();
        using var server = bound!;
        using var cancel = new CancelSource();
        var listening = server.Listen(_ => throw new("the handler is broken"), cancel.Token);

        var sent = ViewerClient.TrySend(new(ViewerVerb.List), out var response, server.Port, underLoad);

        await Assert.That(sent).IsTrue();
        await Assert.That(response!.Ok).IsFalse();
        await Assert.That(response.Message).IsEqualTo("the handler is broken");

        await cancel.CancelAsync();
        await Wait(listening);
    }

    /// <summary>
    /// The whole ownership model: whoever binds owns the queue, and nobody else can.
    /// </summary>
    [Test]
    public async Task ASecondBindIsRefused()
    {
        await Assert.That(ViewerServer.TryBind(0, out var bound)).IsTrue();
        using var first = bound!;

        await Assert.That(ViewerServer.TryBind(first.Port, out var second)).IsFalse();
        await Assert.That(second).IsNull();
    }

    /// <summary>
    /// An owner that accepts the connection and then says nothing. There used to be no bound on
    /// this at all: SendTimeout and ReceiveTimeout apply only to synchronous calls, and the token
    /// the async path was handed is the caller's, which is default from DiffRunner.AddInlineAsync.
    /// A failing test waited for the owner for the rest of its life.
    /// </summary>
    [Test]
    public async Task AnUnresponsiveOwnerTimesOutRatherThanHanging()
    {
        // Stop rather than Dispose: TcpListener is only IDisposable on the modern frameworks, and
        // this test compiles for net48 too
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint) listener.LocalEndpoint).Port;

            // Accepted and then held, which is what a viewer inside the applier mutex looks like.
            // Kept in scope so the connection is not collected and closed under the client
            var accepted = listener.AcceptTcpClientAsync();

            var watch = Stopwatch.StartNew();
            var sent = await ViewerClient.TrySendAsync(
                new(ViewerVerb.List),
                default,
                port,
                TimeSpan.FromSeconds(1));
            watch.Stop();

            await Assert.That(sent).IsFalse();
            await Assert.That(watch.Elapsed).IsLessThan(TimeSpan.FromSeconds(15));

            if (accepted.Status == TaskStatus.RanToCompletion)
            {
                accepted.Result.Close();
            }
        }
        finally
        {
            listener.Stop();
        }
    }
    /// <summary>
    /// Which socket failures mean the listener has stopped, as against one accept having failed.
    /// <para>
    /// Returning on any SocketException gave the queue away for the life of the process: the
    /// socket stays bound so nobody else can take it, and every later client lands in a backlog
    /// nothing is draining. A peer that resets while its connection sits in that backlog is the
    /// ordinary way to hit it - WSAECONNRESET on Windows, ECONNABORTED on BSD and macOS - and is
    /// why Kestrel retries the same condition.
    /// </para>
    /// </summary>
    [Test]
    [Arguments(SocketError.OperationAborted, true)]
    [Arguments(SocketError.Interrupted, true)]
    [Arguments(SocketError.ConnectionReset, false)]
    [Arguments(SocketError.ConnectionAborted, false)]
    [Arguments(SocketError.NetworkDown, false)]
    public async Task SocketFailuresThatStopTheListener(SocketError error, bool expected)
    {
        var exception = new SocketException((int) error);

        await Assert.That(ViewerServer.IsStop(exception, default)).IsEqualTo(expected);
    }

    /// <summary>
    /// And a cancelled token means stop whatever the code says, since that is the ordinary way a
    /// listener is shut down and the token may be observed before the exception is.
    /// </summary>
    [Test]
    public async Task ACancelledTokenStopsTheListenerWhateverTheCode()
    {
        using var cancel = new CancelSource();
        await cancel.CancelAsync();

        await Assert.That(ViewerServer.IsStop(new((int) SocketError.ConnectionReset), cancel.Token)).IsTrue();
    }

    /// <summary>
    /// An owner that answers with an error is not an absent one. Collapsing the two into false
    /// meant a refused inline was read as "nobody is there", so a second viewer was launched, it
    /// could not bind the port, and the snapshot was reported as Queued while being held by
    /// nothing at all.
    /// </summary>
    [Test]
    public async Task ARefusedExchangeIsToldApartFromAnAbsentOwner()
    {
        await Assert.That(ViewerServer.TryBind(0, out var bound)).IsTrue();
        using var server = bound!;
        using var cancel = new CancelSource();
        var listening = server.Listen(_ => ViewerResponse.Error("no thanks"), cancel.Token);

        try
        {
            var refused = await ViewerClient.SendAsync(new(ViewerVerb.List), default, server.Port);

            await Assert.That(refused).IsEqualTo(SendOutcome.Refused);
        }
        finally
        {
            await cancel.CancelAsync();
            await Wait(listening);
        }
    }

    [Test]
    public async Task AnAcceptedExchangeReportsAccepted()
    {
        await Assert.That(ViewerServer.TryBind(0, out var bound)).IsTrue();
        using var server = bound!;
        using var cancel = new CancelSource();
        var listening = server.Listen(_ => ViewerResponse.Success("fine"), cancel.Token);

        try
        {
            await Assert.That(await ViewerClient.SendAsync(new(ViewerVerb.List), default, server.Port))
                .IsEqualTo(SendOutcome.Accepted);
        }
        finally
        {
            await cancel.CancelAsync();
            await Wait(listening);
        }
    }

    [Test]
    // Serialised: the port it releases and probes is recorded as unowned process wide, and
    // the OS may already have handed that number to another test's owner.
    [NotInParallel]
    public async Task AnAbsentOwnerReportsNoOwner()
    {
        ViewerServer.TryBind(0, out var server);
        var port = server!.Port;
        server.Dispose();

        await Assert.That(await ViewerClient.SendAsync(new(ViewerVerb.List), default, port, TimeSpan.FromSeconds(2)))
            .IsEqualTo(SendOutcome.NoOwner);
    }

    /// <summary>
    /// A connect given up on is disposed while still pending, and the task behind it faults
    /// afterwards with nobody left to observe it. The finalizer then reports it: in a test process
    /// that launched a viewer, once per probe the launch gate made while the viewer was still
    /// binding, and fatally in a host that treats unobserved task exceptions as fatal.
    /// <para>
    /// A zero wait gives up on every connect, whatever the platform does with a port nothing is
    /// listening on - Windows lets it hang, others refuse it, both only after the wait has returned.
    /// Serialised with the other tests in this class, since the event is process wide.
    /// </para>
    /// </summary>
    [Test]
    [NotInParallel]
    public async Task AConnectGivenUpOnIsObserved()
    {
        ViewerServer.TryBind(0, out var server);
        var port = server!.Port;
        server.Dispose();
        ViewerClient.ForgetUnowned();

        var unobserved = new List<Exception>();
        void Record(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            lock (unobserved)
            {
                unobserved.Add(args.Exception);
            }
        }

        TaskScheduler.UnobservedTaskException += Record;
        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                ViewerClient.TrySend(new(ViewerVerb.List), out _, port, TimeSpan.Zero);
            }

            // Long enough for the abandoned connects to fault, then collected so their tasks
            // are finalized, which is when an unobserved fault is reported
            for (var pass = 0; pass < 5; pass++)
            {
                await Task.Delay(200);
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Record;
            ViewerClient.ForgetUnowned();
        }

        await Assert.That(unobserved).IsEmpty();
    }

    [Test]
    // Serialised for the same reason as AnAbsentOwnerReportsNoOwner.
    [NotInParallel]
    public async Task AnAbsentOwnerIsNotAnError()
    {
        ViewerServer.TryBind(0, out var server);
        var port = server!.Port;
        server.Dispose();

        await Assert.That(ViewerClient.TrySend(new(ViewerVerb.List), out _, port)).IsFalse();
    }

    /// <summary>
    /// Drains the listener at the end of a test.
    /// <para>
    /// On <see cref="underLoad"/> rather than a margin of its own. Unwinding needs the accept's
    /// continuation to be scheduled, and that waits on the same thread pool everything else here
    /// does - so five seconds was the same bet the client timeout above had already stopped
    /// making, and it lost the same way.
    /// </para>
    /// <para>
    /// Still throws when it runs out, because a listener that never unwinds is a real bug and this
    /// is the only place that would notice.
    /// </para>
    /// </summary>
    static async Task Wait(Task listening)
    {
        try
        {
            await listening.WaitAsync(underLoad);
        }
        catch (OperationCanceledException)
        {
            // Cancellation unwinds through the listener; nothing to report.
        }
    }

    static List<string[]> Fields(string text, string prefix) =>
        text.Split('\n')
            .Where(_ => _.StartsWith(prefix, StringComparison.Ordinal))
            .Select(_ => _.Substring(prefix.Length).Split('|'))
            .ToList();

    static string Decoded(string value)
    {
        ViewerPayload.TryDecode(value, out var decoded);
        return decoded!;
    }
}
