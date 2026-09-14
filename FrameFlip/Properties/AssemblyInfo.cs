using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FrameFlip.Tests")]

// Der Pruefstand der Zuschauerseite unter web/pruefstand/. Er fuehrt WatchService
// gegen einen echten Leuchtturm und braucht dafuer denselben internen Eingang wie
// die Testreihe - etwa um die Wartezeit vor dem Schliessen eines Platzes von
// dreissig Sekunden auf drei zu setzen.
[assembly: InternalsVisibleTo("watchhost")]
