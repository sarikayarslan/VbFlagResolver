# VbFlagResolver

Visual Basic 5/6 projelerinde geliştirme bayraklarına (feature flag) bağlı ölü kodu temizleyen araç.

Uzun yaşayan VB projelerinde yeni özellikler genellikle bir bayrak modülündeki Boolean sabit veya değişkenlerle
açılıp kapatılır (`Public Const FLAG_YENI_RAPOR = True`, `gbFlagX As Boolean` + `InitFlags` içinde atama). Zamanla
yüzlerce bayrak ve bunlara bağlı, artık hiç çalışmayan `If` dalları birikir. VbFlagResolver bayrak modülünü okur,
her bayrağın değerini çıkarır ve projedeki `.frm/.bas/.cls/.ctl` dosyalarında bu bayraklara bağlı
`If/ElseIf/Else` ve `#If` bloklarını sadeleştirir:

Önce (`FLAG_YENI_RAPOR = True`, `FLAG_ESKI_MENU = False`):

```vb
If FLAG_YENI_RAPOR Then
    frmRaporYeni.Show
Else
    frmRapor.Show
End If
If FLAG_ESKI_MENU Or bYetkili Then
    mnuAyarlar.Visible = True
End If
```

Sonra:

```vb
frmRaporYeni.Show
If bYetkili Then
    mnuAyarlar.Visible = True
End If
```

- Koşul kesin **True** ise `If` kalkar, dalın içeriği bir seviye sola alınarak yerine konur.
- Kesin **False** ise dal silinir.
- Çalışma anında belli olan bir değer içeriyorsa (ör. `bYetkili`) koşul sadeleştirilerek korunur.

Orijinal dosyalara asla yazmaz; çıktıyı ayrı bir klasöre üretir, değişmeyen satırları bayt bayt korur ve her
değişikliği ve olası riski (silinen koddaki fonksiyon çağrıları, etiketler vb.) raporlar. Arayüzde dosya yazmadan
önce satır satır fark görülebilir.

## Hızlı başlangıç

1. `publish/` klasöründeki `VbFlagResolver.Gui.exe` dosyasını indirin (kurulum gerekmez, aşağıya bakın).
2. Bayrak modülünüzü (`.bas`) ve proje klasörünüzü seçip **Önizle**'ye basın.
3. **Bayraklar** sekmesinde değerlerin doğru okunduğunu, **Dosyalar ve önizleme** sekmesinde değişiklikleri kontrol edin
   ve temizlenecek dosyaları işaretleyin.
4. **Dönüştür ve çıktıya yaz** ile temizlenmiş projeyi ayrı bir klasöre üretin, VB'de açıp derleyin.

Denemek için depodaki `samples/` klasörü hazır bir örnek proje içerir.

## Hazır exe'yi indirme (derlemeden kullanmak için)

Derlenmiş exe'ler depoda `publish/` klasöründe durur; .NET kurmaya veya derlemeye gerek yoktur.

