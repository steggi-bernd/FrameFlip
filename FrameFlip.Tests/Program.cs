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
    ViewTransformInvariants.Run();
    FloatImagingInvariants.Run();
    GradingInvariants.Run();
    GradingColourInvariants.Run();
    GradingBandInvariants.Run();
    CurveEditingInvariants.Run();
    CurveRenderInvariants.Run();
    AtelierPageInvariants.Run();
    PanelPolishInvariants.Run();
    ColourWheelInvariants.Run();
    BlendInvariants.Run();
    LayerInvariants.Run();
    MaskInvariants.Run();
    AdjustmentInvariants.Run();
    ImageAndGroupInvariants.Run();
    LayerPlacementInvariants.Run();
    PlacementDragInvariants.Run();
    LocalToolInvariants.Run();
    OpticsInvariants.Run();
    RenderDataInvariants.Run();
    PngLayerInvariants.Run();
    StackReproInvariants.Run();
    LayerPanelInvariants.Run();
    PassRebuildInvariants.Run();
    CryptomatteInvariants.Run();
    ImageHitInvariants.Run();
    AtelierLayerInvariants.Run();
    GradeBatchInvariants.Run();
    GradeVideoInvariants.Run();
    ExrInvariants.Run();
    RawCacheInvariants.Run();
    CadenceInvariants.Run();
    RangeInvariants.Run();
    ViewerPlaybackInvariants.Run();
    ViewerControllerInvariants.Run();
    BridgeInvariants.Run();
    LocalizationInvariants.Run();
    RemoteInvariants.Run();
    RelayClientInvariants.Run();
    WatchInvariants.Run();
    PaceInvariants.Run();
    ReadinessInvariants.Run();
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
    ProjectScanPageInvariants.Run();
    ProjectScanServiceInvariants.Run();
    ProjectScanConcurrencyInvariants.Run();
    ProjectThumbnailPageInvariants.Run();
    ProjectThumbnailServiceInvariants.Run();
    AppHostWindowInvariants.Run();
    AppWindowControllerInvariants.Run();
    ViewerOpeningInvariants.Run();
    TrayInvariants.Run();
    RemoteLifecycleInvariants.Run();
    LoadLifecycleInvariants.Run();
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

                /* Dieselben Woerterbuecher wie die Anwendung, in derselben Reihenfolge.
                 *
                 * Frueher stand hier nur die Sprachdatei. Dass Theme.xaml trotzdem zur
                 * Verfuegung stand, war eine NEBENWIRKUNG: ViewerPlaybackInvariants
                 * haengt es ein, und es laeuft zufaellig vorher. Damit entschied die
                 * Reihenfolge der Tests darueber, ob eine Ansicht ueberhaupt laedt -
                 * und als die Projektseite von Theme- auf Desktop-Marken umzog, fiel
                 * sie um, obwohl an ihr nichts falsch war.
                 *
                 * Die Reihenfolge zaehlt: DashboardTokens ueberschreibt Farben aus
                 * DesktopTheme, genau wie in App.xaml.
                 *
                 * Ueber pack-Adressen, nicht als lose Datei: Einzeln eingelesen sieht
                 * ein Woerterbuch die vorher geladenen NICHT, weil StaticResource beim
                 * Einlesen aufloest und der Leser nur das eine Dokument kennt.
                 * DashboardTokens greift aber auf DashFocus aus Theme.xaml zu und
                 * faellt sofort um. Die pack-Adresse nennt die Assembly ausdruecklich,
                 * damit sie auch aus einem Testlaeufer heraus aufloest - der Weg, den
                 * TrayInvariants schon benutzt. */
                foreach (string sheet in new[]
                         {
                             "Views/Theme.xaml",
                             "Views/DesktopTheme.xaml",
                             "Views/DashboardTokens.xaml",
                             "Localization/Strings.de.xaml",
                         })
                {
                    app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
                    {
                        Source = new Uri("/FrameFlip;component/" + sheet, UriKind.Relative),
                    });
                }

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
