namespace VbFlagResolver.Gui;

internal static class Program
{
    /// <summary>
    /// Komut satırından alanlar önceden doldurulabilir:
    /// VbFlagResolver.Gui.exe --flags x.bas --src klasör [--out klasör] [--preview]
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args));
    }
}
