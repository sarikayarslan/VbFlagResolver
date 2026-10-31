# VbFlagResolver

VB5 projesindeki geliştirme bayraklarını (`mDevelopmentFlags.bas`) okuyup `.frm/.bas/.cls/.ctl`
dosyalarındaki `If/ElseIf/Else` bloklarını bu bayraklara göre sadeleştiren araç. Kurallar için `CLAUDE.md`'ye bakın.

## Derleme ve test

```
dotnet build
dotnet test
```

## Tek dosyalık exe (şirket bilgisayarına .NET kurmadan)

```
dotnet publish src/VbFlagResolver.Cli -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Çıktı: `publish/VbFlagResolver.exe` — tek başına kopyalanıp çalıştırılabilir.

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
| `--init-proc InitFlags[,Diger]` | Değişken bayraklara değer veren yordamlar. Varsayılan: modüldeki tüm yordamlar. |
| `--unassigned-unknown` | Hiç atanmamış Boolean değişkeni False yerine Bilinmiyor say. |
| `--ext .frm,.bas` | İşlenecek uzantılar. |
| `--no-copy` | Kaynak olmayan dosyaları kopyalama. |
| `--codepage 1254` | Kaynak kod sayfası. |

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
src/VbFlagResolver.Core    FlagReader, VbSourceFile, Lexer, ExprParser, Simplifier, BlockParser, Rewriter, Report, ProjectProcessor
src/VbFlagResolver.Cli     komut satırı
tests/VbFlagResolver.Tests xUnit; Cases/*.input.bas → *.expected.bas çiftleri (her kural için bir çift)
samples/                   sentetik VB5 projesi (Windows-1254, CRLF)
```
