using System.Diagnostics;
using System.Text;
using VbFlagResolver.Core;

namespace VbFlagResolver.Gui;

/// <summary>Ana pencere: girdiler, önizleme (kuru çalıştırma), dönüştürme ve sonuç sekmeleri.</summary>
internal sealed class MainForm : Form
{
    private readonly GuiSettings _settings = GuiSettings.Load();
    private readonly bool _autoPreview;
    private Report? _report;
    private bool _lastWasDryRun;

    // girdiler
    private readonly TextBox _txtFlags = new() { Dock = DockStyle.Fill };
    private readonly TextBox _txtSrc = new() { Dock = DockStyle.Fill };
    private readonly TextBox _txtOut = new() { Dock = DockStyle.Fill };
    /// <summary>İsteğe bağlı salt okunur referans klasörleri (ör. Lib), ';' ile ayrılmış.</summary>
    private readonly TextBox _txtRefDirs = new() { Dock = DockStyle.Fill, PlaceholderText = "İsteğe bağlı: ortak Lib gibi, kaynak dışındaki klasörler (; ile ayırın). Yalnızca form adları aranır, dosyalara dokunulmaz." };
    private readonly ComboBox _cmbMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly TextBox _txtInit = new() { Width = 200, PlaceholderText = "tüm yordamlar (sıra bilinmiyor)" };
    private readonly TextBox _txtExt = new() { Width = 220 };
    private readonly NumericUpDown _numCodePage = new() { Minimum = 37, Maximum = 65001, Width = 70 };
    // hiç değer atanmamış Boolean değişken bayrak: False mı, Bilinmiyor mu?
    private readonly RadioButton _rbUnassignedFalse = new() { Text = "False say (VB'nin başlangıç değeri) — bağlı bloklar sadeleşir", AutoSize = true, Checked = true };
    private readonly RadioButton _rbUnassignedUnknown = new() { Text = "Bilinmiyor say — bağlı bloklara dokunulmaz", AutoSize = true };
    private readonly CheckBox _chkCopy = new() { Text = "Diğer dosyaları (.frx, .vbp...) kopyala", AutoSize = true };
    private readonly CheckBox _chkDirectives = new() { Text = "#If bloklarını çöz", AutoSize = true, Checked = true };
    private readonly CheckBox _chkRemoveUnusedForms = new() { Text = "Kullanılmayan formları projeden çıkar", AutoSize = true };
    private readonly CheckBox _chkUndefinedZero = new() { Text = "Tanımsız sabit = 0", AutoSize = true, Checked = true };
    private readonly TextBox _txtDefines = new() { Width = 300, PlaceholderText = ".vbp'deki CondComp kullanılır; değiştirmek için ör. DEBUG_MODE=0" };
    /// <summary>Bayrak dosyası okunurken kullanılan derleme sabitleri (önizlemeden bağımsız görünüm için).</summary>
    private CondConstants? _flagConsts;

