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
    BridgeInvariants.Run();
    LocalizationInvariants.Run();
    RemoteInvariants.Run();
    MachineInvariants.Run();
    SettingsInvariants.Run();
    PreviewInvariants.Run();
    PlacementRegression.Run();

    return Check.Report();
}
