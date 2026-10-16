using System.Collections.Generic;
using System.Linq;
using Shunt.Core.Models;

namespace Shunt.Engine;

/// <summary>Resolution of the current input frame against configured chord masks.</summary>
public readonly record struct ChordResolution(
    GamepadButtons ActiveChordButtons,
    GamepadButtons PendingButtons,
    GamepadButtons FilteredButtons)
{
    public GamepadButtons SuppressedButtons => ActiveChordButtons | PendingButtons;
}

/// <summary>
/// Delays solo component dispatch briefly while a configured chord can still form.
/// Time is supplied in monotonic milliseconds so the state machine is deterministic in tests.
/// </summary>
public sealed class ChordStateMachine
{
    private readonly Dictionary<GamepadButtons, long> _pressedAt = [];

    public long ChordWindowMilliseconds { get; set; } = 50;

    public ChordResolution Resolve(
        GamepadButtons input,
        IEnumerable<GamepadButtons> chordMasks,
        long timestampMilliseconds)
    {
        var heldBits = (ulong)input;
        var bit = 1UL;
        while (heldBits != 0)
        {
            if ((heldBits & bit) != 0)
            {
                var button = (GamepadButtons)bit;
                if (!_pressedAt.ContainsKey(button)) _pressedAt[button] = timestampMilliseconds;
                heldBits &= ~bit;
            }
            bit <<= 1;
            if (bit == 0) break;
        }

        foreach (var released in _pressedAt.Keys.Where(button => (input & button) == 0).ToArray())
            _pressedAt.Remove(released);

        var activeChords = GamepadButtons.None;
        var pendingButtons = GamepadButtons.None;
        foreach (var chord in chordMasks.Where(mask => mask != GamepadButtons.None && IsChord(mask)))
        {
            var components = EnumerateBits(chord).ToArray();
            if ((input & chord) == chord)
            {
                var earliest = components.Min(button => _pressedAt.GetValueOrDefault(button, timestampMilliseconds));
                var latest = components.Max(button => _pressedAt.GetValueOrDefault(button, timestampMilliseconds));
                if (latest - earliest <= ChordWindowMilliseconds)
                    activeChords |= chord;
            }

            foreach (var component in components)
            {
                if ((input & component) == 0 || !_pressedAt.TryGetValue(component, out var pressedAt)) continue;
                if (timestampMilliseconds - pressedAt < ChordWindowMilliseconds)
                    pendingButtons |= component;
            }
        }

        pendingButtons &= ~activeChords;
        var suppressed = activeChords | pendingButtons;
        return new ChordResolution(activeChords, pendingButtons, input & ~suppressed);
    }

    public void Reset() => _pressedAt.Clear();

    private static bool IsChord(GamepadButtons buttons) => System.Numerics.BitOperations.PopCount((ulong)buttons) > 1;

    private static IEnumerable<GamepadButtons> EnumerateBits(GamepadButtons buttons)
    {
        var remaining = (ulong)buttons;
        while (remaining != 0)
        {
            var bit = remaining & (~remaining + 1);
            yield return (GamepadButtons)bit;
            remaining &= ~bit;
        }
    }
}
