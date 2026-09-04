using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FrameFlip.Interop;

/// <summary>Was der Explorer gerade zeigt: idealerweise eine selektierte Datei, mindestens der Ordner.</summary>
public sealed record ExplorerTarget(string? FilePath, string? FolderPath, IntPtr WindowHandle)
{
    public bool HasAnything => FilePath is not null || FolderPath is not null;
}

/// <summary>
/// Liest die Auswahl aus dem aktiven Explorer-Fenster ueber Shell.Application / ShellWindows.
/// Late-bound per IDispatch, damit kein Interop-Assembly und keine SHDocVw-Referenz noetig ist.
/// Muss auf einem STA-Thread laufen - wird ausschliesslich vom UI-Thread aufgerufen.
/// </summary>
public static class ExplorerSelectionProvider
{
    private const BindingFlags Call = BindingFlags.InvokeMethod | BindingFlags.GetProperty;

    public static ExplorerTarget? Resolve()
    {
        var comObjects = new List<object>();
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return null;

            var shell = Activator.CreateInstance(shellType);
            if (shell is null) return null;
            comObjects.Add(shell);

            var windows = Invoke(shell, "Windows");
            if (windows is null) return null;
            comObjects.Add(windows);

            int count = Convert.ToInt32(Invoke(windows, "Count") ?? 0);
            IntPtr foreground = NativeMethods.GetForegroundWindow();

            object? fallbackWindow = null;
            IntPtr fallbackHandle = IntPtr.Zero;

            for (int i = 0; i < count; i++)
            {
                object? window = Invoke(windows, "Item", i);
                if (window is null) continue;
                comObjects.Add(window);

                // ShellWindows enthaelt auch Internet Explorer / Edge-Legacy-Fenster.
                var fullName = Invoke(window, "FullName") as string;
                if (fullName is null || !fullName.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                IntPtr handle;
                try { handle = new IntPtr(Convert.ToInt64(Invoke(window, "HWND"))); }
                catch (Exception) { continue; }

                if (handle == foreground)
                {
                    var target = Read(window, handle, comObjects);
                    if (target is not null) return target;
                }

                if (fallbackWindow is null)
                {
                    fallbackWindow = window;
                    fallbackHandle = handle;
                }
            }

            // Der Explorer war nicht im Vordergrund (z.B. Fokus lag auf Blender): erstes Fenster nehmen.
            if (fallbackWindow is not null)
                return Read(fallbackWindow, fallbackHandle, comObjects);

            return null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            foreach (var o in comObjects)
            {
                try { if (Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }
                catch (Exception) { /* egal */ }
            }
        }
    }

    private static ExplorerTarget? Read(object window, IntPtr handle, List<object> comObjects)
    {
        try
        {
            object? document = Invoke(window, "Document");
            if (document is null) return null;
            comObjects.Add(document);

            string? file = null;
            try
            {
                object? selection = Invoke(document, "SelectedItems");
                if (selection is not null)
                {
                    comObjects.Add(selection);
                    int selected = Convert.ToInt32(Invoke(selection, "Count") ?? 0);
                    if (selected > 0)
                    {
                        object? first = Invoke(selection, "Item", 0);
                        if (first is not null)
                        {
                            comObjects.Add(first);
                            var path = Invoke(first, "Path") as string;
                            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                                file = path;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Manche Shell-Views (Bibliotheken, Suchergebnisse) liefern hier keine Auswahl.
            }

            string? folder = null;
            try
            {
                object? folderObj = Invoke(document, "Folder");
                if (folderObj is not null)
                {
                    comObjects.Add(folderObj);
                    object? self = Invoke(folderObj, "Self");
                    if (self is not null)
                    {
                        comObjects.Add(self);
                        var path = Invoke(self, "Path") as string;
                        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                            folder = path;
                    }
                }
            }
            catch (Exception)
            {
                // Fallback bleibt null; der Aufrufer meldet das dem Nutzer.
            }

            if (file is null && folder is null) return null;
            if (folder is null && file is not null) folder = Path.GetDirectoryName(file);

            return new ExplorerTarget(file, folder, handle);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static object? Invoke(object target, string member, params object?[] args)
        => target.GetType().InvokeMember(member, Call, null, target, args);
}
