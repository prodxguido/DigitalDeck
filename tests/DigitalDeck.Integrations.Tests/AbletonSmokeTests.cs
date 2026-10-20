using System.Diagnostics;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Integrations.Ableton;
using DigitalDeck.Integrations.Ableton.Osc;
using Xunit;
using Xunit.Abstractions;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// The half of the bridge no unit test can reach: whether Live actually does what the script asks.
/// </summary>
/// <remarks>
/// Everything else in this project runs against a fake and proves the code is self-consistent.
/// Only this can tell us whether <c>song.capture_midi</c> still exists in the version of Live on
/// this machine — which is the whole risk the blueprint flags, since none of it is contractual.
/// <para>
/// Run it against a scratch Live Set, not a project you care about: it moves the tempo, toggles
/// mute and solo, and switches views. It puts everything back, but a crash halfway through would
/// not.
/// </para>
/// </remarks>
public sealed class AbletonSmokeTests : IDisposable
{
    /// <summary>How long Live gets to complain before a command is called good.</summary>
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);

    private readonly ITestOutputHelper _output;
    private readonly UdpOscTransport _transport = new();
    private readonly AbletonAdapter _adapter;
    private readonly List<OscMessage> _received = new();
    private readonly List<string> _failures = new();
    private readonly object _gate = new();

    public AbletonSmokeTests(ITestOutputHelper output)
    {
        _output = output;

        _transport.MessageReceived += message =>
        {
            lock (_gate) _received.Add(message);
        };

        _adapter = new AbletonAdapter(_transport, AbletonAdapter.ManualHeartbeat);
        _adapter.ScriptFailed += failure =>
        {
            lock (_gate) _failures.Add(failure);
        };
    }

    public void Dispose() => _adapter.Dispose();

    // ---------- The run ----------

    [LiveFact]
    public async Task TheScriptAnswersAndSaysWhichLiveItIsIn()
    {
        await ConnectAsync();

        var status = _adapter.Status;

        _output.WriteLine($"Remote Script : {status.ScriptVersion}");
        _output.WriteLine($"Ableton Live  : {status.LiveVersion}");
        _output.WriteLine($"Puertos       : {status.Ports?.ToString() ?? "no los informó"}");

        Assert.Equal(AbletonConnectionState.Connected, status.State);
        Assert.False(string.IsNullOrWhiteSpace(status.LiveVersion));

        // Confirmation rather than diagnosis: a script on the wrong pair never answers at all, so
        // getting this far already proves the two halves agree. Printing it makes the run say on
        // which pair, which is the question somebody running AbletonOSC too will be asking.
        Assert.Equal(
            new AbletonPorts(UdpOscTransport.DefaultSendPort, UdpOscTransport.DefaultReceivePort),
            status.Ports);
    }

    [LiveFact]
    public async Task EveryReadOnlyQueryComesBackWithAnAnswer()
    {
        await ConnectAsync();

        foreach (var address in new[]
                 {
                     "/live/song/get/tempo",
                     "/live/song/get/is_playing",
                     "/live/song/get/metronome",
                     "/live/view/get/is_session"
                 })
        {
            var reply = await AskAsync(address);

            _output.WriteLine($"{address,-32} -> {reply}");
            Assert.NotNull(reply);
        }
    }

    [LiveFact]
    public async Task EverySafeCommandIsAcceptedByLive()
    {
        await ConnectAsync();

        var tempo = await ReadTempoAsync();
        _output.WriteLine($"Tempo original: {tempo} BPM");
        _output.WriteLine("");

        var results = new List<(AbletonCommand Command, string Outcome)>();

        try
        {
            foreach (var command in SafeCommands)
                foreach (var action in Expand(command))
                    results.Add((command, await RunAsync(action)));
        }
        finally
        {
            await RestoreAsync(tempo);
        }

        Report(results);
        Assert.All(results, entry => Assert.Equal("ok", entry.Outcome));
    }

    [LiveFact(destructive: true)]
    public async Task CreatingTracksIsAcceptedByLive()
    {
        // Split from the removing half on purpose: adding a track to a set is additive and undoes
        // with one Ctrl+Z, while deleting one is the operation nobody wants to discover by accident
        // on the wrong set. Someone validating a new Live version can run this and stop here.
        await ConnectAsync();

        var results = new List<(AbletonCommand Command, string Outcome)>();

        foreach (var command in new[]
                 {
                     AbletonCommand.CreateMidiTrack,
                     AbletonCommand.CreateAudioTrack,
                     AbletonCommand.CreateReturnTrack
                 })
        {
            results.Add((command, await RunAsync(Build(command))));
        }

        Report(results);
        Assert.All(results, entry => Assert.Equal("ok", entry.Outcome));
    }

    [LiveFact(destructive: true)]
    public async Task DuplicatingAndRemovingTracksIsAcceptedByLive()
    {
        await ConnectAsync();

        var results = new List<(AbletonCommand Command, string Outcome)>();

        // Ordered so the set ends roughly where it started: create one, duplicate it, delete both.
        // The selection moves as Live inserts, which is why DeleteTrack runs against the selection
        // rather than an index remembered from before.
        foreach (var command in new[]
                 {
                     AbletonCommand.CreateMidiTrack,
                     AbletonCommand.DuplicateTrack,
                     AbletonCommand.DeleteTrack,
                     AbletonCommand.DeleteTrack
                 })
        {
            results.Add((command, await RunAsync(Build(command))));
        }

        Report(results);
        Assert.All(results, entry => Assert.Equal("ok", entry.Outcome));
    }

    // ---------- Browser ----------
    //
    // The part AbletonOSC does not have, and the one that decides whether phase 3 is possible at
    // all. Read-only here: listing and searching cannot change the set, so none of it is gated.

    [LiveFact]
    public async Task TheBrowserSaysWhichCategoriesThisLiveHas()
    {
        await ConnectAsync();

        var reply = await QueryAsync("/digitaldeck/browser/categories");
        Assert.NotNull(reply);

        var categories = (reply!.GetString(0) ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        _output.WriteLine("Categorías: " + string.Join(", ", categories));
        Assert.Contains("audio_effects", categories);
    }

    [LiveFact]
    public async Task TheBrowserListsWhatLiveHasIndexed()
    {
        await ConnectAsync();

        var reply = await QueryAsync("/digitaldeck/browser/list", "audio_effects", "");
        Assert.NotNull(reply);

        var entries = ParseEntries(reply!.GetString(3));

        _output.WriteLine($"Efectos de audio en el nivel superior: {entries.Count}");
        foreach (var entry in entries.Take(20)) _output.WriteLine("  " + entry);

        Assert.NotEmpty(entries);
    }

    [LiveFact]
    public async Task EveryAudioEffectRackTheUserHasCanBeListed()
    {
        // The question this answers for the product: can the app offer a list of the user's own
        // racks? Only if Live indexed them — a rack sitting in a folder Live has not scanned is
        // unreachable, however visible it is in Explorer.
        await ConnectAsync();

        var reply = await QueryAsync("/digitaldeck/browser/list", "audio_effects", "Audio Effect Rack");

        if (reply is null || reply.GetString(3) is not { Length: > 0 } payload)
        {
            _output.WriteLine("No hay carpeta «Audio Effect Rack» en el browser de este Live.");
            return;
        }

        var racks = ParseEntries(payload);

        _output.WriteLine($"Audio Effect Racks indexados: {racks.Count}");
        foreach (var rack in racks.Take(30)) _output.WriteLine("  " + rack);
    }

    [LiveFact]
    public async Task SearchingTheBrowserFindsSomethingLoadable()
    {
        await ConnectAsync();

        var reply = await QueryAsync("/digitaldeck/browser/search", "audio_effects", "reverb", 20);
        Assert.NotNull(reply);

        var complete = reply!.GetInt(2);
        var results = ParseEntries(reply.GetString(3));

        _output.WriteLine($"«reverb»: {results.Count} resultados (búsqueda completa: {complete == 1})");
        foreach (var result in results.Take(20)) _output.WriteLine("  " + result);

        Assert.NotEmpty(results);
    }

    [LiveFact]
    public async Task AskingForSomethingLiveDoesNotHaveIsRefusedClearly()
    {
        // The failure mode that matters: a name the user typed that Live never indexed has to come
        // back as an explanation, not as a button that quietly does nothing.
        await ConnectAsync();

        int before;
        lock (_gate) before = _failures.Count;

        _transport.Send(new OscMessage(
            "/digitaldeck/browser/load", "", "audio_effects", "Un Plugin Que No Existe"));

        await Task.Delay(SettleTime * 4);

        lock (_gate)
        {
            Assert.True(_failures.Count > before, "Live aceptó cargar algo que no existe.");
            _output.WriteLine(_failures[^1]);
        }
    }

    [LiveFact(destructive: true)]
    public async Task ARackFoundInTheBrowserActuallyLoadsIntoTheSelectedTrack()
    {
        // The end of the whole chain: list what Live has, take a real URI from that listing, and
        // load it. Gated because it leaves a device on the selected track — Ctrl+Z in Live undoes it.
        await ConnectAsync();

        var listing = await QueryAsync(
            "/digitaldeck/browser/list", "audio_effects", "Audio Effect Rack/Space");

        Assert.NotNull(listing);

        var first = (listing!.GetString(3) ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t'))
            .FirstOrDefault(fields => fields.Length >= 3 && fields[2] == "1");

        Assert.NotNull(first);
        _output.WriteLine($"Cargando «{first![0]}»  ({first[1]})");

        int failuresBefore;
        int receivedBefore;

        lock (_gate)
        {
            failuresBefore = _failures.Count;
            receivedBefore = _received.Count;
        }

        var result = await _adapter.LoadDeviceAsync(new AbletonLoadDeviceAction
        {
            Name = first[0],
            Uri = first[1],
            Category = AbletonBrowserCategories.AudioEffects
        });

        Assert.True(result.Success, result.Error);
        await Task.Delay(SettleTime * 4);

        lock (_gate)
        {
            Assert.True(_failures.Count == failuresBefore,
                        _failures.Count > failuresBefore ? _failures[^1] : "Live rechazó la carga.");

            // The script announces a successful load on its own; there is nothing to ask for.
            // Querying that address would be asking the script to handle a reply-only address.
            var confirmation = _received
                .Skip(receivedBefore)
                .FirstOrDefault(message => message.Address == "/digitaldeck/browser/loaded");

            Assert.True(confirmation is not null, "Live no confirmó la carga.");
            _output.WriteLine($"Live confirmó: {confirmation}");
        }
    }

    // ---------- The picker's own path ----------
    //
    // The tests above ask the script directly with a helper of their own. These go through
    // IAbletonClient, which is what the editor uses: the correlation, the retry and the parsing all
    // run for real, against a real Live, over a real socket that really can drop a datagram.

    [LiveFact]
    public async Task WhatLiveCallsAFolderIsWorthWritingDown()
    {
        // is_folder is not contractual and turns out not to mean "has children". Printing the raw
        // flags is how the picker's navigation rule gets checked against a real library instead of
        // against an assumption.
        await ConnectAsync();

        var top = await _adapter.ListAsync(AbletonBrowserCategories.AudioEffects, string.Empty);

        Assert.True(top.Success, top.Error);

        _output.WriteLine($"{"nombre",-32} cargable  carpeta   tiene hijos");

        foreach (var entry in top.Entries.Take(20))
            _output.WriteLine($"{entry.Name,-32} {entry.IsLoadable,-9} {entry.IsFolder,-9} {entry.HasChildren}");

        _output.WriteLine(
            $"marcados como carpeta: {top.Entries.Count(entry => entry.IsFolder)}, " +
            $"con hijos: {top.Entries.Count(entry => entry.HasChildren)}, " +
            $"de {top.Entries.Count}");

        // And what happens when one of them is walked into anyway.
        var candidate = top.Entries.FirstOrDefault(entry => entry.Name.Contains("Rack"))
                        ?? top.Entries[0];

        var inside = await _adapter.ListAsync(AbletonBrowserCategories.AudioEffects, candidate.Name);

        _output.WriteLine(inside.Success
            ? $"«{candidate.Name}» (carpeta={candidate.IsFolder}) tiene {inside.Entries.Count} hijos"
            : $"«{candidate.Name}» no se pudo abrir: {inside.Error}");
    }

    [LiveFact]
    public async Task ThePickerCanWalkFromACategoryIntoAFolder()
    {
        await ConnectAsync();

        var top = await _adapter.ListAsync(AbletonBrowserCategories.AudioEffects, string.Empty);

        Assert.True(top.Success, top.Error);
        Assert.NotEmpty(top.Entries);

        _output.WriteLine($"Nivel superior: {top.Entries.Count} ítems (completo: {top.Complete})");

        var folder = top.Entries.FirstOrDefault(entry => entry.HasChildren);

        Assert.True(folder is not null,
                    "Ningún ítem del nivel superior dice tener hijos. ¿Live está corriendo un script anterior a 2.2.0?");

        var inside = await _adapter.ListAsync(AbletonBrowserCategories.AudioEffects, folder!.Name);

        Assert.True(inside.Success, inside.Error);
        Assert.NotEmpty(inside.Entries);

        _output.WriteLine($"Dentro de «{folder.Name}»: {inside.Entries.Count} ítems");

        foreach (var entry in inside.Entries.Take(10))
            _output.WriteLine($"  {entry.Name}  [{entry.Uri}]");
    }

    [LiveFact]
    public async Task EveryItemThePickerOffersComesWithAUriOfItsOwn()
    {
        // The reason the picker stores a URI at all: two presets with the same name are ordinary,
        // and the name alone would load whichever the script happened to reach first.
        await ConnectAsync();

        var found = await _adapter.SearchAsync(AbletonBrowserCategories.AudioEffects, "reverb", 40);

        Assert.True(found.Success, found.Error);
        Assert.NotEmpty(found.Entries);

        var loadable = found.Entries.Where(entry => entry.IsLoadable).ToList();
        var uris = loadable.Select(entry => entry.Uri).ToList();

        _output.WriteLine($"«reverb»: {loadable.Count} cargables, {uris.Distinct().Count()} URIs distintos");

        Assert.All(loadable, entry => Assert.NotEqual(string.Empty, entry.Uri));
        Assert.Equal(uris.Count, uris.Distinct().Count());
    }

    [LiveFact]
    public async Task TwoQuestionsAtOnceDoNotGetEachOthersAnswers()
    {
        // The one thing a fake transport cannot really prove, because it decides the order itself.
        await ConnectAsync();

        var instruments = _adapter.ListAsync("instruments", string.Empty);
        var effects = _adapter.ListAsync(AbletonBrowserCategories.AudioEffects, string.Empty);
        var search = _adapter.SearchAsync(AbletonBrowserCategories.AudioEffects, "delay", 20);

        await Task.WhenAll(instruments, effects, search);

        Assert.Equal("instruments", (await instruments).Category);
        Assert.Equal(AbletonBrowserCategories.AudioEffects, (await effects).Category);
        Assert.Equal("delay", (await search).Path);

        _output.WriteLine(
            $"instrumentos: {(await instruments).Entries.Count}, " +
            $"efectos: {(await effects).Entries.Count}, " +
            $"«delay»: {(await search).Entries.Count}");
    }

    [LiveFact]
    public async Task AFolderThatIsNotThereComesBackAsAnExplanation()
    {
        // The picker shows this text to the user, so it has to be an explanation and not a stall.
        await ConnectAsync();

        var listing = await _adapter.ListAsync(
            AbletonBrowserCategories.AudioEffects, "Una Carpeta Que No Existe");

        Assert.False(listing.Success);
        Assert.False(string.IsNullOrWhiteSpace(listing.Error));

        _output.WriteLine(listing.Error!);
    }

    [LiveFact]
    public async Task TheUserLibraryAnswersEvenWhenItIsEmpty()
    {
        // On an account running Live in demo mode this is empty, and empty is a valid answer. What
        // it must not be is an error, because the picker would then blame the bridge for it.
        await ConnectAsync();

        var listing = await _adapter.ListAsync("user_library", string.Empty);

        _output.WriteLine(listing.Success
            ? $"User Library: {listing.Entries.Count} ítems (completo: {listing.Complete})"
            : $"User Library: {listing.Error}");

        Assert.True(listing.Success, listing.Error);
    }

    [LiveFact]
    public async Task LiveSaysWhichCategoriesItActuallyHas()
    {
        await ConnectAsync();

        var categories = await _adapter.CategoriesAsync();

        _output.WriteLine("Categorías: " + string.Join(", ", categories));

        Assert.NotEmpty(categories);

        // Anything Live reports has to be something the editor knows how to label, or the chip row
        // would end up showing a raw identifier.
        Assert.All(categories, category =>
            Assert.True(AbletonBrowserCategories.IsKnown(category), $"«{category}» no está en el catálogo."));
    }

    /// <summary>Splits one listing reply into its lines. Fields are name, URI, loadable, folder.</summary>
    private static List<string> ParseEntries(string? payload) =>
        (payload ?? string.Empty)
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Split('\t'))
        .Select(fields => fields.Length >= 3
            ? $"{fields[0]}{(fields[2] == "1" ? "" : "  (carpeta)")}"
            : fields[0])
        .ToList();

    // ---------- The mixer ----------
    //
    // This is the only place that can answer whether Live really has return_tracks, group_track and
    // str_for_value on a mixer parameter. The tests with the fake LOM prove the roster is built and
    // parsed correctly from something shaped like Live; they cannot prove Live is that shape.

    [LiveFact]
    public async Task LiveDescribesEveryChannelOfTheOpenSet()
    {
        await ConnectAsync();

        var tracks = await MixerRosterAsync();

        foreach (var track in tracks)
            _output.WriteLine(
                $"{track.Index,4}  {track.Kind,-7}  prof {track.Depth}  " +
                $"{track.Volume:0.000}  {track.Display,-10}  {track.Name}");

        Assert.NotEmpty(tracks);

        // Every set has exactly one master, and it has to come last or the window draws it inline.
        var master = Assert.Single(tracks, track => track.Kind == LiveTrackKind.Master);
        Assert.Same(master, tracks[^1]);

        // The ordinary tracks are numbered from zero upwards, the returns downwards from -3, and
        // nothing may collide: an index that means two channels moves the wrong fader.
        Assert.Equal(tracks.Count, tracks.Select(track => track.Index).Distinct().Count());
        Assert.All(tracks.Where(t => t.Kind == LiveTrackKind.Return), t => Assert.True(t.Index <= -3));
        Assert.All(tracks.Where(t => t.Kind is LiveTrackKind.Normal or LiveTrackKind.Group),
            t => Assert.True(t.Index >= 0));
    }

    [LiveFact]
    public async Task LiveSuppliesTheReadingUnderEachFader()
    {
        // The app deliberately does not convert 0..1 into decibels itself, so if this comes back
        // empty the mixer loses every label it has. Worth failing loudly rather than shipping.
        await ConnectAsync();

        var tracks = await MixerRosterAsync();

        Assert.All(tracks, track =>
            Assert.False(string.IsNullOrWhiteSpace(track.Display),
                $"«{track.Name}» no trajo lectura: str_for_value se movió en esta versión de Live."));
    }

    [LiveFact]
    public async Task AReturnTrackCanBeReadAndMovedLikeAnyOtherChannel()
    {
        await ConnectAsync();

        var tracks = await MixerRosterAsync();
        var returns = tracks.Where(track => track.Kind == LiveTrackKind.Return).ToList();

        if (returns.Count == 0)
        {
            _output.WriteLine("El set abierto no tiene retornos; no hay nada que probar acá.");
            return;
        }

        var target = returns[0];
        _output.WriteLine($"Moviendo «{target.Name}» (índice {target.Index}) y devolviéndolo.");

        var moved = new TaskCompletionSource<LiveVolume>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Watch(LiveVolume volume)
        {
            if (volume.TrackIndex == target.Index) moved.TrySetResult(volume);
        }

        IAbletonMixer mixer = _adapter;
        mixer.VolumeChanged += Watch;

        try
        {
            // Somewhere it certainly was not, so a stale echo cannot be mistaken for the answer.
            var wanted = target.Volume > 0.5 ? 0.25 : 0.75;
            mixer.SetVolume(target.Index, wanted);

            var echo = await Task.WhenAny(moved.Task, Task.Delay(TimeSpan.FromSeconds(2)));

            Assert.True(echo == moved.Task,
                "Live no avisó que el retorno se movió: el listener de volumen no llegó.");
            Assert.Equal(wanted, moved.Task.Result.Value, 2);
        }
        finally
        {
            mixer.SetVolume(target.Index, target.Volume);
            await Task.Delay(SettleTime);
            mixer.VolumeChanged -= Watch;
            mixer.Close();
        }

        Assert.Empty(_failures);
    }

    /// <summary>Subscribes and waits for the roster Live answers with.</summary>
    private async Task<IReadOnlyList<LiveTrack>> MixerRosterAsync()
    {
        var arrived = new TaskCompletionSource<IReadOnlyList<LiveTrack>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        IAbletonMixer mixer = _adapter;

        void Watch(IReadOnlyList<LiveTrack> tracks) => arrived.TrySetResult(tracks);

        mixer.TracksChanged += Watch;

        try
        {
            mixer.Open();

            var finished = await Task.WhenAny(arrived.Task, Task.Delay(TimeSpan.FromSeconds(3)));

            Assert.True(finished == arrived.Task, "Live no contestó con los canales del set.");

            return arrived.Task.Result;
        }
        finally
        {
            mixer.TracksChanged -= Watch;
            mixer.Close();
        }
    }

    // ---------- What is and is not covered ----------

    /// <summary>
    /// Everything that can be run against an open set and undone, or that has no lasting effect.
    /// </summary>
    private static readonly AbletonCommand[] SafeCommands =
    {
        // First, so the run does not depend on what the user last clicked in Live. Everything
        // below acts on the selection, and with the Main track selected Live correctly refuses to
        // mute, solo or arm it — which failed this run until it was pinned to an ordinary track.
        AbletonCommand.SelectTrack,

        // Transport. Play then Stop leaves the transport where it started.
        AbletonCommand.Play,
        AbletonCommand.Stop,
        AbletonCommand.Continue,
        AbletonCommand.Stop,
        AbletonCommand.StopAllClips,

        // Toggles, run twice so the set ends up as it began.
        AbletonCommand.ToggleRecord,
        AbletonCommand.ToggleRecord,
        AbletonCommand.ToggleSessionRecord,
        AbletonCommand.ToggleSessionRecord,
        AbletonCommand.ToggleMetronome,
        AbletonCommand.ToggleMetronome,
        AbletonCommand.ToggleLoop,
        AbletonCommand.ToggleLoop,
        AbletonCommand.ToggleMute,
        AbletonCommand.ToggleMute,
        AbletonCommand.ToggleSolo,
        AbletonCommand.ToggleSolo,
        AbletonCommand.ToggleArm,
        AbletonCommand.ToggleArm,

        // The setting forms of the same things. Expand runs each one on and then off, so these
        // leave the set no different from how the toggles above left it.
        AbletonCommand.SetRecord,
        AbletonCommand.SetSessionRecord,
        AbletonCommand.SetMetronome,
        AbletonCommand.SetLoop,
        AbletonCommand.SetMute,
        AbletonCommand.SetSolo,
        AbletonCommand.SetArm,

        // Tempo. The original is restored afterwards.
        AbletonCommand.SetTempo,
        AbletonCommand.NudgeTempo,
        AbletonCommand.TapTempo,

        // Mixer. Values are set to what a normal set already has.
        AbletonCommand.SetVolume,
        AbletonCommand.NudgeVolume,
        AbletonCommand.NudgePanning,
        AbletonCommand.SetPanning,
        AbletonCommand.StopTrackClips,

        // Clips. Firing an empty slot does nothing; firing a full one starts a clip that
        // StopAllClips then stops.
        AbletonCommand.FireClip,
        AbletonCommand.StopClip,
        AbletonCommand.FireScene,
        AbletonCommand.SelectScene,
        AbletonCommand.FireSelectedScene,
        AbletonCommand.StopAllClips,

        // Views. Session is restored at the end.
        AbletonCommand.ShowArrangement,
        AbletonCommand.ShowSession,
        AbletonCommand.ToggleSessionArrangement,
        AbletonCommand.ShowSession,
        AbletonCommand.ShowDetailClip,
        AbletonCommand.ShowDetailDevice,
        AbletonCommand.ToggleDetail,
        AbletonCommand.ToggleDetail,
        AbletonCommand.ToggleBrowser,
        AbletonCommand.ToggleBrowser,

        // Zoom, in and straight back out by the same number of notches. This is the pair worth
        // watching with your own eyes: Live's API never says which direction means "closer", so
        // whether the mapping in the script is the right way round is a question only the screen
        // can answer.
        AbletonCommand.ZoomTimeIn,
        AbletonCommand.ZoomTimeOut,
        AbletonCommand.ZoomTracksIn,
        AbletonCommand.ZoomTracksOut
    };

    /// <summary>Commands left out of the smoke run, each with the reason it cannot be automated.</summary>
    private static readonly Dictionary<AbletonCommand, string> NotSmokeTested = new()
    {
        [AbletonCommand.TogglePlay] = "Play and Stop already cover the same call.",
        [AbletonCommand.Undo] = "Would undo whatever the user did last, not what the test did.",
        [AbletonCommand.Redo] = "Same as Undo.",
        [AbletonCommand.CaptureMidi] = "Only does anything after material has actually been played in.",
        [AbletonCommand.CreateMidiTrack] = "Adds a track; covered by the gated creating run.",
        [AbletonCommand.CreateAudioTrack] = "Same.",
        [AbletonCommand.CreateReturnTrack] = "Same.",
        [AbletonCommand.DuplicateTrack] = "Removes tracks afterwards; covered by the gated removing run.",
        [AbletonCommand.DeleteTrack] = "Same.",
        [AbletonCommand.CreateScene] = "Adds a scene to the set; same reason as creating tracks.",
        [AbletonCommand.ZoomTimePreset] =
            "Lands on an absolute zoom level, and Live never says what the level was before — so "
            + "unlike every other command here, the run could not put the view back.",
        [AbletonCommand.ZoomTracksPreset] = "Same.",
        [AbletonCommand.SetSend] =
            "Needs the set to have at least one return track, which not every scratch set does — "
            + "the script rejects send 0 when there are none, and that is correct behaviour, not a "
            + "failure worth failing the run over."
    };

    [Fact]
    public void EveryCommandIsEitherSmokeTestedOrExplicitlyExcused()
    {
        // Runs without Live: it is about this file staying honest as commands are added, not about
        // Ableton. A new command silently escaping the smoke run is exactly how a version bump
        // ships broken.
        var missing = Enum.GetValues<AbletonCommand>()
            .Where(command => !AbletonCommands.Describe(command).ViaKeyboard)
            .Where(command => !SafeCommands.Contains(command) && !NotSmokeTested.ContainsKey(command))
            .ToList();

        Assert.True(missing.Count == 0,
            "Sin cubrir en la corrida de humo ni excusados: " + string.Join(", ", missing));
    }

    // ---------- Plumbing ----------

    /// <summary>
    /// The actions one command contributes to the run.
    /// </summary>
    /// <remarks>
    /// A command that sets rather than toggles has to be run both ways, or the run walks off
    /// leaving the set muted, armed or record-enabled. It is the same reason every toggle appears
    /// twice in the list below. On first and off second, so the state it settles into is the
    /// harmless one.
    /// </remarks>
    private static IEnumerable<AbletonAction> Expand(AbletonCommand command)
    {
        if (!AbletonCommands.Describe(command).NeedsState)
        {
            yield return Build(command);
            yield break;
        }

        var on = Build(command);
        on.State = true;
        yield return on;

        var off = Build(command);
        off.State = false;
        yield return off;
    }

    private static AbletonAction Build(AbletonCommand command)
    {
        var action = AbletonAction.For(command);

        // Everything runs against the selection, so the test does not depend on how the open set
        // is laid out.
        action.TrackIndex = AbletonAction.SelectedTrack;
        action.SceneIndex = AbletonAction.SelectedScene;

        if (command == AbletonCommand.SelectTrack)
            action.TrackIndex = 0;

        return action;
    }

    private async Task ConnectAsync()
    {
        _adapter.Start();

        Assert.True(_transport.IsOpen,
            _transport.LastError ?? $"No se pudo abrir el puerto UDP {UdpOscTransport.DefaultReceivePort}.");

        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < HandshakeTimeout)
        {
            if (_adapter.Status.State == AbletonConnectionState.Connected) return;

            await Task.Delay(100);
            _adapter.Tick();
        }

        Assert.Fail(
            $"El Remote Script no contestó en {HandshakeTimeout.TotalSeconds:0} segundos " +
            $"(estado: {_adapter.Status.State} — {_adapter.Status.Detail}). " +
            "¿Live está abierto con DigitalDeck elegido como Control Surface?");
    }

    /// <summary>Sends one action and reports whether Live complained about it.</summary>
    private async Task<string> RunAsync(AbletonAction action)
    {
        int before;
        lock (_gate) before = _failures.Count;

        var result = await _adapter.ExecuteAsync(action);
        if (!result.Success) return result.Error ?? "rechazado por el puente";

        // Nothing acknowledges a command, so the only evidence of trouble is an error arriving
        // shortly after. Waiting is the honest way to look for it.
        await Task.Delay(SettleTime);

        lock (_gate)
        {
            return _failures.Count == before ? "ok" : _failures[^1];
        }
    }

    private async Task<string?> AskAsync(string address, params object?[] arguments) =>
        (await QueryAsync(address, arguments))?.ToString();

    private async Task<OscMessage?> QueryAsync(string address, params object?[] arguments)
    {
        int before;
        lock (_gate) before = _received.Count;

        _transport.Send(new OscMessage(address, arguments));

        // The browser walks a tree inside Live, so it needs longer than a transport command.
        await Task.Delay(SettleTime * 4);

        lock (_gate)
        {
            return _received.Skip(before).FirstOrDefault(message => message.Address == address);
        }
    }

    private async Task<double> ReadTempoAsync()
    {
        int before;
        lock (_gate) before = _received.Count;

        _transport.Send(new OscMessage("/live/song/get/tempo"));
        await Task.Delay(SettleTime);

        lock (_gate)
        {
            var reply = _received
                .Skip(before)
                .FirstOrDefault(message => message.Address == "/live/song/get/tempo");

            return reply?.GetDouble(0) ?? 120;
        }
    }

    private async Task RestoreAsync(double tempo)
    {
        var action = AbletonAction.For(AbletonCommand.SetTempo);
        action.Value = tempo;

        await _adapter.ExecuteAsync(action);
        await _adapter.ExecuteAsync(AbletonAction.For(AbletonCommand.Stop));
        await _adapter.ExecuteAsync(AbletonAction.For(AbletonCommand.ShowSession));
        await Task.Delay(SettleTime);
    }

    private void Report(IReadOnlyCollection<(AbletonCommand Command, string Outcome)> results)
    {
        foreach (var (command, outcome) in results)
            _output.WriteLine($"{(outcome == "ok" ? "OK  " : "FALL")} {command,-26} {outcome}");

        _output.WriteLine("");
        _output.WriteLine($"{results.Count(entry => entry.Outcome == "ok")}/{results.Count} aceptados por Live");
    }
}
