VERSION 5.00
Object = "{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCTL.OCX"
Begin VB.Form frmSiparis
   BorderStyle     =   3  'Fixed Dialog
   Caption         =   "Sipariþ Giriþi - If FLAG_A Then"
   ClientHeight    =   6120
   ClientLeft      =   45
   ClientTop       =   330
   ClientWidth     =   8415
   BeginProperty Font
      Name            =   "Tahoma"
      Size            =   8.25
      Charset         =   162
      Weight          =   400
      Underline       =   0   'False
      Italic          =   0   'False
      Strikethrough   =   0   'False
   EndProperty
   LinkTopic       =   "Form1"
   MaxButton       =   0   'False
   ScaleHeight     =   6120
   ScaleWidth      =   8415
   StartUpPosition =   1  'CenterOwner
   Begin VB.Frame fraMusteri
      Caption         =   "Müþteri Bilgileri"
      Height          =   1815
      Left            =   120
      TabIndex        =   0
      Top             =   120
      Width           =   8175
      Begin VB.TextBox txtMusteri
         Height          =   315
         Left            =   1440
         TabIndex        =   2
         Text            =   "End If"
         Top             =   360
         Width           =   3015
      End
      Begin VB.ComboBox cboOdeme
         Height          =   315
         ItemData        =   "frmSiparis.frx":0000
         Left            =   1440
         List            =   "frmSiparis.frx":0002
         Style           =   2  'Dropdown List
         TabIndex        =   3
         Top             =   840
         Width           =   3015
      End
      Begin VB.Label lblMusteri
         Caption         =   "Müþteri:"
         Height          =   255
         Left            =   120
         TabIndex        =   1
         Top             =   400
         Width           =   1215
      End
   End
   Begin MSComctlLib.ListView lvwKalem
      Height          =   2895
      Left            =   120
      TabIndex        =   4
      Top             =   2040
      Width           =   8175
      _ExtentX        =   14420
      _ExtentY        =   5106
      View            =   3
      _Version        =   393217
      BeginProperty ColumnHeaders {BDD1F052-858B-11D1-B16A-00C0F0283628}
         NumItems        =   2
         BeginProperty ColumnHeader(1) {BDD1F052-858B-11D1-B16A-00C0F0283628}
            Text            =   "Ürün"
            Object.Width           =   5292
         EndProperty
         BeginProperty ColumnHeader(2) {BDD1F052-858B-11D1-B16A-00C0F0283628}
            SubItemIndex    =   1
            Text            =   "Tutar"
            Object.Width           =   2540
         EndProperty
      EndProperty
   End
   Begin VB.CommandButton cmdKaydet
      Caption         =   "&Kaydet"
      Default         =   -1  'True
      Height          =   375
      Left            =   6960
      TabIndex        =   5
      Top             =   5640
      Width           =   1335
   End
   Begin VB.Menu mnuDosya
      Caption         =   "&Dosya"
      Begin VB.Menu mnuYazdir
         Caption         =   "&Yazdýr"
      End
   End
End
Attribute VB_Name = "frmSiparis"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' Karmaþýk sipariþ formu: araç testleri için bilerek zorlaþtýrýldý.
' "If FLAG_ESKI_MENU Then" yazan yorumlar ve string'ler deðiþmemeli.

Private mcurToplam As Currency
Private Const KDV_ORANI = 0.18

Private Sub Form_Load()
    Dim i As Integer
    Me.Caption = "Sipariþ - If FLAG_ESKI_MENU Then ... End If"   ' string içinde If
    ' 1) 5 seviye iç içe: True / Bilinmiyor / False karýþýk
    If FLAG_SIPARIS_V2 Then                         ' yeni sipariþ ekraný
        If gbFlagRaporV3 Then
            If FLAG_ESKI_FIYAT Then
                FiyatlariEskiYukle
            ElseIf FLAG_KDV_YENI Then
                If cboOdeme.ListCount = 0 Then
                    If FLAG_STOK_UYARI And Not FLAG_B Then
                        cboOdeme.AddItem "Nakit"
                        cboOdeme.AddItem "Kredi Kartý"
                    Else
                        cboOdeme.AddItem "Çek"
                    End If
                End If
            Else
                cboOdeme.AddItem "Havale"
            End If
        Else
            lvwKalem.View = lvwReport
        End If
    Else
        MsgBox "Eski sipariþ ekraný kapatýldý", vbInformation
    End If

    ' 2) ElseIf zinciri: Bilinmiyor önde, sonra True -> Else olur; sonrakiler silinir
    If gbFlagKomutSatiri Then
        txtMusteri.Text = Command$
    ElseIf FLAG_ESKI_MENU Then
        txtMusteri.Text = "eski"
    ElseIf FLAG_LOG_DETAY = True Then
        txtMusteri.Text = "log"
    ElseIf gbFlagAtlamali Then
        txtMusteri.Text = "atlamalý"
    Else
        txtMusteri.Text = ""
    End If

    ' 3) ilk dal False: sonraki ElseIf -> If olur
    If FLAG_BETA_EKRAN Or FLAG_Y Then
        Me.BackColor = vbYellow
    ElseIf gbFlagHataYolu Xor FLAG_B Then
        Me.BackColor = vbRed
    ElseIf FLAG_A Eqv FLAG_C Then
        Me.BackColor = vbWhite
    End If

    ' 4) For / With / Select Case / Do içinde If
    For i = 1 To 3
        With lvwKalem.ListItems
            Select Case i
                Case 1
                    If FLAG_HEX_ACIK Then .Add , , "Kalem " & i
                Case 2
                    If Not FLAG_DEVAM Then
                        .Add , , "Gizli"
                    Else
                        If FLAG_ESIT <> False Then .Add , , "Eþit": mcurToplam = mcurToplam + 1
                    End If
                Case Else
                    Do While .Count < i
                        If FLAG_KARMA Then Exit Do Else .Add , , "Ek " & i
                    Loop
            End Select
        End With
    Next i
