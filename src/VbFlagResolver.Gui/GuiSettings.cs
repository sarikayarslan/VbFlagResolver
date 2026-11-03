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
    /// <summary>Projede kullanılmayan formlar çıktıdaki .vbp'den çıkarılsın.</summary>
    public bool RemoveUnusedForms { get; set; }
    /// <summary>Salt okunur referans klasörleri (ör. Lib), ';' ile ayrılmış.</summary>
    public string ReferenceDirs { get; set; } = "";
    /// <summary>Dokunulmayacak bayraklar ve #If sabitleri (virgülle).</summary>
    public string SkipFlags { get; set; } = "";
    public string SkipDefines { get; set; } = "";
    /// <summary>Kullanıcının verdiği derleme sabitleri ("DEBUG_MODE=0, X=1").</summary>
    public string Defines { get; set; } = "";
    public bool ResolveDirectives { get; set; } = true;
    /// <summary>Tanımsız #If sabitleri 0 sayılsın (VB davranışı).</summary>
    public bool UndefinedConstantsAreZero { get; set; } = true;
    /// <summary>Kaynak klasör (tam yol) → temizlenecek (işaretli) dosyaların göreli yolları; diğerleri olduğu gibi aktarılır.</summary>
    public Dictionary<string, List<string>> CleanFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VbFlagResolver", "gui-settings.json");

    public static GuiSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<GuiSettings>(File.ReadAllText(FilePath)) is { } s)
            {
                // JSON'dan gelen sözlük büyük/küçük harf duyarlıdır; Windows yolları için duyarsız olanla değiştir
                s.CleanFiles = new Dictionary<string, List<string>>(s.CleanFiles ?? [], StringComparer.OrdinalIgnoreCase);
                return s;
            }
        }
        catch
        {
            // bozuk ayar dosyası: varsayılanlarla devam
        }
        return new GuiSettings();
    }

    /// <summary>Bu kaynak klasör için temizlenecek dosyalar (yoksa boş liste oluşturur).</summary>
    public List<string> CleanFor(string sourceDir)
    {
        string key = Path.GetFullPath(sourceDir).TrimEnd('\\', '/');
        if (!CleanFiles.TryGetValue(key, out var list)) CleanFiles[key] = list = [];
        return list;
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
