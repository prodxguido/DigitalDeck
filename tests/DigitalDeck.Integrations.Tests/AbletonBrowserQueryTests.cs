using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Integrations.Ableton;
using DigitalDeck.Integrations.Ableton.Osc;
using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// Asking Live a question and getting the right answer back.
/// </summary>
/// <remarks>
/// Everything else in the bridge is fire-and-forget; this is the only part that waits. Waiting on
/// UDP means two failure modes that cannot be reproduced against a real socket on demand — the
/// datagram that never arrives, and the answer that belongs to somebody else's question — so both
/// are driven by hand here.
/// </remarks>
public class AbletonBrowserQueryTests : IDisposable
{
    /// <summary>Short enough that the retry path costs a test milliseconds rather than seconds.</summary>
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromMilliseconds(250);

    private readonly FakeOscTransport _transport = new();
    private readonly AbletonAdapter _adapter;
    private readonly List<string> _failures = new();

    public AbletonBrowserQueryTests()
    {
        _adapter = new AbletonAdapter(
            _transport,
            AbletonAdapter.ManualHeartbeat,
            () => DateTimeOffset.UtcNow,
            QueryTimeout);

        _adapter.ScriptFailed += _failures.Add;
        _adapter.Start();
        _transport.ReceivePong();
    }

    public void Dispose() => _adapter.Dispose();

    // ---------- Ordinary answers ----------

    [Fact]
    public async Task AListingComesBackAsItsRows()
    {
        _transport.Answer = _ => Listing("audio_effects", "Space", truncated: 0,
            "Chorus Echo.adg\tquery:AudioFx#FileId_5472\t1\t0",
            "Sub-carpeta\t\t0\t1");

        var listing = await _adapter.ListAsync("audio_effects", "Space");

        Assert.True(listing.Success);
        Assert.True(listing.Complete);
        Assert.Equal(2, listing.Entries.Count);

        Assert.Equal("Chorus Echo.adg", listing.Entries[0].Name);
        Assert.Equal("query:AudioFx#FileId_5472", listing.Entries[0].Uri);
        Assert.True(listing.Entries[0].IsLoadable);
        Assert.False(listing.Entries[0].IsFolder);

        Assert.True(listing.Entries[1].IsFolder);
        Assert.False(listing.Entries[1].IsLoadable);
    }

    [Fact]
    public async Task TheQuestionGoesOutWithTheCategoryAndPath()
    {
        _transport.Answer = _ => Listing("packs", "Drums/Kits", truncated: 0);

        await _adapter.ListAsync("packs", "Drums/Kits");

        var request = _transport.Sent.Last();

        Assert.Equal(AbletonCommandMap.ListAddress, request.Address);
        Assert.Equal("packs", request.GetString(0));
        Assert.Equal("Drums/Kits", request.GetString(1));
    }

    [Fact]
    public async Task ATruncatedListingSaysSoRatherThanLookingComplete()
    {
        // Otherwise the picker would quietly claim a folder has 500 items when it has 900, and the
        // one the user is looking for is in the part that was cut.
        _transport.Answer = _ => Listing("user_library", string.Empty, truncated: 1, "Uno\turi\t1\t0");

        var listing = await _adapter.ListAsync("user_library", string.Empty);

        Assert.True(listing.Success);
        Assert.False(listing.Complete);
    }

    [Fact]
    public async Task ASearchThatRanOutOfRoomIsNotReportedAsComplete()
    {
        // The search reply carries the flag the other way round -- it reports completeness, not
        // truncation -- which is exactly the sort of thing worth pinning down in a test.
        _transport.Answer = _ => Search("plugins", "rev", complete: 0, "Reverb\turi\t1\t0");

        var listing = await _adapter.SearchAsync("plugins", "rev");

        Assert.True(listing.Success);
        Assert.False(listing.Complete);
        Assert.Single(listing.Entries);
    }

    [Fact]
    public async Task ASearchIsCorrelatedByTheLowerCasedQueryTheScriptEchoes()
    {
        // The script lower-cases and trims before it echoes. Building the key from the raw text
        // would mean no reply ever matched the question that asked for it.
        _transport.Answer = _ => Search("instruments", "wurli", complete: 1, "Wurli.adg\turi\t1\t0");

        var listing = await _adapter.SearchAsync("instruments", "  Wurli  ");

        Assert.True(listing.Success);
        Assert.Single(listing.Entries);
    }

    [Fact]
    public async Task CategoriesComeBackAsLiveActuallyHasThem()
    {
        _transport.Answer = _ => new OscMessage(
            AbletonCommandMap.CategoriesAddress,
            "instruments\naudio_effects\nuser_library");

        var categories = await _adapter.CategoriesAsync();

        Assert.Equal(new[] { "instruments", "audio_effects", "user_library" }, categories);
    }

    [Fact]
    public async Task ARowSaysWhetherWalkingIntoItLeadsAnywhere()
    {
        // Live calls neither of these a folder. One of them holds six folders of user racks.
        _transport.Answer = _ => Listing("audio_effects", string.Empty, 0,
            "Audio Effect Rack\turi-rack\t1\t0\t1",
            "Compressor\turi-comp\t1\t0\t0");

        var listing = await _adapter.ListAsync("audio_effects", string.Empty);

        Assert.False(listing.Entries[0].IsFolder);
        Assert.True(listing.Entries[0].HasChildren);
        Assert.False(listing.Entries[1].HasChildren);
    }

