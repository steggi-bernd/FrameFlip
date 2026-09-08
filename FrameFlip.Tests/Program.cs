using FrameFlip.Tests;

// WPF-Typen (WriteableBitmap, Image, RenderTargetBitmap) verlangen einen STA-Thread.
int exit = 1;
var thread = new Thread(() => exit = RunAll()) { Name = "FrameFlipTests" };
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
return exit;

static int RunAll()
{
    Console.WriteLine("FrameFlip - Invarianten aus Teil 1 bis 3");

    LoadDictionaries();

    ZoomInvariants.Run();
    BufferInvariants.Run();
    LayoutRegression.Run();
    SequenceInvariants.Run();
    ExportInvariants.Run();
    ExportInvariants.RequestMath();
    PlacementInvariants.Run();
    GovernorInvariants.Run();
    ImagingInvariants.Run();
    RawCacheInvariants.Run();
    CadenceInvariants.Run();
    RangeInvariants.Run();
    ViewerPlaybackInvariants.Run();
    ViewerControllerInvariants.Run();
    BridgeInvariants.Run();
    LocalizationInvariants.Run();
    RemoteInvariants.Run();
    RelayClientInvariants.Run();
    MachineInvariants.Run();
    RemoteCommandRouterInvariants.Run();
    RemotePreviewFollowInvariants.Run();
    SettingsInvariants.Run();
    PreviewInvariants.Run();
    PlacementRegression.Run();
    ProjectInvariants.Run();
    ProjectInvariants.Frames();
    ProjectNavigationInvariants.Run();
    ProjectNavigationStateInvariants.Run();
    ResourceInvariants.Run();
    VaultInvariants.Run();
    BrowseInvariants.Run();
    BrowseInvariants.Movies();
    RenderInvariants.Run();
    RenderServiceInvariants.Run();
    UploadInvariants.Run();

    return Check.Report();
}

/// <summary>
/// Die Woerterbuecher laden, damit Strings.T Texte liefert und nicht Schluessel.
///
/// Gelesen wird die Quelldatei, nicht die ins Programm eingebackene Fassung: Der
/// Weg ueber pack-Adressen verlangt, dass die Anwendung ihre Ressourcen-Assembly
/// kennt, und die steht in einem Testlaeufer schon auf ihm selbst. Die lose XAML
/// einzulesen ist der kuerzere Weg zum selben Ergebnis.
///
/// Ohne das pruefte jede Zusicherung ueber eine Beschriftung "S_LabelVersion"
/// statt "v2" - und das faellt erst auf, nachdem man den falschen Fehler gesucht hat.
/// </summary>
static void LoadDictionaries()
{
    try
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);

        for (int depth = 0; depth < 8 && directory is not null; depth++)
        {
            string candidate = System.IO.Path.Combine(directory.FullName, "FrameFlip", "Localization",
                                                      "Strings.de.xaml");

            if (System.IO.File.Exists(candidate))
            {
                var app = System.Windows.Application.Current ?? new System.Windows.Application();
                // Fenster-Tests duerfen beim Schliessen nicht die gemeinsame
                // Application samt Ressourcen und Dispatcher beenden.
                app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

                using var stream = System.IO.File.OpenRead(candidate);

                app.Resources.MergedDictionaries.Add(
                    (System.Windows.ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream));

                return;
            }

            directory = directory.Parent;
        }

        Console.WriteLine("  Woerterbuch nicht gefunden - Beschriftungen bleiben Schluessel.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Woerterbuecher nicht geladen: {ex.GetType().Name} - {ex.Message}");
    }
}