    // komutlar
    private readonly Button _btnPreview = new() { Text = "Önizle (kuru çalıştırma)  F5", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
    private readonly Button _btnRun = new() { Text = "Dönüştür ve çıktıya yaz", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
    private readonly Button _btnSaveReport = new() { Text = "Raporu kaydet...", AutoSize = true, Enabled = false };
    private readonly Button _btnOpenOut = new() { Text = "Çıktı klasörünü aç", AutoSize = true, Enabled = false };
    private readonly Button _btnClear = new() { Text = "Temizle", AutoSize = true };
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, Width = 120, Visible = false, MarqueeAnimationSpeed = 30 };

    // sonuçlar
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly ListView _lvFiles = NewListView();
    private readonly RichTextBox _rtbDiff = new() { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, BackColor = SystemColors.Window, BorderStyle = BorderStyle.None };
    private readonly CheckBox _chkAllLines = new() { Text = "Tüm satırları göster", AutoSize = true };
    private readonly Label _lblDiffTitle = new() { AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(0, 4, 12, 0) };
    private readonly ListView _lvDetails = NewListView();
    private readonly ListView _lvFlags = NewListView();
    private readonly TextBox _txtFlagFilter = new() { Width = 220, PlaceholderText = "Bayrak ara..." };
    private readonly ComboBox _cmbFlagValue = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly Label _lblFlagSummary = new() { AutoSize = true, Padding = new Padding(8, 4, 0, 0) };
    private readonly ListView _lvUnrecognized = NewListView();
    private readonly TextBox _txtFlagDetail = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
        Font = new Font("Consolas", 9f), BackColor = SystemColors.Window, BorderStyle = BorderStyle.None,
    };
    private readonly TabControl _tabFlagInfo = new() { Dock = DockStyle.Fill };
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 20000 };
    /// <summary>Projede bulunan çağrı sırasını "Çağrı sırası" alanına uygular.</summary>
    private readonly Button _btnUseOrder = new() { AutoSize = true, Visible = false, BackColor = Color.LightYellow };
    private readonly TextBox _txtProcedures = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = true,
        Font = new Font("Consolas", 9f), BackColor = SystemColors.Window, BorderStyle = BorderStyle.None,
    };
    /// <summary>Önizlemeden bağımsız, bayrak dosyası seçilir seçilmez yapılan okuma.</summary>
    private FlagReadResult? _flagRead;
    /// <summary>Son önizleme/dönüştürme raporu güncel girdilerle mi üretildi?</summary>
    private bool _reportIsCurrent;
    private readonly System.Windows.Forms.Timer _flagReadTimer = new() { Interval = 400 };
    private bool _fillingFiles;
    private readonly Button _btnSelectChanged = new() { Text = "Değişenlerin hepsini seç", AutoSize = true };
    private readonly Button _btnSelectNone = new() { Text = "Seçimi kaldır", AutoSize = true };
    private readonly Label _lblSelection = new() { AutoSize = true, Padding = new Padding(8, 6, 0, 0), ForeColor = Color.DimGray };
    /// <summary>Önizleme satırına git: ayrıntı satırının hedeflediği orijinal satır (tüm satırlar açılınca kaydırılır).</summary>
    private int? _pendingDiffLine;
    private List<int> _diffLineMap = [];
    /// <summary>
    /// Dosya listesinde son fare tıklaması/boşluk tuşu zamanı. ListView, öğeler eklenirken veya tutamaç yeniden
    /// oluşturulurken de ItemChecked gönderir; yalnızca kullanıcı girdisinin hemen ardından gelen işaret değişikliği sayılır.
    /// </summary>
    private DateTime _lastFileListInput = DateTime.MinValue;
    private readonly TextBox _txtReport = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
        Font = new Font("Consolas", 9f), BackColor = SystemColors.Window,
    };
    private readonly ToolStripStatusLabel _lblStatus = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

    public MainForm(string[] args)
    {
        Text = "VbFlagResolver - VB5 geliştirme bayrağı çözümleyici";
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1250, 820);
        MinimumSize = new Size(900, 600);
        KeyPreview = true;

        _cmbMode.Items.AddRange(["Otomatik (sabit + değişken)", "Yalnızca sabitler", "Yalnızca değişkenler"]);
        _cmbFlagValue.Items.AddRange(["Tüm değerler", "True", "False", "Bilinmiyor", "Kullanılmayan"]);
        _cmbFlagValue.SelectedIndex = 0;

        BuildLayout();
        _toolTip.SetToolTip(_txtInit,
            "Bayraklara değer veren yordamlar, exe açılırken çağrıldıkları sırayla, virgülle.\n" +
            "Ör. XFonksiyonu,InitFlags → önce XFonksiyonu, sonra InitFlags çalışmış sayılır; aynı bayrakta son atama geçerli.\n" +
            "Boş bırakılırsa tüm yordamlar okunur; birbirini çağırmayan yordamlar aynı bayrağa farklı değer veriyorsa bayrak Bilinmiyor olur.\n" +
            "Yordamların projede nerede çağrıldığı Bayraklar > Yordamlar ve notlar bölümünde görünür (Önizle'den sonra).");
        const string unassignedHelp =
            "Bayrak modülünde 'Public gbX As Boolean' diye tanımlanıp hiçbir yordamda değer verilmeyen değişkenler içindir.\n" +
            "Sabitleri (Const) ve değer atanmış değişkenleri etkilemez.\n" +
            "Başka dosyada 'gbX = ...' ataması olan bayraklar bu seçimden bağımsız olarak her zaman Bilinmiyor sayılır.";
        _toolTip.SetToolTip(_rbUnassignedFalse, unassignedHelp + "\n\nFalse: VB'de Boolean değişken False ile başlar; 'If gbX Then' blokları silinir, Else kısmı kalır.");
        _toolTip.SetToolTip(_rbUnassignedUnknown, unassignedHelp + "\n\nBilinmiyor: değerin başka bir yoldan (ör. ByRef parametre) verildiğinden şüpheleniyorsanız seçin; bu bayraklara bağlı bloklar olduğu gibi kalır.");
        _toolTip.SetToolTip(_chkDirectives,
            "İşaretli: #If / #ElseIf / #Else / #End If grupları derleme sabitlerine göre çözülür; VB'nin derleyeceği dal kalır,\n" +
            "diğer dallar ve #If satırları silinir. Değeri bilinmeyen sabit içeren grup olduğu gibi bırakılır ve raporlanır.\n" +
            "Boş: #If'lere dokunulmaz; bayrak dosyasında #If içindeki tanım/atamalar Bilinmiyor sayılır.");
        _toolTip.SetToolTip(_txtRefDirs,
            "İsteğe bağlı. Projenin kullandığı ama kaynak klasör dışındaki kod klasörleri (ör. ortak Lib); birden çok ise ; ile ayırın.\n" +
            "'Kullanılmayan formları projeden çıkar' açıkken form adları bu klasörlerdeki .bas/.frm/.cls/... dosyalarında da aranır;\n" +
            "adı geçen form projeden çıkarılmaz. Bu dosyalar yalnızca okunur: asla değiştirilmez, kopyalanmaz, çıktı buraya yazılamaz.");
        _toolTip.SetToolTip(_chkRemoveUnusedForms,
            "İşaretli: projede hiçbir yerde kullanılmayan formlar (yalnızca temizlenen bayrak bloklarından çağrılanlar dahil) çıktıdaki .vbp'den çıkarılır, .frm/.frx çıktıya kopyalanmaz.\n" +
            "Başlangıç formu, adı string içinde geçen formlar ve kullanılan MDI alt formlarının ana formu korunur.\n" +
            "Seçilmeyen (temizlenmeyecek) dosyalardaki kullanımlar da sayılır. Önizlemede 'Projeden çıkarılacak' olarak görünür.");
        _toolTip.SetToolTip(_chkUndefinedZero,
            "İşaretli (varsayılan): .vbp'de, Derleme sabitleri alanında veya #Const ile hiç tanımlanmamış bir sabit VB'deki gibi 0 (False)\n" +
            "sayılır; ör. #If ARSLAN = 1 dalı silinir, #Else kalır. Raporda 'tanımsız sabit 0 sayıldı' olarak görünür.\n" +
            "Boş: tanımsız sabit içeren #If grubu olduğu gibi bırakılır (sabit listesinin eksik olabileceği durumlar için).");
        _toolTip.SetToolTip(_txtDefines,
            "Boş bırakılırsa kaynak klasördeki .vbp dosyasının CondComp satırı kullanılır (VB'deki Proje Özellikleri > Make > Conditional Compilation).\n" +
            "Farklı bir derleme için değer verin, ör. DEBUG_MODE=0, YENI_EKRAN=1 — .vbp değerlerini ezer.\n" +
            "Dosya içindeki #Const satırları o dosyada yine geçerlidir (VB'deki gibi).\n" +
            "Kullanılan değerler Bayraklar > Yordamlar ve notlar bölümünde ve raporda görünür.");
        LoadSettings();
        _autoPreview = ApplyArgs(args);
        SuggestFromSource();
        WireEvents();
        UpdateButtons();
        _lblStatus.Text = "Bayrak dosyasını ve kaynak klasörü seçip önce Önizle'ye basın. Orijinal dosyalara asla yazılmaz.";
    }

    // ------------------------------------------------------------------ yerleşim

    /// <summary>Sütun genişliği 96 DPI için verilir; ekran ölçeğine göre büyütülür.</summary>
    private void AddColumn(ListView lv, string text, int width, HorizontalAlignment align = HorizontalAlignment.Left)
        => lv.Columns.Add(text, LogicalToDeviceUnits(width), align);

    private static ListView NewListView() => new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true,
        HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.None,
    };

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // yollar
        var paths = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddPathRow(paths, 0, "Bayrak dosyası:", _txtFlags, BrowseFlags);
        AddPathRow(paths, 1, "Kaynak klasör:", _txtSrc, () => BrowseFolder(_txtSrc, "VB5 proje klasörünü seçin"));
        AddPathRow(paths, 2, "Çıktı klasörü:", _txtOut, () => BrowseFolder(_txtOut, "Çıktının yazılacağı klasörü seçin (kaynak klasör olamaz)"));
        AddPathRow(paths, 3, "Referans klasörleri:", _txtRefDirs, BrowseRefDir);
        var grpPaths = new GroupBox { Text = "Girdiler", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 6) };
        grpPaths.Controls.Add(paths);
        root.Controls.Add(grpPaths, 0, 0);

        // seçenekler
        var line1 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        line1.Controls.AddRange([
            Lbl("Okuma biçimi:"), _cmbMode,
            Lbl("Çağrı sırası:"), _txtInit,
            Lbl("Uzantılar:"), _txtExt,
            Lbl("Kod sayfası:"), _numCodePage,
        ]);
        // radyo düğmeleri aynı kapsayıcıda olmalı ki birbirini dışlasın
        var line2 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        line2.Controls.AddRange([Lbl("Hiç değer atanmamış Boolean değişken:"), _rbUnassignedFalse, _rbUnassignedUnknown]);
        var line3 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(8, 0, 0, 0) };
        line3.Controls.AddRange([_chkDirectives, Lbl("Derleme sabitleri:"), _txtDefines, _chkUndefinedZero, Spacer(16), _chkCopy, _chkRemoveUnusedForms]);
        var opts = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        opts.Controls.Add(line1, 0, 0);
        opts.Controls.Add(line2, 0, 1);
        opts.Controls.Add(line3, 0, 2);
        var grpOpts = new GroupBox { Text = "Seçenekler", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        grpOpts.Controls.Add(opts);
        root.Controls.Add(grpOpts, 0, 1);

        // komutlar
        var cmds = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 6, 0, 6) };
        _btnPreview.Font = new Font(Font, FontStyle.Bold);
        cmds.Controls.AddRange([_btnPreview, _btnRun, Spacer(16), _btnSaveReport, _btnOpenOut, Spacer(16), _btnClear, Spacer(16), _progress]);
        root.Controls.Add(cmds, 0, 2);

        // sekmeler
        _tabs.TabPages.Add(BuildFilesTab());
        _tabs.TabPages.Add(BuildFlagsTab());
        _tabs.TabPages.Add(BuildReportTab());
        root.Controls.Add(_tabs, 0, 3);

        var status = new StatusStrip();
        status.Items.Add(_lblStatus);

        Controls.Add(root);
        Controls.Add(status);
        AcceptButton = _btnPreview;
    }

    private static Label Lbl(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(0, 6, 0, 0), Margin = new Padding(8, 0, 0, 0) };

    private static Control Spacer(int w) => new Panel { Width = w, Height = 1 };

    private static void AddPathRow(TableLayoutPanel t, int row, string label, TextBox box, Action browse)
    {
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 4, 4, 0) }, 0, row);
        box.AllowDrop = true;
        box.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        box.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) box.Text = files[0];
        };
        t.Controls.Add(box, 1, row);
        var btn = new Button { Text = "Gözat...", AutoSize = true };
        btn.Click += (_, _) => browse();
        t.Controls.Add(btn, 2, row);
    }

    private TabPage BuildFilesTab()
    {
        _lvFiles.CheckBoxes = true; // yalnızca işaretli dosyalar temizlenir; diğerleri olduğu gibi aktarılır
        AddColumn(_lvFiles, "☑ = temizle", 185);
        AddColumn(_lvFiles, "Durum", 75);
        AddColumn(_lvFiles, "Silinen", 52, HorizontalAlignment.Right);
        AddColumn(_lvFiles, "Açılan", 50, HorizontalAlignment.Right);
        AddColumn(_lvFiles, "Sadeleşen", 66, HorizontalAlignment.Right);
        AddColumn(_lvFiles, "Tek satır", 60, HorizontalAlignment.Right);
        AddColumn(_lvFiles, "Satır −", 52, HorizontalAlignment.Right);
        AddColumn(_lvFiles, "Uyarı", 46, HorizontalAlignment.Right);
        AddColumn(_lvFiles, "#If", 40, HorizontalAlignment.Right);

        AddColumn(_lvDetails, "Satır", 60, HorizontalAlignment.Right);
        AddColumn(_lvDetails, "Tür", 110);
        AddColumn(_lvDetails, "Açıklama", 900);

        var diffBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
        diffBar.Controls.AddRange([_lblDiffTitle, _chkAllLines]);
        var legend = new Label
        {
            AutoSize = true, Padding = new Padding(16, 4, 0, 0), ForeColor = Color.DimGray,
            Text = "Sütunlar: orijinal satır no | yeni satır no.   − silinen   + eklenen",
        };
        diffBar.Controls.Add(legend);

        var diffPanel = new Panel { Dock = DockStyle.Fill };
        diffPanel.Controls.Add(_rtbDiff);
        diffPanel.Controls.Add(diffBar);

        var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        right.Panel1.Controls.Add(diffPanel);
        right.Panel2.Controls.Add(_lvDetails);

        var selectBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(2) };
        selectBar.Controls.AddRange([_btnSelectChanged, _btnSelectNone, _lblSelection]);
        var split = new SplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(_lvFiles);
        split.Panel1.Controls.Add(selectBar);
        split.Panel2.Controls.Add(right);
        _rtbDiff.HideSelection = false; // ayrıntıdan gidilen satır, odak listedeyken de vurgulu kalsın

        var page = new TabPage("Dosyalar ve önizleme");
        page.Controls.Add(split);
        // bölücü konumları pencere boyutlandıktan sonra verilir
        Shown += (_, _) => { split.SplitterDistance = Math.Min(LogicalToDeviceUnits(590), split.Width / 2); right.SplitterDistance = right.Height * 2 / 3; };
        return page;
    }

    private TabPage BuildFlagsTab()
    {
        AddColumn(_lvFlags, "Bayrak", 230);
        AddColumn(_lvFlags, "Değer", 75);
        AddColumn(_lvFlags, "Tanım biçimi", 210);
        AddColumn(_lvFlags, "Nasıl okundu", 420);
        AddColumn(_lvFlags, "Satır", 50, HorizontalAlignment.Right);
        AddColumn(_lvFlags, "Kullanıldığı dosya", 110, HorizontalAlignment.Right);

        AddColumn(_lvUnrecognized, "Satır", 50, HorizontalAlignment.Right);
        AddColumn(_lvUnrecognized, "Neden", 230);
        AddColumn(_lvUnrecognized, "Metin", 500);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        bar.Controls.AddRange([_txtFlagFilter, _cmbFlagValue, _btnUseOrder, _lblFlagSummary]);

        var grpDetail = new GroupBox { Text = "Seçilen bayrak", Dock = DockStyle.Fill };
        grpDetail.Controls.Add(_txtFlagDetail);
        var pageUnrec = new TabPage("Tanınmayan satırlar");
        pageUnrec.Controls.Add(_lvUnrecognized);
        var pageProcs = new TabPage("Yordamlar ve notlar");
        pageProcs.Controls.Add(_txtProcedures);
        _tabFlagInfo.TabPages.Add(pageUnrec);
        _tabFlagInfo.TabPages.Add(pageProcs);
        var grpUnrec = _tabFlagInfo;

        var bottom = new SplitContainer { Dock = DockStyle.Fill };
        bottom.Panel1.Controls.Add(grpDetail);
        bottom.Panel2.Controls.Add(grpUnrec);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.Panel1.Controls.Add(_lvFlags);
        split.Panel2.Controls.Add(bottom);

        var page = new TabPage("Bayraklar");
        page.Controls.Add(split);
        page.Controls.Add(bar);
        // gizli sekme ilk gösterildiğinde boyutlanır
        page.Layout += (_, _) =>
        {
            if (page.Tag == null && split.Height > 200)
            {
                split.SplitterDistance = split.Height * 3 / 5;
                bottom.SplitterDistance = bottom.Width / 2;
                page.Tag = true;
            }
        };
        return page;
    }

    private TabPage BuildReportTab()
    {
        var page = new TabPage("Rapor");
        page.Controls.Add(_txtReport);
        // sekmeye geçince metnin tamamı seçili gelmesin
        foreach (var box in new[] { _txtReport, _txtProcedures, _txtFlagDetail })
            box.Enter += (_, _) => BeginInvoke(() => box.Select(0, 0));
        return page;
    }

    // ------------------------------------------------------------------ olaylar

    private void WireEvents()
    {
        _btnPreview.Click += async (_, _) => await RunAsync(dryRun: true);
        _btnRun.Click += async (_, _) => await RunAsync(dryRun: false);
        _btnSaveReport.Click += (_, _) => SaveReport();
        _btnOpenOut.Click += (_, _) => OpenFolder(_txtOut.Text);
        _btnClear.Click += (_, _) => ClearAll();
        _lvFiles.SelectedIndexChanged += (_, _) => ShowSelectedFile();
        bool doubleClick = false;
        _lvFiles.MouseDown += (_, e) => { _lastFileListInput = DateTime.UtcNow; doubleClick = e.Clicks > 1; };
        _lvFiles.MouseUp += (_, _) => doubleClick = false;
        // ListView çift tıklamada işareti değiştirir; dosyaya bakmak için çift tıklayan kullanıcı yanlışlıkla işaretlemesin
        // (programın kendi yaptığı toplu seçim etkilenmesin)
        _lvFiles.ItemCheck += (_, e) => { if (doubleClick && !_fillingFiles) e.NewValue = e.CurrentValue; };
        _lvFiles.KeyDown += (_, e) => { if (e.KeyCode == Keys.Space) _lastFileListInput = DateTime.UtcNow; };
        _lvFiles.ItemChecked += (_, e) => ToggleClean(e.Item);
        _btnSelectChanged.Click += (_, _) => SetSelection(f => f.Changed);
        _btnSelectNone.Click += (_, _) => SetSelection(_ => false);
        _lvDetails.SelectedIndexChanged += (_, _) => JumpToDetail();
        _chkAllLines.CheckedChanged += (_, _) => ShowSelectedFile();
        _txtFlagFilter.TextChanged += (_, _) => FillFlags();
        _cmbFlagValue.SelectedIndexChanged += (_, _) => FillFlags();
        _lvFlags.SelectedIndexChanged += (_, _) => ShowSelectedFlag();
        _btnUseOrder.Click += async (_, _) =>
        {
            _txtInit.Text = (string)_btnUseOrder.Tag!;
            _btnUseOrder.Visible = false;
            if (_btnPreview.Enabled) await RunAsync(dryRun: true);
            _tabs.SelectedIndex = 1;
        };
        _txtSrc.Leave += (_, _) => SuggestFromSource();
        foreach (var tb in new[] { _txtFlags, _txtSrc, _txtOut }) tb.TextChanged += (_, _) => UpdateButtons();

        // bayrak dosyası seçilince hemen oku; okuma seçenekleri değişince yeniden oku
        bool flagFileChanged = false;
        _txtFlags.TextChanged += (_, _) => { flagFileChanged = true; ScheduleFlagRead(); };
        _txtSrc.TextChanged += (_, _) => _reportIsCurrent = false;
        _cmbMode.SelectedIndexChanged += (_, _) => ScheduleFlagRead();
        _txtInit.TextChanged += (_, _) => ScheduleFlagRead();
        _rbUnassignedUnknown.CheckedChanged += (_, _) => ScheduleFlagRead(); // iki yönde de tetiklenir
        _numCodePage.ValueChanged += (_, _) => ScheduleFlagRead();
        _txtDefines.TextChanged += (_, _) => ScheduleFlagRead();
        _chkDirectives.CheckedChanged += (_, _) =>
        {
            _txtDefines.Enabled = _chkUndefinedZero.Enabled = _chkDirectives.Checked;
            ScheduleFlagRead();
        };
        _chkUndefinedZero.CheckedChanged += (_, _) => ScheduleFlagRead();
        _chkRemoveUnusedForms.CheckedChanged += (_, _) => _reportIsCurrent = false;
        _txtRefDirs.TextChanged += (_, _) => _reportIsCurrent = false;
        _txtSrc.TextChanged += (_, _) => { if (File.Exists(_txtFlags.Text.Trim())) ScheduleFlagRead(); }; // .vbp sabitleri kaynaktan gelir
        _flagReadTimer.Tick += (_, _) =>
        {
            ReadFlagsNow(showTab: flagFileChanged);
            flagFileChanged = false;
        };
        KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.F5 && _btnPreview.Enabled) { e.Handled = true; await RunAsync(dryRun: true); }
        };
        Shown += async (_, _) =>
        {
            ReadFlagsNow(showTab: false);
            if (_autoPreview && _btnPreview.Enabled) await RunAsync(dryRun: true);
        };
        FormClosing += (_, _) => SaveSettings();
    }

    private void UpdateButtons()
    {
        bool ok = _txtFlags.Text.Trim().Length > 0 && _txtSrc.Text.Trim().Length > 0;
        _btnPreview.Enabled = ok && !_progress.Visible;
        _btnRun.Enabled = ok && _txtOut.Text.Trim().Length > 0 && !_progress.Visible;
        _btnSaveReport.Enabled = _report != null && !_progress.Visible;
        _btnOpenOut.Enabled = !_lastWasDryRun && _report != null && Directory.Exists(_txtOut.Text.Trim());
        _btnClear.Enabled = !_progress.Visible;
    }

    private void BrowseFlags()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Bayrak modülünü seçin",
            Filter = "VB modülü (*.bas)|*.bas|Tüm dosyalar (*.*)|*.*",
        };
        if (File.Exists(_txtFlags.Text)) dlg.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(_txtFlags.Text));
        else if (Directory.Exists(_txtSrc.Text)) dlg.InitialDirectory = _txtSrc.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK) _txtFlags.Text = dlg.FileName;
    }

    /// <summary>Seçilen klasörü referans listesine ekler (zaten varsa eklemez).</summary>
    private void BrowseRefDir()
    {
        using var dlg = new FolderBrowserDialog { Description = "Referans klasörü seçin (ör. ortak Lib; salt okunur)", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var list = RefDirs();
        if (!list.Contains(dlg.SelectedPath, StringComparer.OrdinalIgnoreCase)) list.Add(dlg.SelectedPath);
        _txtRefDirs.Text = string.Join("; ", list);
    }

    private List<string> RefDirs()
        => _txtRefDirs.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private void BrowseFolder(TextBox box, string description)
    {
        using var dlg = new FolderBrowserDialog { Description = description, UseDescriptionForTitle = true, ShowNewFolderButton = true };
        if (Directory.Exists(box.Text)) dlg.InitialDirectory = box.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            box.Text = dlg.SelectedPath;
            if (box == _txtSrc) SuggestFromSource();
        }
    }

    /// <summary>Kaynak seçilince boş alanlar için öneri: bayrak dosyası ve "_temiz" çıktı klasörü.</summary>
    private void SuggestFromSource()
    {
        var src = _txtSrc.Text.Trim();
        if (!Directory.Exists(src)) return;
        if (_txtOut.Text.Trim().Length == 0)
            _txtOut.Text = Path.GetFullPath(src).TrimEnd('\\', '/') + "_temiz";
        if (_txtFlags.Text.Trim().Length == 0)
        {
            try
            {
                var candidate = Directory.EnumerateFiles(src, "*.bas", SearchOption.AllDirectories)
                    .FirstOrDefault(f => Path.GetFileName(f).Contains("Flag", StringComparison.OrdinalIgnoreCase));
                if (candidate != null) _txtFlags.Text = candidate;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // ------------------------------------------------------------------ çalıştırma

    private ProcessOptions BuildOptions(bool dryRun)
    {
        var init = _txtInit.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var exts = _txtExt.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.StartsWith('.') ? e : "." + e).ToArray();
        return new ProcessOptions
        {
            FlagsPath = _txtFlags.Text.Trim(),
            SourceDir = _txtSrc.Text.Trim(),
            OutputDir = _txtOut.Text.Trim().Length > 0 ? _txtOut.Text.Trim() : null,
            DryRun = dryRun,
            CopyOtherFiles = _chkCopy.Checked,
            Defines = _txtDefines.Text.Trim().Length > 0 ? _txtDefines.Text.Trim() : null,
            ResolveDirectives = _chkDirectives.Checked,
            UndefinedConstantsAreZero = _chkUndefinedZero.Checked,
            CodePage = (int)_numCodePage.Value,
            Extensions = exts.Length > 0 ? exts : new ProcessOptions { FlagsPath = "", SourceDir = "" }.Extensions,
            // önizleme tüm dosyaları gösterir (neyin değişeceğine bakıp seçmek için); yazarken yalnızca seçililer temizlenir
            ProcessOnly = dryRun ? null : SelectedFiles(),
            RemoveUnusedForms = _chkRemoveUnusedForms.Checked,
            ReferenceDirs = RefDirs(),
            UsageSelection = dryRun ? SelectedFiles() : null,
            FlagOptions = new FlagReaderOptions
            {
                Mode = (FlagReadMode)Math.Max(0, _cmbMode.SelectedIndex),
                InitProcedures = init.Length > 0 ? init : null,
                UnassignedVariableIsFalse = _rbUnassignedFalse.Checked,
            },
        };
    }

    private async Task RunAsync(bool dryRun)
    {
        if (!File.Exists(_txtFlags.Text.Trim())) { Warn("Bayrak dosyası bulunamadı."); return; }
        if (!Directory.Exists(_txtSrc.Text.Trim())) { Warn("Kaynak klasör bulunamadı."); return; }
        if (!dryRun)
        {
            if (SelectedFiles().Count == 0
                && MessageBox.Show(this, "Temizlenecek dosya seçilmedi; tüm dosyalar olduğu gibi kopyalanacak.\n\n" +
                                         "Önizlemeden sonra temizlenecek dosyaların kutusunu işaretleyin (veya \"Değişenlerin hepsini seç\"). Yine de devam edilsin mi?",
                    "Onay", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            var outDir = _txtOut.Text.Trim();
            if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any()
                && MessageBox.Show(this, $"Çıktı klasörü boş değil:\n{outDir}\n\nAynı adlı dosyaların üzerine yazılacak. Devam edilsin mi?",
                    "Onay", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
        }

        SaveSettings();
        _flagReadTimer.Stop(); // önizleme bayrakları zaten güncel seçeneklerle okur
        var options = BuildOptions(dryRun);
        SetBusy(true, dryRun ? "Önizleme hazırlanıyor..." : "Dönüştürülüyor...");
        try
        {
            var report = await Task.Run(() => ProjectProcessor.Run(options, msg => BeginInvoke(() => _lblStatus.Text = msg.Trim())));
            _report = report;
            _reportIsCurrent = true;
            _lastWasDryRun = dryRun;
            Populate(report);
            int changed = report.Files.Count(f => f.Changed);
            _lblStatus.Text = (dryRun ? "Önizleme tamam (dosya yazılmadı): " : $"Yazıldı → {report.OutputDir}: ")
                              + $"{report.Flags.Count} bayrak, {report.Files.Count} kaynak dosya, {changed} dosya "
                              + (dryRun ? "değişebilir — temizlenecekleri işaretleyin, " : "temizlendi, ")
                              + $"{report.WarningCount} uyarı.";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Hata: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, null);
        }
        // önizleme sürerken seçim değiştiyse bir kez daha önizle; dönüştürme sonucunu ise önizlemeyle ezme
        if (_rerunPreviewWhenIdle)
        {
            _rerunPreviewWhenIdle = false;
            if (dryRun) await RunAsync(dryRun: true);
        }
    }

    /// <summary>Tüm alanları, seçenekleri ve sonuçları başlangıç durumuna döndürür. Diskteki hiçbir dosyaya dokunmaz.</summary>
    private void ClearAll()
    {
        _txtFlags.Text = _txtSrc.Text = _txtOut.Text = _txtInit.Text = _txtFlagFilter.Text = "";
        var defaults = new GuiSettings();
        _cmbMode.SelectedIndex = defaults.Mode;
        _txtExt.Text = defaults.Extensions;
        _numCodePage.Value = defaults.CodePage;
        SetUnassignedUnknown(defaults.UnassignedUnknown);
        _chkCopy.Checked = defaults.CopyOtherFiles;
        _chkRemoveUnusedForms.Checked = defaults.RemoveUnusedForms;
        _txtRefDirs.Text = defaults.ReferenceDirs;
        _txtDefines.Text = defaults.Defines;
        _chkDirectives.Checked = defaults.ResolveDirectives;
        _chkUndefinedZero.Checked = defaults.UndefinedConstantsAreZero;
        _flagConsts = null;
        _cmbFlagValue.SelectedIndex = 0;
        _chkAllLines.Checked = false;
        _flagReadTimer.Stop(); // alan değişiklikleri okuma zamanlayıcısını başlatmış olabilir

        _report = null;
        _flagRead = null;
        _reportIsCurrent = false;
        _lastWasDryRun = false;
        _lvFiles.Items.Clear();
        _lvDetails.Items.Clear();
        _rtbDiff.Clear();
        _lblDiffTitle.Text = "";
        _txtReport.Clear();
        _btnUseOrder.Visible = false;
        FillFlags();
        _tabs.TabPages[0].Text = "Dosyalar ve önizleme";
        _tabFlagInfo.TabPages[0].Text = "Tanınmayan satırlar";
        _tabFlagInfo.TabPages[1].Text = "Yordamlar ve notlar";
        _tabs.SelectedIndex = 0;
        UpdateButtons();
        _lblStatus.Text = "Temizlendi. Bayrak dosyasını ve kaynak klasörü seçip önce Önizle'ye basın. Orijinal dosyalara asla yazılmaz.";
        _txtFlags.Focus();
    }

    private void SetBusy(bool busy, string? message)
    {
        _progress.Visible = busy;
        UseWaitCursor = busy;
        if (message != null) _lblStatus.Text = message;
        UpdateButtons();
    }

    private void Warn(string message) => MessageBox.Show(this, message, "VbFlagResolver", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    // ------------------------------------------------------------------ sonuçlar

    private void Populate(Report report)
    {
        string? previous = _lvFiles.SelectedItems.Count > 0 ? ((FileReport)_lvFiles.SelectedItems[0].Tag!).RelativePath : null;
        _fillingFiles = true;
        _lvFiles.BeginUpdate();
        _lvFiles.Items.Clear();
        var ordered = report.Files
            .OrderByDescending(f => f.Failed).ThenByDescending(f => f.Changed || f.Warnings.Count > 0)
            .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
        foreach (var f in ordered)
        {
            var item = new ListViewItem([
                f.RelativePath, "",
                f.Count(ChangeKind.BlockRemoved).ToString(), f.Count(ChangeKind.BlockLifted).ToString(),
                f.Count(ChangeKind.BlockSimplified).ToString(), f.Count(ChangeKind.SingleLineIf).ToString(),
                f.RemovedLines.ToString(), f.Warnings.Count.ToString(), f.Count(ChangeKind.DirectiveResolved).ToString(),
            ]) { Tag = f, Checked = IsSelected(f.RelativePath) };
            if (f.Warnings.Count > 0) item.SubItems[7].ForeColor = Color.DarkOrange;
            item.UseItemStyleForSubItems = f.Warnings.Count == 0;
            StyleFileItem(item);
            _lvFiles.Items.Add(item);
        }
        _lvFiles.EndUpdate();
        _fillingFiles = false;

        FillFlags();
        _txtReport.Text = report.ToText().Replace("\r\n", "\n").Replace("\n", "\r\n");
        UpdateSelectionInfo();

        // yeniden önizlemede aynı dosya seçili kalsın
        var reselect = _lvFiles.Items.Cast<ListViewItem>().FirstOrDefault(i => ((FileReport)i.Tag!).RelativePath == previous);
        if (reselect != null) { reselect.Selected = true; reselect.EnsureVisible(); }
        else if (_lvFiles.Items.Count > 0) _lvFiles.Items[0].Selected = true;
        else ShowSelectedFile();
    }

    /// <summary>Durum metni ve renk: önizlemede değişebilen dosya seçiliyse temizlenir, değilse olduğu gibi aktarılır.</summary>
    private void StyleFileItem(ListViewItem item)
    {
        var f = (FileReport)item.Tag!;
        bool sel = item.Checked;
        if (f.RemovedFromProject)
        {
            item.SubItems[1].Text = _lastWasDryRun ? "Projeden çıkarılacak" : "Projeden çıkarıldı";
            item.Font = _lvFiles.Font;
            item.ForeColor = Color.MediumPurple;
            return;
        }
        item.SubItems[1].Text =
            f.Failed ? "HATA"
            : f.KeptAsIs ? "Olduğu gibi"
            : f.Changed ? (_lastWasDryRun ? (sel ? "Temizlenecek" : "Seçilmedi") : "Temizlendi")
            : f.Warnings.Count > 0 ? "Uyarı" : "Aynı";
        item.Font = f.Changed && sel ? new Font(_lvFiles.Font, FontStyle.Bold) : _lvFiles.Font;
        item.ForeColor = f.Failed ? Color.Firebrick
            : f.KeptAsIs ? Color.SteelBlue
            : f.Changed ? (sel ? SystemColors.WindowText : Color.DimGray)
            : f.Warnings.Count == 0 ? Color.Gray : SystemColors.WindowText;
    }

    private bool IsSelected(string rel)
        => Directory.Exists(_txtSrc.Text.Trim())
           && _settings.CleanFor(_txtSrc.Text.Trim()).Any(k => k.Equals(rel, StringComparison.OrdinalIgnoreCase));

    /// <summary>Temizlenecek dosyalar (bu kaynak klasör için kayıtlı seçim).</summary>
    /// <summary>
    /// Temizlenecek dosyalar (bu kaynak klasör için kayıtlı seçim), Core'a desen olarak verilecek biçimde: ".\" öneki
    /// tam göreli yol eşlemesi yapar; yoksa "modA.bas" alt klasördeki aynı adlı dosyaya da uyardı.
    /// </summary>
    private List<string> SelectedFiles()
        => Directory.Exists(_txtSrc.Text.Trim()) ? _settings.CleanFor(_txtSrc.Text.Trim()).Select(r => ".\\" + r).ToList() : [];

    private void UpdateSelectionInfo()
    {
        if (_report == null) { _lblSelection.Text = ""; return; }
        int changed = _report.Files.Count(f => f.Changed);
        int selected = _lvFiles.Items.Cast<ListViewItem>().Count(i => i.Checked);
        _lblSelection.Text = $"{selected} dosya temizlenecek; işaretsizler olduğu gibi aktarılır.";
        if (_report.RemovedForms.Count > 0)
            _lblSelection.Text += $" {_report.RemovedForms.Count} form projeden {(_lastWasDryRun ? "çıkarılacak" : "çıkarıldı")}.";
        _tabs.TabPages[0].Text = $"Dosyalar ve önizleme ({changed} değişebilir, {selected} seçili)";
    }

    /// <summary>Kullanıcının işaret değişikliğini bu kaynak klasör için kaydeder (önizlemeyi yeniden çalıştırmaz).</summary>
    private void ToggleClean(ListViewItem item)
    {
        if (_fillingFiles || item.Tag is not FileReport f || !Directory.Exists(_txtSrc.Text.Trim())) return;
        if ((DateTime.UtcNow - _lastFileListInput).TotalMilliseconds > 500)
        {
            // kullanıcı girdisi olmadan gelen olay: görünümü kayıtlı seçime geri çek
            bool saved = IsSelected(f.RelativePath);
            if (item.Checked != saved) BeginInvoke(() => { _fillingFiles = true; item.Checked = saved; _fillingFiles = false; });
            return;
        }
        SaveSelection(f.RelativePath, item.Checked);
        _settings.Save();
        StyleFileItem(item);
        UpdateSelectionInfo();
        if (item.Selected) ShowSelectedFile();
        RerunPreviewIfUsageDepends();
    }

    private void SaveSelection(string rel, bool selected)
    {
        var list = _settings.CleanFor(_txtSrc.Text.Trim());
        list.RemoveAll(k => k.Equals(rel, StringComparison.OrdinalIgnoreCase));
        if (selected) list.Add(rel);
    }

    /// <summary>Düğmelerle toplu seçim.</summary>
    private void SetSelection(Func<FileReport, bool> select)
    {
        if (!Directory.Exists(_txtSrc.Text.Trim())) return;
        _fillingFiles = true;
        foreach (ListViewItem item in _lvFiles.Items)
        {
            var f = (FileReport)item.Tag!;
            item.Checked = select(f);
            SaveSelection(f.RelativePath, item.Checked);
            StyleFileItem(item);
        }
        _fillingFiles = false;
        _settings.Save();
        UpdateSelectionInfo();
        ShowSelectedFile();
        RerunPreviewIfUsageDepends();
    }

    /// <summary>Seçim, kullanılmayan form analizini etkilediği için önizlemeyi yeniler.</summary>
    private void RerunPreviewIfUsageDepends()
    {
        if (!_chkRemoveUnusedForms.Checked || _report == null) return;
        _reportIsCurrent = false;
        if (_btnPreview.Enabled) _ = RunAsync(dryRun: true);
        else if (_progress.Visible) _rerunPreviewWhenIdle = true; // bir işlem sürüyor: bitince bir kez daha önizle
    }

    private bool _rerunPreviewWhenIdle;

    // ------------------------------------------------------------------ bayraklar

    /// <summary>Bayrak dosyası veya okuma seçenekleri değişince kısa bir gecikmeyle yeniden okur.</summary>
    private void ScheduleFlagRead()
    {
        _reportIsCurrent = false;
        _flagReadTimer.Stop();
        _flagReadTimer.Start();
    }

    /// <summary>Bayrak dosyasını önizlemeden bağımsız okur ve Bayraklar sekmesini doldurur.</summary>
    private void ReadFlagsNow(bool showTab)
    {
        _flagReadTimer.Stop();
        var path = _txtFlags.Text.Trim();
        if (!File.Exists(path))
        {
            _flagRead = null;
            FillFlags();
            return;
        }
        try
        {
            var po = BuildOptions(dryRun: true);
            var fo = po.FlagOptions;
            // #If için sabitler: kaynak klasördeki .vbp (seçildiyse) + kullanıcının verdikleri
            var src = _txtSrc.Text.Trim();
            var constNotes = new List<SourceNote>();
            _flagConsts = CondConstants.FromProject(Directory.Exists(src) ? src : null, po.Defines, constNotes, po.CodePage);
            _flagConsts.UndefinedIsZero = po.UndefinedConstantsAreZero;
            _flagRead = FlagReader.Read(path, new FlagReaderOptions
            {
                Mode = fo.Mode, InitProcedures = fo.InitProcedures,
                UnassignedVariableIsFalse = fo.UnassignedVariableIsFalse, CodePage = (int)_numCodePage.Value,
                Constants = _flagConsts, ResolveDirectives = po.ResolveDirectives,
            });
            _flagRead.Notes.InsertRange(0, constNotes);
        }
        catch (Exception ex)
        {
            _flagRead = null;
            _lblStatus.Text = "Bayrak dosyası okunamadı: " + ex.Message;
            FillFlags();
            return;
        }
        FillFlags();
        var all = _flagRead.Flags.All.ToList();
        _lblStatus.Text = $"{Path.GetFileName(path)}: {all.Count} bayrak okundu (True: {all.Count(f => f.Value == true)}, " +
                          $"False: {all.Count(f => f.Value == false)}, Bilinmiyor: {all.Count(f => f.Value == null)}), " +
                          $"{_flagRead.Unrecognized.Count} satır tanınmadı. Kullanım bilgisi ve formlardaki atamalar için Önizle'ye basın.";
        if (showTab) _tabs.SelectedIndex = 1;
    }

    /// <summary>Bayraklar sekmesinde gösterilen veri. Usage/Calls yalnızca önizlemeden sonra dolar.</summary>
    private sealed record FlagView(
        FlagTable Flags, IReadOnlyList<SourceNote> Unrecognized, IReadOnlyList<SourceNote> Notes,
        IReadOnlyList<FlagProcedure> Procedures, IReadOnlyDictionary<string, int>? Usage, IReadOnlyList<ProcedureCall>? Calls);

    /// <summary>Güncel önizleme varsa onun tablosu (formlardaki atamalar dahil), yoksa doğrudan okuma.</summary>
    private FlagView? FlagSource()
    {
        if (_report != null && _reportIsCurrent)
            return new FlagView(_report.Flags, _report.UnrecognizedFlagLines, _report.FlagNotes, _report.FlagProcedures, _report.FlagUsage, _report.ProcedureCalls);
        if (_flagRead != null)
            return new FlagView(_flagRead.Flags, _flagRead.Unrecognized, _flagRead.Notes, _flagRead.Procedures, null, null);
        return null;
    }

    /// <summary>Yordamlar, çalıştırma sırası, projedeki çağrı yerleri ve okuma notları.</summary>
    private string BuildProceduresText(FlagView v)
    {
        var sb = new StringBuilder();
        var order = _txtInit.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var roots = v.Procedures.Where(p => p.IsRoot).Select(p => p.Name).ToList();
        sb.AppendLine(order.Length > 0
            ? $"Çağrı sırası (verilen): {string.Join(" → ", order)}  — son atama geçerli"
            : roots.Count > 1
                ? $"Çağrı sırası verilmedi. Birbirini çağırmayan yordamlar: {string.Join(", ", roots)}. Aynı bayrağa farklı değer verenler Bilinmiyor sayılır."
                : $"Çalıştırılan yordam: {(roots.Count == 1 ? roots[0] : "(yok)")}");
        sb.AppendLine();
        var tmp = new Report();
        tmp.FlagProcedures.AddRange(v.Procedures);
        if (v.Calls != null) tmp.ProcedureCalls.AddRange(v.Calls);
        tmp.AppendProcedures(sb);
        sb.AppendLine();
        tmp.Constants = _reportIsCurrent && _report != null ? _report.Constants : _flagConsts ?? new CondConstants();
        tmp.ResolveDirectives = _chkDirectives.Checked;
        tmp.AppendConstants(sb);
        if (v.Calls == null) sb.AppendLine("Projede çağrıldıkları yerler: Önizle'ye basınca görünür.");
        else if (v.Calls.Count == 0) sb.AppendLine("Projede çağrıldıkları yer bulunamadı.");
        if (v.Notes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Okuma notları:");
            foreach (var n in v.Notes)
                sb.AppendLine($"  {(n.Line > 0 ? $"satır {n.Line}: " : "")}{n.Message}");
        }
        return sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    private void FillFlags()
    {
        var src = FlagSource();
        _lvFlags.BeginUpdate();
        _lvFlags.Items.Clear();
        _lvUnrecognized.BeginUpdate();
        _lvUnrecognized.Items.Clear();
        _txtFlagDetail.Clear();
        if (src == null)
        {
            _lvFlags.EndUpdate();
            _lvUnrecognized.EndUpdate();
            _txtProcedures.Clear();
            _lblFlagSummary.Text = "Bayrak dosyası seçilmedi.";
            _tabs.TabPages[1].Text = "Bayraklar";
            return;
        }

        var (flags, unrecognized, usage) = (src.Flags, src.Unrecognized, src.Usage);
        _txtProcedures.Text = BuildProceduresText(src);
        var suggestion = _reportIsCurrent ? _report?.SuggestInitOrder() : null;
        string current = string.Join(",", _txtInit.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        _btnUseOrder.Visible = suggestion != null && !string.Equals(string.Join(",", suggestion.Value.Order), current, StringComparison.OrdinalIgnoreCase);
        if (suggestion is var (order, source))
        {
            _btnUseOrder.Text = $"Projedeki çağrı sırasını kullan: {string.Join(" → ", order)}";
            _btnUseOrder.Tag = string.Join(",", order);
            _toolTip.SetToolTip(_btnUseOrder, $"{source} içinde bu sırayla çağrılıyor. Tıklayınca 'Çağrı sırası' alanına yazılır ve önizleme yenilenir.");
        }
        _tabFlagInfo.TabPages[1].Text = $"Yordamlar ve notlar ({src.Procedures.Count})";
        _tabFlagInfo.TabPages[0].Text = $"Tanınmayan satırlar ({unrecognized.Count})";
        var filter = _txtFlagFilter.Text.Trim();
        var all = flags.All.ToList();
        IEnumerable<FlagInfo> rows = all.OrderBy(f => f.Line).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase);
        if (filter.Length > 0) rows = rows.Where(f => f.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
        rows = _cmbFlagValue.SelectedIndex switch
        {
            1 => rows.Where(f => f.Value == true),
            2 => rows.Where(f => f.Value == false),
            3 => rows.Where(f => f.Value == null),
            4 => usage == null ? [] : rows.Where(f => !usage.ContainsKey(f.Name)),
            _ => rows,
        };

        int shown = 0;
        foreach (var f in rows)
        {
            string used = usage == null ? "—" : usage.GetValueOrDefault(f.Name).ToString();
            var item = new ListViewItem([
                f.Name,
                f.Value switch { true => "True", false => "False", _ => "Bilinmiyor" },
                f.Style, f.HowRead, f.Line.ToString(), used,
            ]) { Tag = f };
            item.UseItemStyleForSubItems = false;
            item.SubItems[1].ForeColor = f.Value switch { true => Color.DarkGreen, false => Color.Firebrick, _ => Color.DarkOrange };
            if (f.UnknownReason != null) item.SubItems[3].ForeColor = Color.DarkOrange;
            if (usage != null && !usage.ContainsKey(f.Name)) item.SubItems[5].ForeColor = Color.Gray;
            _lvFlags.Items.Add(item);
            shown++;
        }
        _lvFlags.EndUpdate();

        foreach (var u in unrecognized)
            _lvUnrecognized.Items.Add(new ListViewItem([u.Line.ToString(), u.Message, u.Text ?? ""]));
        _lvUnrecognized.EndUpdate();

        _lblFlagSummary.Text = $"Gösterilen {shown} / {all.Count}   —   True: {all.Count(f => f.Value == true)}, " +
                               $"False: {all.Count(f => f.Value == false)}, Bilinmiyor: {all.Count(f => f.Value == null)}" +
                               (usage == null
                                   ? "   (kullanım bilgisi için Önizle)"
                                   : $", kullanılmayan: {all.Count(f => !usage.ContainsKey(f.Name))}   (önizleme sonucu, formlardaki atamalar dahil)");
        _tabs.TabPages[1].Text = $"Bayraklar ({all.Count})";
        if (_lvFlags.Items.Count > 0) _lvFlags.Items[0].Selected = true;
    }

    private void ShowSelectedFlag()
    {
        if (_lvFlags.SelectedItems.Count == 0 || _lvFlags.SelectedItems[0].Tag is not FlagInfo f)
        {
            _txtFlagDetail.Clear();
            return;
        }
        var usage = FlagSource()?.Usage;
        var sb = new StringBuilder();
        sb.AppendLine($"{f.Name} = {f.Value switch { true => "True", false => "False", _ => "Bilinmiyor" }}");
        sb.AppendLine();
        sb.AppendLine($"Tanım biçimi : {f.Style}");
        sb.AppendLine($"Tanım satırı : {f.Line}: {f.Declaration}");
        if (f.Expression != null && f.Kind == FlagKind.Const) sb.AppendLine($"Değer ifadesi: {f.Expression}");
        if (f.AssignmentLine != null) sb.AppendLine($"Atama        : {f.AssignmentProcedure}, satır {f.AssignmentLine}: {f.AssignmentText}");
        else if (f.Kind == FlagKind.Variable) sb.AppendLine("Atama        : yok");
        sb.AppendLine($"Nasıl okundu : {f.HowRead}");
        if (f.UnknownReason != null) sb.AppendLine($"Bilinmiyor   : {f.UnknownReason}");
        sb.AppendLine(usage == null
            ? "Kullanım     : (önizlemeden sonra görünür)"
            : $"Kullanım     : {usage.GetValueOrDefault(f.Name)} kaynak dosyada");
        _txtFlagDetail.Text = sb.ToString();
    }

    private void ShowSelectedFile()
    {
        _lvDetails.BeginUpdate();
        _lvDetails.Items.Clear();
        if (_lvFiles.SelectedItems.Count == 0 || _lvFiles.SelectedItems[0].Tag is not FileReport f || _report == null)
        {
            _lblDiffTitle.Text = "";
            _rtbDiff.Clear();
            _lvDetails.EndUpdate();
            return;
        }

        _lblDiffTitle.Text = f.RelativePath;
        if (f.OriginalBytes != null && f.OutputBytes != null)
        {
            var enc = VbSourceFile.GetEncoding((int)_numCodePage.Value);
            _rtbDiff.Rtf = DiffRtf.Build(enc.GetString(f.OriginalBytes), enc.GetString(f.OutputBytes), _chkAllLines.Checked, out _diffLineMap);
        }
        else
        {
            _rtbDiff.Clear();
            _diffLineMap = [];
        }

        foreach (var c in f.Changes)
            _lvDetails.Items.Add(new ListViewItem([c.Line.ToString(), KindText(c.Kind), c.Description]));
        foreach (var w in f.Warnings)
        {
            var item = new ListViewItem([w.Line.ToString(), "UYARI", w.Message + (w.Text != null ? "   | " + w.Text : "")]) { ForeColor = Color.DarkOrange };
            _lvDetails.Items.Add(item);
        }
        var fileItem = _lvFiles.SelectedItems[0];
        if (f.KeptAsIs)
            _lvDetails.Items.Add(new ListViewItem(["", "", "Seçilmediği için temizlenmeden olduğu gibi aktarıldı."]) { ForeColor = Color.SteelBlue });
        else if (f.Changed && _lastWasDryRun && !fileItem.Checked)
            _lvDetails.Items.Insert(0, new ListViewItem(["", "", "Seçili değil: Dönüştür'de bu dosya temizlenmeden olduğu gibi aktarılır. Temizlemek için kutusunu işaretleyin."]) { ForeColor = Color.SteelBlue });
        else if (_lvDetails.Items.Count == 0 && !f.RemovedFromProject)
            _lvDetails.Items.Add(new ListViewItem(["", "", "Bu dosyada değişiklik veya uyarı yok."]) { ForeColor = Color.Gray });
        if (f.RemovedFromProject)
        {
            string? reason = _report.RemovedForms.FirstOrDefault(r => r.RelativePath == f.RelativePath)?.Reason;
            _lvDetails.Items.Insert(0, new ListViewItem(["", "", "Bu form projede hiçbir yerde kullanılmıyor: çıktıdaki .vbp'den çıkarılır, .frm/.frx kopyalanmaz. Nedeni: " + reason]) { ForeColor = Color.MediumPurple });
        }
        _lvDetails.EndUpdate();

        // "tüm satırlar" açılarak yeniden çizildiyse istenen satıra git ve ayrıntı satırını yeniden seç
        if (_pendingDiffLine is int pending)
        {
            _pendingDiffLine = null;
            ScrollDiffToLine(pending);
            _jumping = true;
            var again = _lvDetails.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Text == pending.ToString());
            if (again != null) again.Selected = true;
            _jumping = false;
        }
    }

    private bool _jumping;

    /// <summary>Ayrıntı listesinde seçilen değişiklik/uyarı satırına önizlemede gider.</summary>
    private void JumpToDetail()
    {
        if (_jumping || _lvDetails.SelectedItems.Count == 0 || !int.TryParse(_lvDetails.SelectedItems[0].Text, out int line)) return;
        if (_diffLineMap.Contains(line))
        {
            ScrollDiffToLine(line);
        }
        else if (!_chkAllLines.Checked)
        {
            // satır özet görünümde yok (ör. değişmeyen satırdaki uyarı): tüm satırları açıp oraya git
            _pendingDiffLine = line;
            _chkAllLines.Checked = true;
        }
    }

    private const int EM_GETFIRSTVISIBLELINE = 0xCE, EM_LINESCROLL = 0xB6;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Orijinal satır numarasına karşılık gelen önizleme satırını seçer ve görünür alanın üstüne yakın kaydırır.</summary>
    private void ScrollDiffToLine(int originalLine)
    {
        int idx = _diffLineMap.IndexOf(originalLine);
        if (idx < 0 || idx >= _rtbDiff.Lines.Length) return;
        int start = _rtbDiff.GetFirstCharIndexFromLine(idx);
        int end = idx + 1 < _rtbDiff.Lines.Length ? _rtbDiff.GetFirstCharIndexFromLine(idx + 1) - 1 : _rtbDiff.TextLength;
        _rtbDiff.Select(start, Math.Max(0, end - start));
        int first = (int)SendMessage(_rtbDiff.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
        SendMessage(_rtbDiff.Handle, EM_LINESCROLL, IntPtr.Zero, idx - first - 3);
    }

    private static string KindText(ChangeKind k) => k switch
    {
        ChangeKind.BlockRemoved => "Blok silindi",
        ChangeKind.BlockLifted => "Blok açıldı",
        ChangeKind.BlockSimplified => "Sadeleşti",
        ChangeKind.DirectiveResolved => "#If çözüldü",
        _ => "Tek satır If",
    };

    private void SaveReport()
    {
        if (_report == null) return;
        using var dlg = new SaveFileDialog
        {
            Title = "Raporu kaydet",
            Filter = "Metin dosyası (*.txt)|*.txt",
            FileName = $"VbFlagResolver-rapor-{DateTime.Now:yyyyMMdd-HHmm}.txt",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { new WriteGuard(_report.SourceDir, _report.FlagsFile, _report.ReferenceDirs).CheckFile(dlg.FileName); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message + "\n\nRaporu kaynak klasörün dışına kaydedin.", "Raporu kaydet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        File.WriteAllText(dlg.FileName, _report.ToText(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        _lblStatus.Text = "Rapor kaydedildi: " + dlg.FileName;
    }

    private static void OpenFolder(string path)
    {
        if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    // ------------------------------------------------------------------ ayarlar

    private void LoadSettings()
    {
        _txtFlags.Text = _settings.FlagsPath;
        _txtSrc.Text = _settings.SourceDir;
        _txtOut.Text = _settings.OutputDir;
        _cmbMode.SelectedIndex = Math.Clamp(_settings.Mode, 0, 2);
        _txtInit.Text = _settings.InitProcedures;
        _txtExt.Text = _settings.Extensions;
        _numCodePage.Value = Math.Clamp(_settings.CodePage, (int)_numCodePage.Minimum, (int)_numCodePage.Maximum);
        SetUnassignedUnknown(_settings.UnassignedUnknown);
        _chkCopy.Checked = _settings.CopyOtherFiles;
        _chkRemoveUnusedForms.Checked = _settings.RemoveUnusedForms;
        _txtRefDirs.Text = _settings.ReferenceDirs;
        _txtDefines.Text = _settings.Defines;
        _chkDirectives.Checked = _settings.ResolveDirectives;
        _chkUndefinedZero.Checked = _settings.UndefinedConstantsAreZero;
        _txtDefines.Enabled = _chkUndefinedZero.Enabled = _chkDirectives.Checked;
    }

    private void SetUnassignedUnknown(bool unknown)
    {
        _rbUnassignedUnknown.Checked = unknown;
        _rbUnassignedFalse.Checked = !unknown;
    }

    private void SaveSettings()
    {
        _settings.FlagsPath = _txtFlags.Text.Trim();
        _settings.SourceDir = _txtSrc.Text.Trim();
        _settings.OutputDir = _txtOut.Text.Trim();
        _settings.Mode = _cmbMode.SelectedIndex;
        _settings.InitProcedures = _txtInit.Text.Trim();
        _settings.Extensions = _txtExt.Text.Trim();
        _settings.CodePage = (int)_numCodePage.Value;
        _settings.UnassignedUnknown = _rbUnassignedUnknown.Checked;
        _settings.CopyOtherFiles = _chkCopy.Checked;
        _settings.RemoveUnusedForms = _chkRemoveUnusedForms.Checked;
        _settings.ReferenceDirs = _txtRefDirs.Text.Trim();
        _settings.Defines = _txtDefines.Text.Trim();
        _settings.ResolveDirectives = _chkDirectives.Checked;
        _settings.UndefinedConstantsAreZero = _chkUndefinedZero.Checked;
        _settings.Save();
    }

    /// <summary>--flags/--src/--out ile alanları doldurur; --preview varsa true döner.</summary>
    private bool ApplyArgs(string[] args)
    {
        bool preview = false;
        for (int i = 0; i < args.Length; i++)
        {
            string? next = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i].ToLowerInvariant())
            {
                case "--flags" when next != null: _txtFlags.Text = Path.GetFullPath(next); i++; break;
                case "--src" when next != null: _txtSrc.Text = Path.GetFullPath(next); i++; break;
                case "--out" when next != null: _txtOut.Text = Path.GetFullPath(next); i++; break;
                case "--preview": preview = true; break;
            }
        }
        return preview;
    }
}
