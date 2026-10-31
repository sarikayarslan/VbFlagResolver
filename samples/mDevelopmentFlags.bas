Attribute VB_Name = "mDevelopmentFlags"
'==============================================================
' mDevelopmentFlags - Geliþtirme bayraklarý
' DÝKKAT: Deðerleri deðiþtirmeden önce Þükrü Bey'e danýþýn!
'==============================================================
Option Explicit

' --- Biçim 1: tipli sabit
Public Const FLAG_YENI_RAPOR As Boolean = True
Public Const FLAG_ESKI_MENU As Boolean = False

' --- Biçim 2: tipsiz sabit
Public Const FLAG_LOG_DETAY = True
Public Const FLAG_BETA_EKRAN = False   ' beta ekraný kapalý

' --- Biçim 3: Global
Global Const FLAG_KDV_YENI = True
Global Const FLAG_ESKI_YAZICI As Boolean = False
Const FLAG_YEREL_TEST = False

' --- Biçim 4: ayný satýrda birden çok sabit
Public Const FLAG_A = True, FLAG_B = False, FLAG_C As Boolean = True

' --- Biçim 5: satýr devamý
Public Const FLAG_UZUN_ISIMLI_GECICI_BAYRAK As Boolean = _
    True
Public Const FLAG_X = True, _
             FLAG_Y = False

' --- Biçim 6: diðer bayraklardan türetilmiþ
Public Const FLAG_TURETILMIS = FLAG_A And Not FLAG_B
Public Const FLAG_KAPALI_TURETILMIS As Boolean = FLAG_ESKI_MENU Or FLAG_B

Private Const FLAG_GIZLI = False ' özel ama yine de okunur
Rem Rem ile yazýlmýþ eski usul yorum satýrý
Public Const flag_kucuk_harf = True   ' kullanýmda FLAG_KUCUK_HARF yazýlýyor

' --- Biçim 7: deðiþken + InitFlags
Public gbFlagMusteriV2 As Boolean
Public gbFlagStokSayim As Boolean, gbFlagDebugPanel As Boolean
Public gbFlagHicAtanmamis As Boolean
Public gbFlagCalismaZamani As Boolean  ' frmAyarlar içinde atanýyor -> Bilinmiyor

' --- bayrak olmayan satýrlar (rapora "tanýnmayan" olarak yazýlmalý)
Public Const APP_VERSION = "5.2.1"
Public Const MAX_KAYIT As Long = 1000
Public Declare Function GetTickCount Lib "kernel32" () As Long
Public gsKullanici As String

public ArslanFlag as Boolean

Public Sub InitFLags()
    gbFlagMusteriV2 = True
    gbFlagStokSayim = False: gbFlagDebugPanel = True   ' iki atama tek satýrda
    gbFlagCalismaZamani = False
	ArslanFlag = True
End Sub


public sub DeleteFlags()
	ArslanFlag = False
End sub