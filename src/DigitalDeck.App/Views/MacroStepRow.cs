using System.ComponentModel;
using System.Globalization;
using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.App.Views;

/// <summary>
/// One row of the «varias acciones» list: the step itself plus what the editor draws for it.
/// </summary>
/// <remarks>
/// The delay is edited in seconds and stored in milliseconds — seconds is what somebody waiting for
/// Spotify to open thinks in, and milliseconds is what <see cref="Task.Delay(int)"/> takes.
/// </remarks>
public sealed class MacroStepRow : INotifyPropertyChanged
{
    private int _position;
    private string _delaySeconds;

    public MacroStepRow(MacroStep step)
    {
        Step = step;
        _delaySeconds = (step.DelayMs / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
    }

    public MacroStep Step { get; }

    public string Description => Step.Action.Summary;

    /// <summary>Its place in the order, as the list shows it: 1, 2, 3…</summary>
    public int Position
    {
        get => _position;
        set
        {
            if (_position == value) return;
            _position = value;
            Raise(nameof(Position));
        }
    }

    public string DelaySeconds
    {
        get => _delaySeconds;
        set
        {
            if (_delaySeconds == value) return;
            _delaySeconds = value;
            Raise(nameof(DelaySeconds));
        }
    }

    /// <summary>
    /// Reads the typed delay onto the step. False when the text is not a number the step can take,
    /// which leaves the stored value alone so a half-typed «1.» does not wipe it.
    /// </summary>
    public bool TryApplyDelay()
    {
        var text = DelaySeconds.Trim().Replace(',', '.');

        if (text.Length == 0)
        {
            Step.DelayMs = 0;
            return true;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return false;

        if (seconds < 0 || seconds * 1000 > MacroStep.MaxDelayMs)
            return false;

        Step.DelayMs = (int)Math.Round(seconds * 1000);
        return true;
    }

    public void Refresh() => Raise(nameof(Description));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
