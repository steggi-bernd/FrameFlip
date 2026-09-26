namespace FrameFlip.Views;

/// <summary>
/// Was die Uebersicht der Einstellungen nur vom Wirt erfaehrt: ob die Blender-Bruecke
/// lauscht und wie voll der Vorschau-Speicher ist - beides in Worten.
/// </summary>
internal sealed record SettingsStatus(string Bridge, string Memory);
