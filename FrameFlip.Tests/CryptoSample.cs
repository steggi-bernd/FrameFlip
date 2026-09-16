namespace FrameFlip.Tests;

/// <summary>
/// Ein Render mit Kryptomatten, aus Blender 4.5 - 16x12 Bildpunkte, Cycles.
///
/// Drei benannte Objekte nebeneinander auf einem Boden: Kugel, Wuerfel, Kegel. Jedes
/// mit eigenem Material, damit sich beide Kryptomatten pruefen lassen - die nach
/// Objekt und die nach Material.
///
/// An dieser Datei haengt mehr als eine Pruefung des Manifests. Sie hat beim Bauen
/// eine Annahme widerlegt: Blender schreibt die Kryptomattenkanaele KLEIN
/// geschrieben - "ViewLayer.CryptoObject00.r" -, waehrend alles andere in derselben
/// Datei gross geschrieben ist. Und sie liegen als float32 vor, waehrend das Bild
/// selbst half ist: Eine Kennung ist ein Hashwert, und auf sechzehn Bit gerundet
/// waere sie ein anderer.
/// </summary>
internal static class CryptoSample
{
    public const int Width = 16;
    public const int Height = 12;

    public static byte[] Bytes() => Convert.FromBase64String(Data);

    private const string Data =
        "di8xAQIEAABCbGVuZGVyTXVsdGlDaGFubmVsAHN0cmluZwAZAAAAQmxlbmRlciBWMi41NS4xIGFuZCBuZXdlckNhbWVyYQBzdHJpbmcA" +
        "BgAAAEthbWVyYURhdGUAc3RyaW5nABMAAAAyMDI2LzA5LzE2IDE1OjEzOjE0RmlsZQBzdHJpbmcACgAAADx1bnRpdGxlZD5GcmFtZQBz" +
        "dHJpbmcAAQAAADFSZW5kZXJUaW1lAHN0cmluZwAIAAAAMDA6MDAuMDBTY2VuZQBzdHJpbmcABQAAAFNjZW5lVGltZQBzdHJpbmcACwAA" +
        "ADAwOjAwOjAwOjAxY2hhbm5lbHMAY2hsaXN0AGQFAABWaWV3TGF5ZXIuQ29tYmluZWQuQQABAAAAAAAAAAEAAAABAAAAVmlld0xheWVy" +
        "LkNvbWJpbmVkLkIAAQAAAAAAAAABAAAAAQAAAFZpZXdMYXllci5Db21iaW5lZC5HAAEAAAAAAAAAAQAAAAEAAABWaWV3TGF5ZXIuQ29t" +
        "YmluZWQuUgABAAAAAAAAAAEAAAABAAAAVmlld0xheWVyLkNyeXB0b01hdGVyaWFsMDAuYQACAAAAAAAAAAEAAAABAAAAVmlld0xheWVy" +
        "LkNyeXB0b01hdGVyaWFsMDAuYgACAAAAAAAAAAEAAAABAAAAVmlld0xheWVyLkNyeXB0b01hdGVyaWFsMDAuZwACAAAAAAAAAAEAAAAB" +
        "AAAAVmlld0xheWVyLkNyeXB0b01hdGVyaWFsMDAucgACAAAAAAAAAAEAAAABAAAAVmlld0xheWVyLkNyeXB0b01hdGVyaWFsMDEuYQAC" +
        "AAAAAAAAAAEAAAABAAAAVmlld0xheWVyLkNyeXB0b01hdGVyaWFsMDEuYgACAAAAAAAAAAEAAAABAAAAVmlld0xheWVyLkNyeXB0b01h" +
        "dGVyaWFsMDEuZwACAAAAAAAAAAEAAAABAAAAVmlld0xheWVyLkNyeXB0b01hdGVyaWFsMDEucgACAAAAAAAAAAEAAAABAAAAVmlld0xh" +
        "eWVyLkNyeXB0b01hdGVyaWFsMDIuYQACAAAAAAAAAAEAAAABAAAAVmlld0xheWVyLkNyeXB0b01hdGVyaWFsMDIuYgACAAAAAAAAAAEA" +
        "AAABAAAAVmlld0xheWVyLkNyeXB0b01hdGVyaWFsMDIuZwACAAAAAAAAAAEAAAABAAAAVmlld0xheWVyLkNyeXB0b01hdGVyaWFsMDIu" +
        "cgACAAAAAAAAAAEAAAABAAAAVmlld0xheWVyLkNyeXB0b09iamVjdDAwLmEAAgAAAAAAAAABAAAAAQAAAFZpZXdMYXllci5DcnlwdG9P" +
        "YmplY3QwMC5iAAIAAAAAAAAAAQAAAAEAAABWaWV3TGF5ZXIuQ3J5cHRvT2JqZWN0MDAuZwACAAAAAAAAAAEAAAABAAAAVmlld0xheWVy" +
        "LkNyeXB0b09iamVjdDAwLnIAAgAAAAAAAAABAAAAAQAAAFZpZXdMYXllci5DcnlwdG9PYmplY3QwMS5hAAIAAAAAAAAAAQAAAAEAAABW" +
        "aWV3TGF5ZXIuQ3J5cHRvT2JqZWN0MDEuYgACAAAAAAAAAAEAAAABAAAAVmlld0xheWVyLkNyeXB0b09iamVjdDAxLmcAAgAAAAAAAAAB" +
        "AAAAAQAAAFZpZXdMYXllci5DcnlwdG9PYmplY3QwMS5yAAIAAAAAAAAAAQAAAAEAAABWaWV3TGF5ZXIuQ3J5cHRvT2JqZWN0MDIuYQAC" +
        "AAAAAAAAAAEAAAABAAAAVmlld0xheWVyLkNyeXB0b09iamVjdDAyLmIAAgAAAAAAAAABAAAAAQAAAFZpZXdMYXllci5DcnlwdG9PYmpl" +
        "Y3QwMi5nAAIAAAAAAAAAAQAAAAEAAABWaWV3TGF5ZXIuQ3J5cHRvT2JqZWN0MDIucgACAAAAAAAAAAEAAAABAAAAVmlld0xheWVyLkRl" +
        "cHRoLloAAgAAAAAAAAABAAAAAQAAAFZpZXdMYXllci5EaWZmQ29sLkIAAQAAAAAAAAABAAAAAQAAAFZpZXdMYXllci5EaWZmQ29sLkcA" +
        "AQAAAAAAAAABAAAAAQAAAFZpZXdMYXllci5EaWZmQ29sLlIAAQAAAAAAAAABAAAAAQAAAFZpZXdMYXllci5NaXN0LloAAgAAAAAAAAAB" +
        "AAAAAQAAAABjb21wcmVzc2lvbgBjb21wcmVzc2lvbgABAAAAA2NyeXB0b21hdHRlLzU0MmNhZmEvY29udmVyc2lvbgBzdHJpbmcAEQAA" +
        "AHVpbnQzMl90b19mbG9hdDMyY3J5cHRvbWF0dGUvNTQyY2FmYS9oYXNoAHN0cmluZwAOAAAATXVybXVySGFzaDNfMzJjcnlwdG9tYXR0" +
        "ZS81NDJjYWZhL21hbmlmZXN0AHN0cmluZwCbAAAAeyJDdWJlIjoiYThmY2U4NjUiLCJMaWdodCI6ImM3N2Q0M2U1IiwiS3VnZWwiOiI1" +
        "MzJkNjgxOCIsIld1ZXJmZWwiOiJjYTE5MGQ5OSIsIktlZ2VsIjoiODJlYTIyNzUiLCJCb2RlbiI6ImQ3Y2Q0MTM5IiwiU29ubmUiOiIy" +
        "ZWIyMGM0MSIsIm9iamVjdCI6IjA1NWVjNzNkIn1jcnlwdG9tYXR0ZS81NDJjYWZhL25hbWUAc3RyaW5nABYAAABWaWV3TGF5ZXIuQ3J5" +
        "cHRvT2JqZWN0Y3J5cHRvbWF0dGUvYzdkYmY1ZS9jb252ZXJzaW9uAHN0cmluZwARAAAAdWludDMyX3RvX2Zsb2F0MzJjcnlwdG9tYXR0" +
        "ZS9jN2RiZjVlL2hhc2gAc3RyaW5nAA4AAABNdXJtdXJIYXNoM18zMmNyeXB0b21hdHRlL2M3ZGJmNWUvbWFuaWZlc3QAc3RyaW5nACgB" +
        "AAB7ImRlZmF1bHRfc3VyZmFjZSI6Ijc4ZDA1NTI5IiwiZGVmYXVsdF92b2x1bWUiOiJiMzMxMjgwZSIsImRlZmF1bHRfbGlnaHQiOiJm" +
        "ZTI2OWY5MyIsImRlZmF1bHRfYmFja2dyb3VuZCI6ImRiYTdlYzg1IiwiZGVmYXVsdF9lbXB0eSI6IjlhNDZjYTAzIiwic2hhZGVyIjoi" +
        "YTE3NjdmZTkiLCJNYXRlcmlhbCI6IjM5MTMyMDlkIiwiTWF0ZXJpYWxLdWdlbCI6ImZjMjBlMDFiIiwiTWF0ZXJpYWxXdWVyZmVsIjoi" +
        "NDlhYjU2ODUiLCJNYXRlcmlhbEtlZ2VsIjoiMzZjZjIzZTEiLCJNYXRlcmlhbEJvZGVuIjoiZWJhNjUwNjgifWNyeXB0b21hdHRlL2M3" +
        "ZGJmNWUvbmFtZQBzdHJpbmcAGAAAAFZpZXdMYXllci5DcnlwdG9NYXRlcmlhbGN5Y2xlcy5WaWV3TGF5ZXIucmVuZGVyX3RpbWUAc3Ry" +
        "aW5nAAgAAAAwMDowMC4wMGN5Y2xlcy5WaWV3TGF5ZXIuc2FtcGxlcwBzdHJpbmcAAQAAADhjeWNsZXMuVmlld0xheWVyLnN5bmNocm9u" +
        "aXphdGlvbl90aW1lAHN0cmluZwAIAAAAMDA6MDAuMDBjeWNsZXMuVmlld0xheWVyLnRvdGFsX3RpbWUAc3RyaW5nAAgAAAAwMDowMC4w" +
        "MGRhdGFXaW5kb3cAYm94MmkAEAAAAAAAAAAAAAAADwAAAAsAAABkaXNwbGF5V2luZG93AGJveDJpABAAAAAAAAAAAAAAAA8AAAALAAAA" +
        "bGluZU9yZGVyAGxpbmVPcmRlcgABAAAAAHBpeGVsQXNwZWN0UmF0aW8AZmxvYXQABAAAAAAAgD9zY3JlZW5XaW5kb3dDZW50ZXIAdjJm" +
        "AAgAAAAAAAAAAAAAAHNjcmVlbldpbmRvd1dpZHRoAGZsb2F0AAQAAAAAAIA/eERlbnNpdHkAZmxvYXQABAAAAAAAkEIAkwsAAAAAAAAA" +
        "AAAAgA4AAHhe7JsHVBTXGsdBA8TeqBYeVvApNsRCCcsK0aiokUTaSlDpBARBqbszO0tTKRFEFFdFKVbgiYKAUqQoFkQFImJhsYJwjkYE" +
        "u757Z9vMusvie4mSsP/vcJn/nXvvDN/c38ydPYscSpaFiL8u4s+L+JEiXiaZepMC2Y5dhoeYPl1KTopEmst4lUmm7qsH8Mp2JHjA68at" +
        "BA949f6N4L8Mr7vDHMTUyvSltDPBUkytTN3iVf8SuUvXHofyynCtT0DlidgWKCP35PAmx+LJDyaWH0E941Fqg0vWSj1NBltvRxKqW4Mq" +
        "Kp7BvIfeW6CSZ7wMHXP/M3hVSK8TUwvEAefG0YJb/DMinNXusGBBPEy35QUcaY3XceIoMkmRhpyLmFpp+d+Z4CGINsGkgSN1dOwgjtJL" +
        "NTe2cnqZ8QROngRe+6lQLmjunYSh7QOb0F+3o5rQb47AUIU3CZAf3A/P5u4Hfqcr25kRnz+niPLIdvBz60MKbwvX0UIpQoZFjp+qdbiU" +
        "8tg9a3vHrYkcezV0rJK679Ep/VN0GbnLOQ900BD/4dbnQwegjrajFbyxss/gVcIzUjAH5ErgWYGZIyeXjLfESyGvQlph5HrFyXj9LEl4" +
        "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";
}