| Dosya | Açıklama | Doğrudan indirme |
|---|---|---|
| `VbFlagResolver.Gui.exe` (~48 MB) | Arayüz | [indir](https://github.com/sarikayarslan/VbFlagResolver/raw/master/publish/VbFlagResolver.Gui.exe) |
| `VbFlagResolver.exe` (~35 MB) | Komut satırı | [indir](https://github.com/sarikayarslan/VbFlagResolver/raw/master/publish/VbFlagResolver.exe) |

GitHub sayfasından indirmek için: `publish` klasörüne girin → exe'ye tıklayın → sağ üstteki **Download raw file**
(aşağı ok) düğmesine basın. Tarayıcı veya SmartScreen imzasız exe için uyarı verebilir: **Sakla / Ek bilgi → Yine de çalıştır**.

Katkıda bulunanlar için: kod değiştiğinde exe'ler aşağıdaki 5. adımla yeniden üretilip `publish/` klasörüyle
birlikte commit edilmelidir; aksi halde depodaki exe eski kodla kalır.

## Derleme

### 1. Gerekenler (yalnızca derleyen bilgisayarda)

| Gereken | Açıklama |
|---|---|
| Windows 10/11 (x64) | Arayüz projesi WinForms olduğu için Windows'ta derlenir. Komut satırı ve testler başka işletim sistemlerinde de derlenir. |
| [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) | Kontrol: `dotnet --list-sdks` çıktısında `9.0.x` olmalı. |
| İnternet bağlantısı | İlk derlemede NuGet paketleri (xUnit vb.), ilk publish'te win-x64 çalışma zamanı paketleri indirilir. Sonraki derlemeler önbellekten çalışır. |
| Git (isteğe bağlı) | Kaynakları klonlamak için. |

Visual Studio gerekmez; isterseniz `VbFlagResolver.sln` Visual Studio 2022 (17.12+) veya Rider ile de açılır.

**Exe'yi çalıştıracak bilgisayarda hiçbir şey kurulu olması gerekmez** (.NET dahil); bkz. adım 5 ve 6.

### 2. Kaynakları alma

```
git clone https://github.com/sarikayarslan/VbFlagResolver.git
cd VbFlagResolver
```

Aşağıdaki tüm komutlar depo kök klasöründe (`VbFlagResolver.sln`'nin olduğu yer) çalıştırılır.

### 3. Derleme ve testler

```
dotnet build
dotnet test
```

- `dotnet build` paketleri indirir (restore) ve dört projeyi Debug modda derler. Çıktılar her projenin
  `bin\Debug\` klasörüne gider.
- `dotnet test` xUnit testlerini çalıştırır; son satırda `Passed! - Failed: 0` görülmelidir.
  Testler `samples/` klasöründeki sentetik VB5 projesini ve `tests/VbFlagResolver.Tests/Cases/` altındaki
  girdi/beklenen dosya çiftlerini kullanır.
- Release derlemesi için: `dotnet build -c Release`.

### 4. Kaynaktan çalıştırma (geliştirme sırasında)

```
dotnet run --project src/VbFlagResolver.Cli -- --flags samples/mDevelopmentFlags.bas --src samples --dry-run
dotnet run --project src/VbFlagResolver.Gui
```

`--` işaretinden sonraki her şey programa parametre olarak geçer.

### 5. Tek dosyalık exe üretme (publish)

```
dotnet publish src/VbFlagResolver.Cli -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
dotnet publish src/VbFlagResolver.Gui -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

| Parametre | Anlamı |
|---|---|
| `-c Release` | İyileştirilmiş (optimize) derleme. |
| `-r win-x64` | 64 bit Windows için. |
| `--self-contained true` | .NET çalışma zamanı exe'nin içine konur; hedef bilgisayarda .NET gerekmez. |
| `-p:PublishSingleFile=true` | Her şey tek bir exe'de toplanır (sıkıştırmalı; ayarlar ilgili `.csproj` dosyalarında). |
| `-o publish` | Çıktı klasörü. |

Sonuç `publish\` klasöründe:

| Dosya | Boyut | Açıklama |
|---|---|---|
| `VbFlagResolver.exe` | ~35 MB | Komut satırı aracı. |
| `VbFlagResolver.Gui.exe` | ~48 MB | Arayüz. |

Yanlarında `.pdb` dosyası oluşursa (hata ayıklama bilgisi) silinebilir; çalışmak için gerekmez.

### 6. Başka bir bilgisayarda kullanma

1. Exe'yi (birini veya ikisini) VB projesinin bulunduğu bilgisayara kopyalayın. Başka dosya gerekmez.
   E-posta gibi bazı yollar `.exe` eki engeller; zip'leyerek aktarabilirsiniz.
2. İnternetten indirilen dosya engellenmiş olabilir: exe'ye sağ tık → **Özellikler** → **Engellemeyi kaldır**
   (veya PowerShell'de `Unblock-File .\VbFlagResolver.Gui.exe`).
3. İlk açılışta Windows SmartScreen "Windows bilgisayarınızı korudu" uyarısı verebilir (exe imzalı değil):
   **Ek bilgi → Yine de çalıştır**. Kurumsal bilgisayarlarda imzasız programlar yönetici tarafından engellenmiş
   olabilir; bu durumda sistem yöneticinizden izin isteyin.
4. İlk çalıştırmayı mutlaka **Önizle** (arayüz) veya `--dry-run` (komut satırı) ile yapın.

Tek dosyalık exe ilk açılışta kendini birkaç saniye içinde hazırlar; sonraki açılışlar daha hızlıdır.

### Sık karşılaşılan sorunlar

| Belirti | Neden / çözüm |
|---|---|
| Publish sırasında `The process cannot access the file '...\publish\VbFlagResolver.Gui.exe' because it is being used by another process` | O exe açık. Kapatıp (Görev Yöneticisi'nde `VbFlagResolver.Gui.exe` kalmadığından emin olun) komutu tekrarlayın. |
| `NETSDK1045: The current .NET SDK does not support targeting .NET 9.0` | .NET 9 SDK kurulu değil; `dotnet --list-sdks` ile kontrol edip kurun. |
| `NU1301: Unable to load the service index` veya restore takılıyor | İnternet/proxy erişimi yok. Paketler bir kez indirildikten sonra çevrimdışı derleme çalışır. |
| İlk publish uzun sürüyor | win-x64 çalışma zamanı paketi indiriliyor; yalnızca ilk seferde. |
| Testlerde `samples klasörü bulunamadı` | Testler depo içinden çalıştırılmalı (`samples/` klasörü depo kökünde olmalı). |

## Arayüz (VbFlagResolver.Gui.exe)

1. Bayrak dosyasını, kaynak klasörü ve çıktı klasörünü seçin (Gözat veya sürükle-bırak). Kaynak seçilince
   adında "Flag" geçen .bas dosyası ve `<kaynak>_temiz` çıktı klasörü önerilir.
   Bayrak dosyası seçilir seçilmez okunur ve **Bayraklar** sekmesi açılır: her bayrağın değeri, tanım biçimi
   (`Public Const ... As Boolean`, tipsiz, `Global`, değişken), nasıl okunduğu (doğrudan değer, ifadeden hesaplama,
   aynı satırda çoklu tanım, satır devamı, InitFlags ataması, atanmamış → False) ve tanım satırı görünür.
   Okuma seçenekleri değiştirilince liste yeniden okunur.
2. **Önizle (F5)** dosya yazmadan çalışır. Sonuçlar üç sekmede görünür:
   - **Dosyalar ve önizleme:** dosya başına silinen/açılan/sadeleşen blok sayıları; seçilen dosyanın renkli farkı
     (orijinal ↔ yeni satır numaralarıyla) ve satır satır değişiklik/uyarı listesi. Listeden bir değişiklik veya uyarı
     satırına tıklayınca önizleme o satıra gider.
     Önizleme tüm dosyalarda neyin değişeceğini gösterir; **yalnızca kutusu işaretli dosyalar temizlenir**, diğerleri
     olduğu gibi aktarılır. **Değişenlerin hepsini seç** / **Seçimi kaldır** düğmeleri toplu seçim yapar; seçim kaynak
     klasör başına saklanır. Seçilmeyen dosyalardaki bayrak atamaları da taranır (bayrağı Bilinmiyor yapabilir).
   - **Bayraklar:** önizlemeden sonra kullanıldıkları dosya sayısı ve formlardaki atamalar nedeniyle Bilinmiyor
     olanlar da eklenir; arama ve değere göre filtre (True/False/Bilinmiyor/kullanılmayan); bayrak dosyasında
     tanınmayan satırlar.
   - **Rapor:** komut satırının ürettiği raporun aynısı; **Raporu kaydet** ile dosyaya yazılır.
3. Temizlenecek dosyaları işaretleyip **Dönüştür ve çıktıya yaz**. Hiç dosya seçilmediyse veya çıktı klasörü boş
   değilse onay istenir; kaynak klasöre asla yazılmaz.

**Temizle** tüm alanları, seçenekleri ve sonuçları başlangıç durumuna döndürür; diskteki hiçbir dosyaya dokunmaz.

### Seçenekler

İlk dört seçenek bayrakların nasıl okunduğunu değiştirir; değiştirildiklerinde Bayraklar sekmesi hemen yeniden okunur.
"Diğer dosyaları kopyala" yalnızca çıktıya yazmayı etkiler. Parantez içinde komut satırındaki karşılığı var.

| Seçenek | Ne işe yarar | Ne zaman değiştirilir |
|---|---|---|
| **Okuma biçimi** (`--flag-mode`) | Bayrak dosyasında neyin bayrak sayılacağı. **Otomatik:** `Const X = True` sabitleri ve `Public X As Boolean` değişkenleri (değerleri yordamlardaki atamalardan). **Yalnızca sabitler:** değişkenler ve atamalar yok sayılır. **Yalnızca değişkenler:** sabitler yok sayılır. Yok sayılan satırlar "tanınmayan satırlar" listesine düşer. | Normalde Otomatik. Bayrak dosyasında bayrak olmayan Boolean değişkenler veya sabitler varsa ve bunların işlenmesi istenmiyorsa daraltılır. |
| **Çağrı sırası** (`--init-proc`) | Değişkenlere değer veren yordamlar, exe açılırken çağrıldıkları sırayla, virgülle (ör. `VarsayilanlariYukle,InitFlags`). Aynı bayrağa birden çok yordam değer veriyorsa son çalışanın değeri geçerli olur. Listede olmayan ve listedekilerden çağrılmayan yordamlardaki atamalar bayrağı Bilinmiyor yapar. **Boş:** tüm yordamlar okunur; birbirini çağırmayan yordamlar aynı bayrağa farklı değer veriyorsa o bayrak Bilinmiyor kalır. | Bayraklar sekmesinde "hangisinin son çalıştığı bilinmiyor" görüldüğünde. Doğru sıra "Yordamlar ve notlar" bölümünde görünür; tek ve kesin bir sıra bulunursa **Projedeki çağrı sırasını kullan** düğmesi alanı doldurur. |
| **Uzantılar** (`--ext`) | İçindeki If blokları işlenecek dosya türleri. Listede olmayan dosyalar işlenmez (kutu işaretliyse aynen kopyalanır). | Projede başka türde kod dosyası varsa eklenir (ör. `.dsr`), bazı türler işlenmek istenmiyorsa çıkarılır. |
| **Kod sayfası** (`--codepage`) | Dosyaların karakter kodlaması. 1254 = Windows Türkçe (ğ, ş, ı, İ…). Dosyalar bu kodlamayla okunur ve yazılır; değişmeyen satırlar her durumda bayt bayt korunur. | Yalnızca kaynaklar başka kodlamayla kaydedilmişse (ör. 1252 Batı Avrupa). Yanlış değer Türkçe karakterlerin önizlemede bozuk görünmesine yol açar. |
| **#If bloklarını çöz** (`--no-directives` kapatır) | **İşaretli (varsayılan):** `#If`/`#ElseIf`/`#Else`/`#End If` grupları derleme sabitlerine göre çözülür; VB'nin derleyeceği dal kalır, diğer dallar ve `#If` satırları silinir (bkz. "Koşullu derleme"). **Boş:** `#If`'lere dokunulmaz; bayrak dosyasında `#If` içindeki tanım/atamalar Bilinmiyor sayılır. | `#If`'lerin çıktıda olduğu gibi kalması isteniyorsa kapatılır (ör. farklı derleme yapılandırmaları korunacaksa). |
| **Derleme sabitleri** (`--define`) | `#If` koşullarındaki sabitlerin değerleri, ör. `DEBUG_MODE=0, YENI_EKRAN=1`. **Boş:** kaynak klasördeki `.vbp` dosyasının `CondComp` satırı kullanılır (VB'de Proje Özellikleri → Make → Conditional Compilation Arguments). Verilen değerler `.vbp`'yi ezer. | Hangi derleme için temizlik yapılacaksa o değerler yazılır; ör. `.vbp`'de geliştirme ayarı (`DEBUG_MODE = 1`) kayıtlıysa ve sürüm (`0`) için temizlenecekse. Kullanılan değerler "Yordamlar ve notlar" bölümünde ve raporda görünür. |
| **Tanımsız sabit = 0** (`--undefined-unknown` kapatır) | **İşaretli (varsayılan):** `.vbp`'de, Derleme sabitleri alanında veya `#Const` ile hiç tanımlanmamış sabit VB'deki gibi 0 (False) sayılır; ör. `#If ARSLAN = 1` dalı silinir. **Boş:** böyle `#If` grupları olduğu gibi bırakılır ve uyarı verilir. | Kullandığınız sabit listesinin eksik olabileceğinden şüpheleniyorsanız (ör. sabit yalnızca derleme makinesinde tanımlıysa) kaldırın. |
| **Hiç değer atanmamış Boolean değişken** (iki seçenekli; `--unassigned-unknown` = Bilinmiyor) | Yalnızca bayrak modülünde tanımlanıp hiçbir yordamda değer verilmeyen `gbX As Boolean` değişkenleri için; sabitleri ve değer atanmış değişkenleri etkilemez. **False say (varsayılan):** VB'de Boolean değişken False ile başlar; `If gbX Then` blokları silinir, `Else` kısmı kalır. **Bilinmiyor say:** bu bayraklara bağlı bloklar olduğu gibi kalır. | Değerin aracın göremediği bir yoldan verildiğinden şüpheleniliyorsa (ör. `ByRef` parametreyle başka yordamda değiştiriliyorsa). Diğer dosyalardaki doğrudan `gbX = ...` atamaları zaten yakalanır ve bayrağı her durumda Bilinmiyor yapar. |
| **Diğer dosyaları (.frx, .vbp...) kopyala** (`--no-copy` kapatır) | Önizlemeye etkisi yoktur; yalnızca **Dönüştür ve çıktıya yaz** sırasında. **İşaretli (varsayılan):** işlenmeyen dosyalar da (`.frx` form resimleri, `.vbp` proje, `.res`, resimler ve **bayrak dosyasının kendisi**) çıktıya aynen kopyalanır; çıktı klasörü VB5'te doğrudan açılabilen eksiksiz bir proje olur. **Boş:** yalnızca işlenen kaynak dosyalar yazılır; `.frx` ve bayrak dosyası olmadan proje açılmaz, dosyaları elle bir proje kopyasının üzerine koymak gerekir. | Neredeyse her zaman işaretli bırakılır. Yalnızca değişen kaynak dosyalarını ayrıca görmek isteniyorsa kapatılır. |
| **Kullanılmayan formları projeden çıkar** (`--remove-unused-forms`) | Projede hiçbir yerde kullanılmayan formları çıktıdaki `.vbp`'den çıkarır (`Form=` satırı silinir) ve `.frm`/`.frx` dosyalarını çıktıya kopyalamaz. Temizlenmiş kod esas alınır: yalnızca silinen bayrak bloklarından çağrılan formlar da kullanılmamış sayılır; zincir etkisi de çözülür (yalnızca kullanılmayan formdan açılan form da çıkar). Her zaman kalanlar: `.vbp`'deki başlangıç formu, adı string içinde geçen formlar (uyarı notu düşülür) ve bir MDI alt formu kullanılıyorsa MDI ana formu. Seçilmemiş (temizlenmeyen) dosyalar özgün koduyla sayılır. Önizlemede "Projeden çıkarılacak" olarak görünür. | Proje temizlendikten sonra artık açılmayan eski formları da atmak için açın; formlar adla dolaylı (ör. başka projeden) kullanılıyorsa kapalı bırakın. |

Son kullanılan yollar ve seçenekler `%APPDATA%\VbFlagResolver\gui-settings.json` dosyasında saklanır.
Alanlar komut satırından da doldurulabilir: `VbFlagResolver.Gui.exe --flags x.bas --src klasör [--out klasör] [--preview]`.

## Komut satırı kullanımı

İlk çalıştırma mutlaka kuru modda yapılmalı:

```
VbFlagResolver.exe --flags C:\Proje\mDevelopmentFlags.bas --src C:\Proje --dry-run --report C:\rapor.txt
```

Rapor uygunsa çıktı üret:

```
VbFlagResolver.exe --flags C:\Proje\mDevelopmentFlags.bas --src C:\Proje --out C:\Proje_temiz --report C:\rapor.txt
```

Orijinal dosyalara asla yazılmaz: `--out` kaynak klasörle aynı, onun içinde veya üstünde olamaz; `--report` kaynak
klasörün içinde veya bayrak dosyasının kendisi olamaz. Yollar junction/symlink'ler çözülerek karşılaştırılır.

| Seçenek | Açıklama |
|---|---|
| `--flags <dosya>` | Bayrak modülü (adı serbest). |
| `--src <klasör>` | Proje klasörü (alt klasörler dahil). |
| `--out <klasör>` | Çıktı klasörü. Değişmeyen dosyalar ve `.frx/.vbp` gibi diğer dosyalar bayt düzeyinde aynen kopyalanır. |
| `--dry-run` | Dosya yazmaz; yalnızca rapor. |
| `--report <dosya>` | Raporu dosyaya da yazar (UTF-8). |
| `--flag-mode auto\|const\|var` | Bayrak okuma biçimi: sabitler, Boolean değişkenler veya ikisi. |
| `--init-proc A,B` | Değişken bayraklara değer veren yordamlar, exe açılırken **çağrıldıkları sırayla** (arayüzde "Çağrı sırası"). Aynı bayrakta son atama geçerli. Varsayılan: tüm yordamlar (aşağıya bakın). |
| `--unassigned-unknown` | Hiç atanmamış Boolean değişkeni False yerine Bilinmiyor say. |
| `--ext .frm,.bas` | İşlenecek uzantılar. |
| `--keep frmEski.frm,Moduller\*.bas` | Bu dosyaları dönüştürmeden olduğu gibi aktar. Göreli yol veya yalnızca dosya adı; `*` ve `?` kullanılabilir; tekrar verilebilir. Bayrak atamaları yine taranır. |
| `--only frmMain.frm,frmStok.frm` | Yalnızca bu dosyaları temizle, diğerlerini olduğu gibi aktar (`--keep` ile aynı desenler). |
| `--remove-unused-forms` | Projede hiçbir yerde kullanılmayan formları çıktıdaki `.vbp`'den çıkarır; `.frm`/`.frx` kopyalanmaz. |
| `--no-copy` | Kaynak olmayan dosyaları kopyalama. |
| `--codepage 1254` | Kaynak kod sayfası. |
| `--define A=0,B=1` | `#If` için derleme sabitleri; `.vbp` CondComp değerlerini ezer. |
| `--no-directives` | `#If` gruplarını çözme. |
| `--undefined-unknown` | Tanımsız `#If` sabitini 0 sayma; o grubu olduğu gibi bırak (varsayılan: VB gibi 0). |

## Değişken bayraklar nasıl okunur?

`Public gbX As Boolean` gibi değişken bayrakların değeri, bayrak modülündeki yordamların içindeki atamalardan
gelir. Yordamlar metindeki sırayla değil, **çağrı sırasıyla** çalışmış sayılır:

- Aynı yordamda sırayla atama: son atama geçerli (`gbX = True` … en altta `gbX = False` → False).
- Bir yordam diğerini çağırıyorsa (`InitFlags` içinde `XFonksiyonu` / `Call XFonksiyonu`) çağrı yerinde izlenir.
- Birbirini çağırmayan yordamlar aynı bayrağa farklı değer veriyorsa hangisinin son çalıştığı koddan bilinemez:
  bayrak **Bilinmiyor** sayılır. Sırayı `--init-proc XFonksiyonu,InitFlags` (arayüzde "Çağrı sırası") ile verin.
  Doğru sırayı bulmak için rapordaki "Projede çağrıldıkları yerler" bölümüne bakın (ör. `Sub Main` veya `Form_Load`).
- `If`/döngü içindeki, koşullu `Exit Sub`'dan sonraki veya koşullu çağrılan yordamdaki atama: Bilinmiyor
  (aynı zincirde sonradan koşulsuz atama gelirse o geçerli olur). Koşulsuz `Exit Sub`'dan sonraki atama yok sayılır.
- Tek satırlık If içindeki atama (`If Command$ = "/dev" Then gbX = True`, `:` ile devam eden kısım dahil): Bilinmiyor.
- Yordamda `GoTo`/`GoSub` varsa o yordamdaki tüm atamalar Bilinmiyor sayılır (hangisinin atlandığı bilinemez).
  `On Error GoTo Hata` varsa hata verebilecek bir satırdan (çağrı, hesaplanamayan ifade) sonraki atamalar ve
  `Exit Sub`'dan sonraki hata bölümündeki atamalar Bilinmiyor sayılır.
- Satır numaralı (`10  gbX = True`) atamalar okunur; satır başındaki `Ad:` etikettir, yordam çağrısı sayılmaz.
- Çalıştırılmış sayılan ama projede hiç çağrılmayan bayrak yordamları raporda not olarak belirtilir.
- `Private`/`Dim`/kapsamsız `Const` bayraklar başka modülden erişilemez; başka bir dosyada kullanılıyorsa Bilinmiyor
  sayılır ve raporlanır.
- `mDevelopmentFlags.gbX = True` gibi modül adıyla yazılmış atama da okunur.
- Yordam dışındaki atama (`End Sub`'dan sonra `gbX = False`) VB'de derlenmez: bayrak Bilinmiyor sayılır ve raporlanır.
- Hiç atanmamış Boolean değişken VB varsayılanı olan False sayılır (`--unassigned-unknown` ile Bilinmiyor).

## Koşullu derleme (#If)

`#If` / `#ElseIf` / `#Else` / `#End If` grupları, bayraklardan **önce** çözülür; yani VB'nin derleyeceği kod üzerinde
çalışılır. Sabitlerin değeri (öncelik sırasıyla, sonraki öncekini ezer):

1. `.vbp` dosyasındaki `CondComp="DEBUG_MODE = 0 : YENI = 1"` satırı,
2. arayüzde "Derleme sabitleri" alanı / `--define`,
3. dosyanın içindeki `#Const` satırları — VB'deki gibi yalnızca o dosyada ve tanımlandığı yerden sonra geçerli.

Sonuçlar:

- Koşulu bilinen grup: derlenecek dalın içeriği kalır (girintisi değişmez), diğer dallar ve `#If` satırları silinir.
  Hiçbir dal derlenmiyorsa grup tamamen silinir.
- `#If`/`#Else` içinde iki farklı If başlığı olup gövde ve `End If` ortaksa, önce `#If` çözüldüğü için geriye tek başlık
  kalır ve If normal işlenir.
- Bayrak dosyasında `#If` içindeki tanım ve atamalar da derlenecek dala göre okunur.
- Hiçbir yerde tanımlanmamış sabit, VB'deki gibi **0 (False)** sayılır: `#If ARSLAN = 1` dalı silinir, `#Else` kalır;
  raporda "tanımsız sabit 0 sayıldı: ARSLAN" yazar. Arayüzde **Tanımsız sabit = 0** kutusu kaldırılırsa (komut
  satırında `--undefined-unknown`) böyle gruplar **olduğu gibi** bırakılır ve uyarı verilir; içindeki bayrak If'leri
  yine işlenir, bayrak dosyasında böyle bir grubun içindeki tanım/atama Bilinmiyor sayılır.
- Tanımlı ama değeri bilinmeyen sabit (ör. iki `.vbp` farklı değer veriyorsa) içeren grup her durumda olduğu gibi kalır.
- Silinecek bir If dalı çözülemeyen bir `#If`/`#End If` ile kesişiyorsa (biri silinip diğeri kalacaksa) o If bloğu
  değiştirilmez ve uyarı verilir; derlenmeyen kod üretilmez.
- Hangi dalın kaldığı raporda (`#If DEBUG_MODE → #Else dalı kaldı`) ve arayüzde dosya listesinin `#If` sütununda görünür.

## Rapor içeriği

- Okunan bayrak sayısı (True/False/Bilinmiyor), Bilinmiyor olanlar ve nedeni, hiç kullanılmayan bayraklar
- Bayrak dosyasında tanınmayan/atlanan satırlar
- Başka dosyada atama yapılan (`For X = ...` dahil), aynı isimle yeniden bildirilen veya aynı isimde parametresi
  olan bayraklar (Bilinmiyor sayılır)
- Dosya başına: silinen / açılan / sadeleşen blok ve tek satırlık If sayısı, satır satır değişiklik listesi
- Kullanılmayan formlar seçeneği açıksa projeden çıkarılan formlar ve nedeni; adı string içinde geçtiği için bırakılan formlar
- Uyarılar: silinen kısımdaki fonksiyon çağrıları (VB'de kısa devre yok), silinen etiket/Dim/Const'un başka yerde
  kullanılması, silinen `On Error`, koşul dışında kullanılan bayraklar, çözümlenemeyen koşullar,
  etiket veya `:` içeren (bu yüzden değiştirilmeyen) blok satırları

## Bilinen sınırlar

- İç içe tek satırlık If (`If A Then If B Then ...`) değiştirilmez, uyarı verilir.
- Değiştirilen başlık satırı satır devamı (` _`) içeriyorsa tek satıra birleştirilir.
- Etiket veya `:` ile ek ifade içeren `If/Else/End If` satırlarının bulunduğu bloklar olduğu gibi bırakılır (uyarı verilir).
- Silinecek dalda, yordamın başka bir yerinden `GoTo` ile atlanan bir etiket veya başka yerde kullanılan bir
  `Dim`/`Const` varsa, silmek derlenmeyen kod üreteceği için If bloğu olduğu gibi bırakılır (uyarı verilir).
- `Else If x Then` (boşluklu; VB'de Else + iç içe If) satırının bulunduğu blok olduğu gibi bırakılır; içindeki diğer
  If'ler ve dosyanın geri kalanı işlenir.
- Bir blok açılınca bloğu açan `If`, kalan dalın başlığı ve `End If` satırlarındaki yorumlar kendi satırlarında
  korunur; silinen dalın başlık yorumu o dalla birlikte silinir. Çözülen `#If` / `#ElseIf` / `#Else` / `#End If`
  satırlarındaki yorumlar için de aynı kural geçerlidir. Tamamen silinen bloğun (ve satırı silinen tek satırlık
  If'in) yorumları da silinir.
- Yorum sayılmayanlar doğru ayrılır: string içindeki `'` (`"it's"`), `Rem` ile başlayan adlar (`RemoveItem`).
  ` _` ile biten yorum satırında VB'deki gibi sonraki satır da yoruma dahildir ve ona dokunulmaz.
- Girinti, gövdenin başlıktan derin ilk kod satırına göre bir seviye azaltılır; bundan daha az girintili (hatalı
  girintili) satırlara dokunulmaz. Sekme 4 sütun sayılır.
- Temizlikten sonra içi tamamen boşalan yordama (Sub/Function/Property) bir yorum satırı eklenir ve uyarı verilir:
  VB IDE içi boş yordamı kaydederken siler, bu da çağrıldığı yerlerde derleme hatasına yol açar. Yordam gerekmiyorsa
  çağrılarıyla birlikte elle silinebilir.
- Tek satırlık If'ten kalan kısım anlamı değişmesin diye düzeltilir: `If FT Then 100` → `GoTo 100`,
  `If FT Then Init: x = 1` → `Call Init: x = 1` (yoksa `Init` etikete dönüşürdü).
- `&H`/`&O` sabitleri VB gibi işaretli yorumlanır (`&HFFFF` = -1, `&HFFFF&` = 65535).

## Yapı

```
src/VbFlagResolver.Core    FlagReader, VbSourceFile, Lexer, ExprParser, Simplifier, BlockParser, Rewriter, Report, ProjectProcessor, LineDiff, WriteGuard
src/VbFlagResolver.Cli     komut satırı
src/VbFlagResolver.Gui     WinForms arayüzü (önizleme/fark, bayrak listesi, rapor)
tests/VbFlagResolver.Tests xUnit; Cases/*.input.bas → *.expected.bas çiftleri (her kural için bir çift)
samples/                   sentetik VB5 projesi (Windows-1254, CRLF)
```
