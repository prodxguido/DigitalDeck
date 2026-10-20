using System.Text.Json.Serialization;

namespace DigitalDeck.Core.Model.Actions;

/// <summary>
/// One entry in a <see cref="MacroAction"/>: an action, and how long to wait before running it.
/// </summary>
public sealed class MacroStep
{
    /// <summary>Longest pause a step may declare (five minutes), so a typo cannot hang the deck.</summary>
    public const int MaxDelayMs = 300_000;

    public required ActionPayload Action { get; set; }

    /// <summary>
    /// Milliseconds to wait <em>before</em> this step. Before rather than after, so the last step
    /// never leaves the button spinning on a pause nobody sees the point of.
    /// </summary>
    public int DelayMs { get; set; }
}

/// <summary>
/// Runs several actions in order from a single press — «arrancá la mañana»: la playlist, el diario,
/// Live y Spotify, uno atrás del otro.
/// </summary>
/// <remarks>
/// The steps hold plain <see cref="ActionPayload"/>s, so a macro can do anything a single button
/// can. What it cannot hold is another macro: nesting would let a deck build a cycle that runs
/// forever, and there is nothing a nested macro expresses that a longer list does not.
/// </remarks>
public sealed class MacroAction : ActionPayload
{
    /// <summary>Enough for any routine, and low enough that a corrupt file cannot ask for millions.</summary>
    public const int MaxSteps = 20;

    public List<MacroStep> Steps { get; set; } = new();

    /// <summary>
    /// Keep going when a step fails. On by default: if Spotify is not installed, the rest of the
    /// morning should still open, and the failure is reported at the end.
    /// </summary>
    public bool ContinueOnError { get; set; } = true;

    [JsonIgnore]
    public override string Summary => Steps.Count switch
    {
        0 => "Sin acciones todavía",
        1 => Steps[0].Action.Summary,
        _ => $"{Steps.Count} acciones en orden: {string.Join(" → ", Steps.Take(3).Select(s => s.Action.Summary))}"
             + (Steps.Count > 3 ? "…" : string.Empty)
    };

    [JsonIgnore]
    public override bool IsPotentiallyDangerous => Steps.Any(s => s.Action.IsPotentiallyDangerous);

    public override string? Validate()
    {
        if (Steps.Count == 0)
            return "Agregá al menos una acción a la secuencia.";

        if (Steps.Count > MaxSteps)
            return $"Una secuencia admite hasta {MaxSteps} acciones.";

        for (var i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            var position = i + 1;

            if (step.Action is MacroAction)
                return $"Paso {position}: una secuencia no puede contener otra secuencia.";

            if (step.DelayMs < 0 || step.DelayMs > MacroStep.MaxDelayMs)
                return $"Paso {position}: la espera va de 0 a {MacroStep.MaxDelayMs / 1000} segundos.";

            if (step.Action.IsPotentiallyDangerous && !step.Action.Acknowledged)
                return $"Paso {position}: esa acción ejecuta un programa y falta confirmarla.";

            if (step.Action.Validate() is { } problem)
                return $"Paso {position}: {problem}";
        }

        return null;
    }
}
