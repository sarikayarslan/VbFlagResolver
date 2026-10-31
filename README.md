# VbFlagResolver

VB5 projesindeki geliştirme bayraklarını (`mDevelopmentFlags.bas`) okuyup `.frm/.bas/.cls/.ctl`
dosyalarındaki `If/ElseIf/Else` bloklarını bu bayraklara göre sadeleştiren araç. Orijinal dosyalara asla yazmaz;
çıktıyı ayrı bir klasöre üretir ve rapor verir.

## Derleme

### 1. Gerekenler (yalnızca derleyen bilgisayarda)

| Gereken | Açıklama |
|---|---|
| Windows 10/11 (x64) | Arayüz projesi WinForms olduğu için Windows'ta derlenir. Komut satırı ve testler başka işletim sistemlerinde de derlenir. |
| [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) | Kontrol: `dotnet --list-sdks` çıktısında `9.0.x` olmalı. |
| İnternet bağlantısı | İlk derlemede NuGet paketleri (xUnit vb.), ilk publish'te win-x64 çalışma zamanı paketleri indirilir. Sonraki derlemeler önbellekten çalışır. |
| Git (isteğe bağlı) | Kaynakları klonlamak için. |

Visual Studio gerekmez; isterseniz `VbFlagResolver.sln` Visual Studio 2022 (17.12+) veya Rider ile de açılır.

**Exe'yi kullanacak şirket bilgisayarında hiçbir şey kurulu olması gerekmez** (.NET dahil); bkz. adım 5.

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


