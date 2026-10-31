VERSION 5.00
Object = "{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCTL.OCX"
Begin VB.Form frmMain
   BorderStyle     =   1  'Fixed Single
   Caption         =   "Ana Menü - Stok ve Müþteri Takibi"
   ClientHeight    =   5415
   ClientLeft      =   45
   ClientTop       =   330
   ClientWidth     =   7860
   Icon            =   "frmMain.frx":0000
   LinkTopic       =   "Form1"
   MaxButton       =   0   'False
   ScaleHeight     =   5415
   ScaleWidth      =   7860
   StartUpPosition =   2  'CenterScreen
   Begin VB.Frame fraIslemler
      Caption         =   "Ýþlemler"
      Height          =   2895
      Left            =   120
      TabIndex        =   2
      Top             =   120
      Width           =   3615
      Begin VB.CommandButton cmdRapor
         Caption         =   "Rapor Göster"
         Height          =   495
         Left            =   240
         TabIndex        =   4
         Top             =   360
         Width           =   3135
      End
      Begin VB.CommandButton cmdStok
         Caption         =   "Stok Sayýmý"
         Height          =   495
         Left            =   240
         TabIndex        =   3
         Top             =   960
         Width           =   3135
      End
   End
   Begin MSComctlLib.StatusBar sbDurum
      Align           =   2  'Align Bottom
      Height          =   375
      Left            =   0
      TabIndex        =   1
      Top             =   5040
      Width           =   7860
      _ExtentX        =   13864
      _ExtentY        =   661
      _Version        =   393216
      BeginProperty Panels {8E3867A5-8586-11D1-B16A-00C0F0283628}
         NumPanels       =   1
         BeginProperty Panel1 {8E3867AB-8586-11D1-B16A-00C0F0283628}
            Text            =   "Hazýr"
         EndProperty
      EndProperty
   End
   Begin VB.Label lblBaslik
      Caption         =   "Hoþ geldiniz"
      BeginProperty Font
         Name            =   "MS Sans Serif"
         Size            =   12
         Charset         =   162
         Weight          =   700
      EndProperty
      Height          =   375
      Left            =   3960
      TabIndex        =   0
      Top             =   240
      Width           =   3735
   End
   Begin VB.Menu mnuDosya
      Caption         =   "&Dosya"
      Begin VB.Menu mnuCikis
         Caption         =   "Çý&kýþ"
      End
   End
End
Attribute VB_Name = "frmMain"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

Private mlSayac As Long

Private Sub Form_Load()
    Dim sMesaj As String

    ' Basit True bayrak: If kalkmalý, içerik sola kaymalý
    If FLAG_YENI_RAPOR Then
        cmdRapor.Caption = "Yeni Rapor (ðüþýöç)"
        cmdRapor.Tag = "YENI"
    End If

    ' Basit False bayrak: blok tamamen silinmeli
    If FLAG_ESKI_MENU Then
        mnuDosya.Caption = "Eski Menü"
        mnuDosya.Visible = False
    End If

    ' False + Else: yalnýzca Else içeriði kalmalý
    If FLAG_BETA_EKRAN Then
        lblBaslik.Caption = "BETA"
    Else
        lblBaslik.Caption = "Hoþ geldiniz, " & gsKullanici
    End If

    ' Çalýþma zamaný deðiþkeni ile karýþýk: koþul sadeleþip korunmalý
    If FLAG_LOG_DETAY And mlSayac > 0 Then
        Debug.Print "Sayaç: "; mlSayac
    End If

    ' = True / <> False sadeleþtirmesi
    If FLAG_KDV_YENI = True Then
        sMesaj = "KDV %20"
    End If
    If gbFlagCalismaZamani <> False And FLAG_A Then
        sMesaj = sMesaj & " (çalýþma zamaný)"
    End If

    ' Büyük/küçük harf duyarsýz eþleþme
    If flag_kucuk_harf Or Not fLisansVar() Then sbDurum.Panels(1).Text = "Lisans: ""Deneme"" sürümü"

    ' Tek satýrlýk If'ler
    If FLAG_A Then mlSayac = 1 Else mlSayac = 2
    If FLAG_B Then mlSayac = 10 Else mlSayac = 20 ' B kapalý
    If FLAG_ESKI_YAZICI Then Printer.Orientation = 2
    If FLAG_Y Or mlSayac > 5 Then mlSayac = 5
End Sub

Private Sub cmdRapor_Click()
    ' 4 seviye iç içe If
    If FLAG_YENI_RAPOR Then
        If mlSayac > 0 Then
            If FLAG_C Then
                If FLAG_B Then
                    MsgBox "Bu hiç görünmemeli"
                Else
                    Call RaporHazirla(mlSayac)
                End If
            End If
        Else
            MsgBox "Kayýt yok", vbInformation, "Uyarý"
        End If
    Else
        Call EskiRapor
    End If
End Sub

Private Sub cmdStok_Click()
    Dim i As Integer

    ' ElseIf zinciri: ilk dal False, ikinci Bilinmiyor, üçüncü True
    If FLAG_ESKI_MENU Then
        MsgBox "Eski stok ekraný"
    ElseIf gbFlagCalismaZamani Then
        frmStokYeni.Show vbModal
    ElseIf FLAG_X Then
        frmStok.Show vbModal
    Else
        MsgBox "Ulaþýlamaz"
    End If

    ' ElseIf zinciri: ilk dal False, ikinci True -> doðrudan ikinci dalýn içeriði
    If FLAG_Y Then
        Beep
    ElseIf FLAG_TURETILMIS Then
        sbDurum.Panels(1).Text = "Türetilmiþ bayrak açýk"
    Else
        sbDurum.Panels(1).Text = "Kapalý"
    End If

    ' For / Select Case / With içinde If
    For i = 1 To 3
        Select Case i
            Case 1
                If gbFlagStokSayim Then
                    Call StokSay(i)
                End If
            Case 2
                If gbFlagMusteriV2 Then
                    Call MusteriYukle(i)
                Else
                    Call EskiMusteriYukle(i)
                End If
            Case Else
                With sbDurum.Panels(1)
                    If gbFlagDebugPanel And i = 3 Then
                        .Text = "Debug: " & CStr(i)
                    End If
                End With
        End Select
    Next i
End Sub

Private Sub mnuCikis_Click()
    On Error GoTo Hata

    ' Silinecek blokta GoTo etiketi: etiket baþka yerde kullanýlýyorsa uyarý
    If FLAG_ESKI_YAZICI Then
        Dim lKopya As Long
        For lKopya = 1 To 2
            If Printer.Copies = 0 Then GoTo Atla
        Next lKopya
Atla:
        Printer.EndDoc
    End If

    ' Kýsa devre yok: False And Fonksiyon() -> fonksiyon silinir, uyarý verilmeli
    If FLAG_B And KaydetVeKapat() Then
        Unload Me
    End If

    Unload Me
    Exit Sub
Hata:
    If Err.Number = 482 Then GoTo Atla
    MsgBox "Hata: " & Err.Description
End Sub

Private Function fLisansVar() As Boolean
    fLisansVar = (Len(gsKullanici) > 0)
End Function
