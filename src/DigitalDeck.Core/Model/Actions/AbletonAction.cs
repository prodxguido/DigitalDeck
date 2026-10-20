using System.Globalization;
using System.Text.Json.Serialization;

namespace DigitalDeck.Core.Model.Actions;

/// <summary>
/// Drives Ableton Live through the DigitalDeck Remote Script.
/// </summary>
/// <remarks>
/// Not marked dangerous: unlike a path button, nothing here starts a program. The worst an
/// unconfirmed press can do is change something in the open Live set, which Live's own undo covers.
/// </remarks>
public sealed class AbletonAction : ActionPayload
{
    /// <summary>Track index meaning "whichever track is selected in Live right now".</summary>
    public const int SelectedTrack = -1;

    /// <summary>Track index meaning the master track.</summary>
    public const int MasterTrack = -2;

    /// <summary>Scene index meaning "the selected scene".</summary>
    public const int SelectedScene = -1;

    public AbletonCommand Command { get; set; }

    /// <summary>
    /// Which track the command acts on. Defaults to the selected one, because a deck button is
    /// configured once and pressed for months — "the track I am working on" ages far better than
    /// "track 4", which stops meaning anything the moment a track is inserted above it.
    /// </summary>
    public int TrackIndex { get; set; } = SelectedTrack;

    public int SceneIndex { get; set; } = SelectedScene;

    /// <summary>Tempo, volume, pan or nudge amount, depending on the command.</summary>
    public double Value { get; set; }

    /// <summary>
    /// On or off, for the commands that set rather than toggle.
    /// </summary>
    /// <remarks>
    /// The reason those commands exist: a toggle leaves the set wherever it happened to be, which
    /// is fine for a button somebody is watching and wrong for one meant to put things in a known
    /// state — the last thing you want when arming a take is a button that disarms it.
    /// </remarks>
    public bool State { get; set; } = true;

    /// <summary>Which send of the track, counted from zero: 0 is send A.</summary>
    public int SendIndex { get; set; }

    [JsonIgnore]
    public AbletonCommandInfo Info => AbletonCommands.Describe(Command);

    [JsonIgnore]
    public override string Summary
    {
        get
        {
            var info = Info;
            var parts = new List<string> { info.Label };

            if (info.NeedsTrack) parts.Add(DescribeTrack(TrackIndex));
            if (info.NeedsScene) parts.Add(DescribeScene(SceneIndex));
            if (info.NeedsSend) parts.Add(DescribeSend(SendIndex));
            if (info.NeedsState) parts.Add(State ? "encender" : "apagar");

            if (info.NeedsValue)
            {
                var number = Value.ToString("0.###", CultureInfo.CurrentCulture);
                parts.Add(info.ValueLabel is null ? number : $"{number} {info.ValueLabel}");
            }

            return string.Join(" · ", parts);
        }
    }

    public override string? Validate()
    {
        if (!Enum.IsDefined(Command))
            return "Ese comando de Ableton ya no existe en esta versión de DigitalDeck.";

        var info = Info;

        if (info.NeedsTrack && TrackIndex < MasterTrack)
            return "El número de track no es válido.";

        if (info.NeedsScene && SceneIndex < SelectedScene)
            return "El número de escena no es válido.";

        if (info.NeedsSend && SendIndex < 0)
            return "El send tiene que ser A o posterior.";

        if (info.NeedsValue && (double.IsNaN(Value) || Value < info.ValueMinimum || Value > info.ValueMaximum))
        {
            var low = info.ValueMinimum.ToString("0.###", CultureInfo.CurrentCulture);
            var high = info.ValueMaximum.ToString("0.###", CultureInfo.CurrentCulture);
            return $"{info.ValueLabel ?? "El valor"} tiene que estar entre {low} y {high}.";
        }

        return null;
    }

    /// <summary>A fresh action for a command, with that command's default value already filled in.</summary>
    public static AbletonAction For(AbletonCommand command)
    {
        var info = AbletonCommands.Describe(command);

        return new AbletonAction
        {
            Command = command,
            Value = info.ValueDefault
        };
    }

    public static string DescribeTrack(int index) => index switch
    {
        SelectedTrack => "track seleccionada",
        MasterTrack => "master",
        _ => $"track {index + 1}"
    };

    public static string DescribeScene(int index) =>
        index == SelectedScene ? "escena seleccionada" : $"escena {index + 1}";

    /// <summary>Sends are lettered in Live's own mixer, so they are lettered here too.</summary>
    public static string DescribeSend(int index) =>
        index is >= 0 and < 26
            ? $"send {(char)('A' + index)}"
            : $"send {index + 1}";
}