1. `publish\` içindeki exe'yi (birini veya ikisini) USB bellek veya ağ paylaşımıyla kopyalayın. Başka dosya gerekmez.
2. İlk açılışta Windows SmartScreen "Windows bilgisayarınızı korudu" uyarısı verebilir (exe imzalı değil):
   **Ek bilgi → Yine de çalıştır**.
3. İlk çalıştırmayı mutlaka **Önizle** (arayüz) veya `--dry-run` (komut satırı) ile yapın.

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
     (orijinal ↔ yeni satır numaralarıyla) ve satır satır değişiklik/uyarı listesi.
   - **Bayraklar:** önizlemeden sonra kullanıldıkları dosya sayısı ve formlardaki atamalar nedeniyle Bilinmiyor
     olanlar da eklenir; arama ve değere göre filtre (True/False/Bilinmiyor/kullanılmayan); bayrak dosyasında
     tanınmayan satırlar.
   - **Rapor:** komut satırının ürettiği raporun aynısı; **Raporu kaydet** ile dosyaya yazılır.
3. Önizleme uygunsa **Dönüştür ve çıktıya yaz**. Çıktı klasörü boş değilse onay istenir; kaynak klasöre asla yazılmaz.

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
| **Hiç değer atanmamış Boolean değişken** (iki seçenekli; `--unassigned-unknown` = Bilinmiyor) | Yalnızca bayrak modülünde tanımlanıp hiçbir yordamda değer verilmeyen `gbX As Boolean` değişkenleri için; sabitleri ve değer atanmış değişkenleri etkilemez. **False say (varsayılan):** VB'de Boolean değişken False ile başlar; `If gbX Then` blokları silinir, `Else` kısmı kalır. **Bilinmiyor say:** bu bayraklara bağlı bloklar olduğu gibi kalır. | Değerin aracın göremediği bir yoldan verildiğinden şüpheleniliyorsa (ör. `ByRef` parametreyle başka yordamda değiştiriliyorsa). Diğer dosyalardaki doğrudan `gbX = ...` atamaları zaten yakalanır ve bayrağı her durumda Bilinmiyor yapar. |
| **Diğer dosyaları (.frx, .vbp...) kopyala** (`--no-copy` kapatır) | Önizlemeye etkisi yoktur; yalnızca **Dönüştür ve çıktıya yaz** sırasında. **İşaretli (varsayılan):** işlenmeyen dosyalar da (`.frx` form resimleri, `.vbp` proje, `.res`, resimler ve **bayrak dosyasının kendisi**) çıktıya aynen kopyalanır; çıktı klasörü VB5'te doğrudan açılabilen eksiksiz bir proje olur. **Boş:** yalnızca işlenen kaynak dosyalar yazılır; `.frx` ve bayrak dosyası olmadan proje açılmaz, dosyaları elle bir proje kopyasının üzerine koymak gerekir. | Neredeyse her zaman işaretli bırakılır. Yalnızca değişen kaynak dosyalarını ayrıca görmek isteniyorsa kapatılır. |

Son kullanılan yollar ve seçenekler `%APPDATA%\VbFlagResolver\gui-settings.json` dosyasında saklanır.
Alanlar komut satırından da doldurulabilir: `VbFlagResolver.Gui.exe --flags x.bas --src klasör [--out klasör] [--preview]`.

## Kullanım

İlk çalıştırma mutlaka kuru modda yapılmalı:

```
VbFlagResolver.exe --flags C:\Proje\mDevelopmentFlags.bas --src C:\Proje --dry-run --report rapor.txt
```

Rapor uygunsa çıktı üret (orijinal dosyalara asla yazılmaz; `--out` kaynak klasörle aynı veya onun üstü olamaz):

```
VbFlagResolver.exe --flags C:\Proje\mDevelopmentFlags.bas --src C:\Proje --out C:\Proje_temiz --report rapor.txt
```

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
| `--no-copy` | Kaynak olmayan dosyaları kopyalama. |
| `--codepage 1254` | Kaynak kod sayfası. |

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
- Yordam dışındaki atama (`End Sub`'dan sonra `gbX = False`) VB'de derlenmez: bayrak Bilinmiyor sayılır ve raporlanır.
- Hiç atanmamış Boolean değişken VB varsayılanı olan False sayılır (`--unassigned-unknown` ile Bilinmiyor).

## Rapor içeriği

- Okunan bayrak sayısı (True/False/Bilinmiyor), Bilinmiyor olanlar ve nedeni, hiç kullanılmayan bayraklar
- Bayrak dosyasında tanınmayan/atlanan satırlar
- Başka dosyada atama yapılan veya aynı isimle yeniden bildirilen bayraklar (Bilinmiyor sayılır)
- Dosya başına: silinen / açılan / sadeleşen blok ve tek satırlık If sayısı, satır satır değişiklik listesi
- Uyarılar: silinen kısımdaki fonksiyon çağrıları (VB'de kısa devre yok), silinen etiket/Dim'in başka yerde
  kullanılması, silinen `On Error`, koşul dışında kullanılan bayraklar, çözümlenemeyen koşullar,
  etiket veya `:` içeren (bu yüzden değiştirilmeyen) blok satırları

## Bilinen sınırlar

- `#If`/`#Const` koşullu derleme henüz işlenmiyor (ikinci aşama).
- İç içe tek satırlık If (`If A Then If B Then ...`) değiştirilmez, uyarı verilir.
- Değiştirilen başlık satırı satır devamı (` _`) içeriyorsa tek satıra birleştirilir.
- Etiket veya `:` ile ek ifade içeren `If/Else/End If` satırlarının bulunduğu bloklar olduğu gibi bırakılır (uyarı verilir).

## Yapı

```
src/VbFlagResolver.Core    FlagReader, VbSourceFile, Lexer, ExprParser, Simplifier, BlockParser, Rewriter, Report, ProjectProcessor, LineDiff
src/VbFlagResolver.Cli     komut satırı
src/VbFlagResolver.Gui     WinForms arayüzü (önizleme/fark, bayrak listesi, rapor)
tests/VbFlagResolver.Tests xUnit; Cases/*.input.bas → *.expected.bas çiftleri (her kural için bir çift)
samples/                   sentetik VB5 projesi (Windows-1254, CRLF)
```