    [Fact]
    public async Task AScriptTooOldToAnswerThatStillBehavesTheWayItUsedTo()
    {
        // 2.1.0 sends four fields. Falling back to is_folder leaves it working as it did rather
        // than turning it into a picker that refuses to open anything at all.
        _transport.Answer = _ => Listing("packs", string.Empty, 0, "Una carpeta\turi\t0\t1");

        var listing = await _adapter.ListAsync("packs", string.Empty);

        Assert.True(listing.Entries[0].HasChildren);
    }

    // ---------- Lost datagrams ----------

    [Fact]
    public async Task ALostQuestionIsAskedAgain()
    {
        var asked = 0;

        _transport.Answer = _ =>
        {
            asked++;
            return asked == 1 ? null : Listing("drums", string.Empty, 0, "Kit.adg\turi\t1\t0");
        };

        var listing = await _adapter.ListAsync("drums", string.Empty);

        Assert.Equal(2, asked);
        Assert.True(listing.Success);
        Assert.Single(listing.Entries);
    }

    [Fact]
    public async Task SilenceEndsAsAnAnswerTheUserCanRead()
    {
        _transport.Answer = _ => null;

        var listing = await _adapter.ListAsync("drums", string.Empty);

        Assert.False(listing.Success);
        Assert.Contains("no contestó", listing.Error);

        // Twice, and then it stops: an unanswered question must not hold the editor open forever.
        Assert.Equal(2, _transport.Sent.Count(message => message.Address == AbletonCommandMap.ListAddress));
    }

    [Fact]
    public async Task AQuestionAskedWhileLiveIsGoneIsRefusedWithoutBeingSent()
    {
        using var adapter = new AbletonAdapter(
            _transport, AbletonAdapter.ManualHeartbeat, () => DateTimeOffset.UtcNow, QueryTimeout);

        var before = _transport.Sent.Count;
        var listing = await adapter.ListAsync("drums", string.Empty);

        Assert.False(listing.Success);
        Assert.Equal(before, _transport.Sent.Count);
    }

    // ---------- Answers that belong to somebody else ----------

    [Fact]
    public async Task TwoQuestionsInFlightEachGetTheirOwnAnswer()
    {
        // Nothing stops the editor from having a listing and a search outstanding at once, and a
        // reply carries no request id -- only the echo of what was asked.
        _transport.Answer = message => message.Address switch
        {
            AbletonCommandMap.ListAddress => Listing("packs", "A", 0, "de la carpeta\turi-a\t1\t0"),
            AbletonCommandMap.SearchAddress => Search("packs", "kick", 1, "de la búsqueda\turi-b\t1\t0"),
            _ => null
        };

        var listing = _adapter.ListAsync("packs", "A");
        var search = _adapter.SearchAsync("packs", "kick");

        Assert.Equal("de la carpeta", (await listing).Entries.Single().Name);
        Assert.Equal("de la búsqueda", (await search).Entries.Single().Name);
    }

    [Fact]
    public async Task AnAnswerToADifferentFolderIsNotTakenAsThisOne()
    {
        // Two listings at once: an answer echoing the other path must not satisfy this question.
        _transport.Answer = _ => null;

        var wanted = _adapter.ListAsync("packs", "Correcta");
        var other = _adapter.ListAsync("packs", "Otra");

        _transport.Receive(Listing("packs", "Otra", 0, "de la otra\turi\t1\t0"));
        _transport.Receive(Listing("packs", "Correcta", 0, "de la correcta\turi\t1\t0"));

        Assert.Equal("de la correcta", (await wanted).Entries.Single().Name);
        Assert.Equal("de la otra", (await other).Entries.Single().Name);
    }

    // ---------- Refusals ----------

    [Fact]
    public async Task ARefusalEndsTheQuestionInsteadOfMakingItWaitOutTheClock()
    {
        _transport.Answer = _ => new OscMessage(
            AbletonCommandMap.ErrorAddress,
            AbletonCommandMap.ListAddress,
            "ArgumentError: there is nothing called 'Space' at that level");

        var listing = await _adapter.ListAsync("audio_effects", "Space");

        Assert.False(listing.Success);
        Assert.Contains("nothing called", listing.Error);

        // One attempt, because the script already said what it thinks; asking twice would only
        // get the same refusal.
        Assert.Single(_transport.Sent, message => message.Address == AbletonCommandMap.ListAddress);
    }

    [Fact]
    public async Task ARefusedQuestionDoesNotAlsoLookLikeAFailedButton()
    {
        // A folder that turned out to be empty is the picker's business, not the status bar's.
        _transport.Answer = _ => new OscMessage(
            AbletonCommandMap.ErrorAddress, AbletonCommandMap.ListAddress, "vacío");

        await _adapter.ListAsync("audio_effects", "Space");

        Assert.Empty(_failures);
    }

    [Fact]
    public void ARefusalWithNoQuestionWaitingStillReachesTheStatusBar()
    {
        _transport.Receive(new OscMessage(
            AbletonCommandMap.ErrorAddress, AbletonCommandMap.LoadAddress, "no está indexado"));

        Assert.Single(_failures);
        Assert.Contains("no está indexado", _failures[0]);
    }

    // ---------- Helpers ----------

    private static OscMessage Listing(string category, string path, int truncated, params string[] lines) =>
        new(AbletonCommandMap.ListAddress, category, path, truncated, string.Join("\n", lines));

    private static OscMessage Search(string category, string query, int complete, params string[] lines) =>
        new(AbletonCommandMap.SearchAddress, category, query, complete, string.Join("\n", lines));
}
