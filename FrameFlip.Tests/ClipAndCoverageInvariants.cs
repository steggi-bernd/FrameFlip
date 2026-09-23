using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Was eine Schnittmaske und eine Gruppe abdecken - in der Farbe und in der Deckung.
///
/// Drei Stellen, an denen der Composer etwas rechnete, das er nicht meinte. Sie fielen
/// nicht beim Ansehen auf, sondern beim Aufschreiben dessen, was ein Knoten "Mischen"
/// tun soll: Ein Knoten muss es in einem Satz sagen koennen, und fuer diese drei Faelle
/// gab es keinen Satz, nur den Code.
/// </summary>
public static class ClipAndCoverageInvariants
{
    private const int Size = 64;

    public static void Run()
    {
        AClippedLayerStaysOnItsPlacedCarrier();
        AGroupCoversWithItsOpacity();
        AClippedLayerSharesItsCarriersCoverage();
    }

    /// <summary>
    /// Ein Logo in der Mitte, darauf eine rote Flaeche angeschnitten: Rot gehoert aufs
    /// Logo und nirgends sonst hin.
    ///
    /// Vorher oeffnete das Logo dort, wo es nicht lag, gar keine Gruppe - und die rote
    /// Flaeche wurde dort zur gewoehnlichen Ebene, ueber das ganze Bild.
    /// </summary>
    private static void AClippedLayerStaysOnItsPlacedCarrier()
    {
        Check.Group("Eine angeschnittene Ebene bleibt auf ihrem platzierten Traeger");

        var sources = new Dictionary<string, FloatFrame>
        {
            [""] = Flat(Size, Size, 0.2f, 0.2f, 0.2f, sceneReferred: true),
            ["logo.png"] = Flat(16, 16, 1f, 1f, 1f, sceneReferred: false),
            ["rot.png"] = Flat(Size, Size, 1f, 0f, 0f, sceneReferred: false),
        };

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Content = LayerContent.Pass, Source = "" },
                new ImageLayer { Content = LayerContent.Image, Source = "logo.png", Place = { Scale = 0.5f } },
                new ImageLayer { Content = LayerContent.Image, Source = "rot.png", Clipped = true },
            },
        };

        var built = LayerComposer.Compose(stack, sources)!;

        int corner = 2 * Size + 2;
        int middle = (Size / 2) * Size + Size / 2;

        Check.Near(built.R[corner], 0.2, 1e-5, "ausserhalb des Logos bleibt das Bild, wie es war");
        Check.Near(built.G[corner], 0.2, 1e-5, "und zwar in allen Kanaelen");

        Check.That(built.R[middle] > 0.9f && built.G[middle] < 0.1f,
                   "auf dem Logo liegt das Rot", $"{built.R[middle]:0.00}/{built.G[middle]:0.00}");
    }

    /// <summary>
    /// Eine Gruppe auf halber Deckkraft ueber nichts deckt halb - nicht ganz.
    /// </summary>
    private static void AGroupCoversWithItsOpacity()
    {
        Check.Group("Eine Gruppe deckt mit ihrer Deckkraft");

        var sources = new Dictionary<string, FloatFrame>
        {
            [""] = Flat(Size, Size, 0f, 0f, 0f, sceneReferred: true),
            ["rot.png"] = Flat(Size, Size, 1f, 0f, 0f, sceneReferred: false),
        };

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer
                {
                    Content = LayerContent.Group,
                    Opacity = 0.5f,
                    Children = { new ImageLayer { Content = LayerContent.Image, Source = "rot.png" } },
                },
            },
        };

        var built = LayerComposer.Compose(stack, sources)!;

        Check.Near(built.A![0], 0.5, 1e-5, "halbe Deckkraft, halbe Deckung");
        Check.Near(built.R[0], 0.5, 1e-5, "und die Farbe ist ebenso halb");
    }

    /// <summary>
    /// Ein Pass aus einer EXR, der links nichts trifft, und darauf eine Flaeche
    /// angeschnitten: Links bleibt es durchsichtig, wie der Traeger.
    /// </summary>
    private static void AClippedLayerSharesItsCarriersCoverage()
    {
        Check.Group("Eine angeschnittene Ebene deckt, was ihr Traeger deckt");

        var carrier = Flat(Size, Size, 0.3f, 0.3f, 0.3f, sceneReferred: true);

        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size / 2; x++)
                carrier.A![y * Size + x] = 0f;

        var sources = new Dictionary<string, FloatFrame>
        {
            [""] = carrier,
            ["rot.png"] = Flat(Size, Size, 1f, 0f, 0f, sceneReferred: false),
        };

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Content = LayerContent.Pass, Source = "" },
                new ImageLayer { Content = LayerContent.Image, Source = "rot.png", Clipped = true, Opacity = 0.8f },
            },
        };

        var built = LayerComposer.Compose(stack, sources)!;

        int left = (Size / 2) * Size + 4;
        int right = (Size / 2) * Size + Size - 4;

        Check.Near(built.A![left], 0.0, 1e-5, "wo der Traeger nichts trifft, bleibt es durchsichtig");
        Check.Near(built.A![right], 1.0, 1e-5, "wo er trifft, deckt es");
    }

    private static FloatFrame Flat(int width, int height, float r, float g, float b, bool sceneReferred)
    {
        int count = width * height;

        return new FloatFrame
        {
            Width = width,
            Height = height,
            R = Enumerable.Repeat(r, count).ToArray(),
            G = Enumerable.Repeat(g, count).ToArray(),
            B = Enumerable.Repeat(b, count).ToArray(),
            A = Enumerable.Repeat(1f, count).ToArray(),
            IsSceneReferred = sceneReferred,
        };
    }
}
