using System.Text;
using System.Windows.Input;

namespace FrameFlip.Interop;

/// <summary>Modifier-plus-Taste-Kombination, in der Konfiguration als "Ctrl+Alt+Space" abgelegt.</summary>
public readonly record struct HotKeyDefinition(ModifierKeys Modifiers, Key Key)
{
    public static readonly HotKeyDefinition Default = new(ModifierKeys.Control | ModifierKeys.Alt, Key.Space);

    public bool IsValid => Key != Key.None && Modifiers != ModifierKeys.None;

    public uint NativeModifiers
    {
        get
        {
            uint m = NativeMethods.MOD_NOREPEAT;
            if (Modifiers.HasFlag(ModifierKeys.Alt)) m |= NativeMethods.MOD_ALT;
            if (Modifiers.HasFlag(ModifierKeys.Control)) m |= NativeMethods.MOD_CONTROL;
            if (Modifiers.HasFlag(ModifierKeys.Shift)) m |= NativeMethods.MOD_SHIFT;
            if (Modifiers.HasFlag(ModifierKeys.Windows)) m |= NativeMethods.MOD_WIN;
            return m;
        }
    }

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public override string ToString()
    {
        if (!IsValid) return string.Empty;
        var sb = new StringBuilder();
        if (Modifiers.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) sb.Append("Win+");
        sb.Append(Key.ToString());
        return sb.ToString();
    }

    public static bool TryParse(string? text, out HotKeyDefinition definition)
    {
        definition = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var modifiers = ModifierKeys.None;
        var key = Key.None;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win":
                case "windows": modifiers |= ModifierKeys.Windows; break;
                default:
                    if (!Enum.TryParse(part, ignoreCase: true, out key)) return false;
                    break;
            }
        }

        definition = new HotKeyDefinition(modifiers, key);
        return definition.IsValid;
    }
}
