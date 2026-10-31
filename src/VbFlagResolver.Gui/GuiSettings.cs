using System.Text.Json;

namespace VbFlagResolver.Gui;

/// <summary>Son kullanılan yollar ve seçenekler (%APPDATA%\VbFlagResolver\gui-settings.json).</summary>
internal sealed class GuiSettings
{
    public string FlagsPath { get; set; } = "";
    public string SourceDir { get; set; } = "";
    public string OutputDir { get; set; } = "";
    public int Mode { get; set; }
    public string InitProcedures { get; set; } = "";
    public string Extensions { get; set; } = ".frm,.bas,.cls,.ctl,.dob,.pag";
    public int CodePage { get; set; } = 1254;
    public bool UnassignedUnknown { get; set; }
    public bool CopyOtherFiles { get; set; } = true;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VbFlagResolver", "gui-settings.json");

    public static GuiSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<GuiSettings>(File.ReadAllText(FilePath)) ?? new GuiSettings();
        }
        catch
        {
            // bozuk ayar dosyası: varsayılanlarla devam
        }
        return new GuiSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // ayar kaydedilemezse sessizce geç
        }
    }
}
