using System.Text.Json.Serialization;

namespace DigitalDeck.Core.Model.Actions;

/// <summary>
/// What a button does when pressed. Serialised into the item's <c>data</c> column with a "kind"
/// discriminator, so new action types can be added without a schema migration.
/// </summary>
/// <remarks>
/// To add an action type: derive from this class, register it with a
/// <see cref="JsonDerivedTypeAttribute"/> below, and register a matching
/// <see cref="Abstractions.IActionHandler"/> implementation with the dispatcher.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(OpenPathAction), "open_path")]
[JsonDerivedType(typeof(OpenUrlAction), "open_url")]
[JsonDerivedType(typeof(AbletonAction), "ableton")]
[JsonDerivedType(typeof(AbletonLoadDeviceAction), "ableton_load")]
[JsonDerivedType(typeof(MixerPanelAction), "mixer_panel")]
[JsonDerivedType(typeof(HotkeyAction), "hotkey")]
[JsonDerivedType(typeof(MacroAction), "macro")]
public abstract class ActionPayload
{
    /// <summary>Short human-readable summary shown in the editor and on tile tooltips.</summary>
    [JsonIgnore]
    public abstract string Summary { get; }

    /// <summary>
    /// True when running this action could start a program. The editor requires an explicit
    /// acknowledgement before saving such a button, and the dispatcher refuses to run one that
    /// was never acknowledged.
    /// </summary>
    [JsonIgnore]
    public virtual bool IsPotentiallyDangerous => false;

    /// <summary>Set once the user has explicitly acknowledged a dangerous action.</summary>
    public bool Acknowledged { get; set; }

    /// <summary>Reason this action cannot run as configured, or null when it is valid.</summary>
    public virtual string? Validate() => null;
}
