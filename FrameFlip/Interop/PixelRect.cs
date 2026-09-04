namespace FrameFlip.Interop;

/// <summary>Rechteck in echten Geraetepixeln (nicht in DIPs).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);