End Sub

Private Sub cmdKaydet_Click()
    Dim sMesaj As String
    On Error GoTo HataYakala
    ' 5) koþul satýr devamý ile bölünmüþ, yorumlu baþlýk
    If FLAG_TURETILMIS And _
       (gbFlagMusteriV2 Or gbFlagCalismaZamani) And _
       Not FLAG_ESKI_YAZICI Then                    ' kaydetme koþulu
        sMesaj = "Kaydedildi: " & txtMusteri.Text
    End If ' kaydetme sonu

    ' 6) silinen kýsýmda fonksiyon çaðrýsý: VB'de kýsa devre yok, uyarý beklenir
    If FLAG_ESKI_MENU And EskiKontrol() Then
        sMesaj = sMesaj & " (eski kontrol)"
    End If

    ' 7) silinen blokta etiket ve Const; baþka yerde kullanýlýyor -> uyarý
    If FLAG_ESKI_YAZICI Then
        Const ESKI_PORT = "LPT1"
YeniDene:
        Open ESKI_PORT For Output As #1
        Print #1, sMesaj
        Close #1
    End If
    If Len(sMesaj) = 0 Then GoTo YeniDene

    ' 8) tek satýrlýk If biçimleri
    If FLAG_A Then Call Yazdir(sMesaj) Else Debug.Print "yazdýrýlmadý"
    If FLAG_B Then Debug.Print "B" Else Debug.Print "B deðil": mcurToplam = 0
    If FLAG_C Then Hazirla: Debug.Print "hazýr"
    Close #1: If FLAG_ESKI_YAZICI Then Close #2
    If FLAG_X Then 100 Else 200
100:
    Debug.Print "100"
200:
    If SEVIYE = True And FLAG_A Then Debug.Print "seviye"
    If gbFlagCalismaZamani = True And FLAG_YENI_RAPOR Then Debug.Print "çalýþma"
    If mDevelopmentFlags.FLAG_LOG_DETAY Then Debug.Print "modül adýyla"
    mcurToplam = mcurToplam * IIf(FLAG_KDV_YENI, 1 + KDV_ORANI, 1)   ' koþul dýþý kullaným
    Exit Sub
HataYakala:
    MsgBox Err.Description, vbCritical, "Hata"
End Sub

Private Sub Hesapla(ByVal FLAG_GOLGE As Boolean, Optional ByVal nAdet As Integer = 1)
    ' parametre bayraðý gölgeliyor: FLAG_GOLGE her yerde Bilinmiyor olmalý
    If FLAG_GOLGE Then
        mcurToplam = mcurToplam * nAdet
    End If
End Sub

Private Sub mnuYazdir_Click()
    ' 9) #If ile If baþlýklarý; DEBUG_MODE = 0 (Proje.vbp)
#If DEBUG_MODE Then
    If FLAG_DEBUG_ARAYUZ Or gbFlagDebugPanel Then
#Else
    If FLAG_DEBUG_ARAYUZ And gbFlagDebugPanel Then
#End If
        Debug.Print "debug yazdýr"
    End If
	' sekmeyle girintilenmiþ bölüm
	If FLAG_STOK_UYARI Then
		If gbFlagYardimci Then
			Debug.Print "stok"
		End If
	End If
    ' 10) "Else If" (boþluklu): bu blok olduðu gibi kalmalý
    If Me.Visible Then
        Debug.Print "görünür"
    Else If gbFlagSonradanAcik Then
        Debug.Print "sonradan"
    End If
    End If
    If gbFlagModulAdli Imp FLAG_B Then Debug.Print "imp"
    If (FLAG_ESKI_MENU) Then
    ElseIf (gbFlagCikisSonrasi) Then
        Debug.Print "çýkýþ"
    End If
End Sub

' 11) içi tamamen bayraða baðlý yordam: temizlikten sonra boþ kalýr; VB IDE boþ yordamý sildiði için yorum eklenmeli
Private Sub EskiYazdir(ByVal s As String)
    If FLAG_ESKI_YAZICI Then
        Printer.Print s
        Printer.EndDoc
    End If
End Sub

Private Function EskiKontrol() As Boolean
    EskiKontrol = True
End Function

Private Sub Yazdir(ByVal s As String)
    Debug.Print s
End Sub

Private Sub Hazirla()
End Sub

Private Sub FiyatlariEskiYukle()
End Sub
