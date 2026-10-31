VERSION 5.00
Begin VB.Form frmAyarlar
   Caption         =   "Ayarlar"
   ClientHeight    =   3195
   ClientLeft      =   60
   ClientTop       =   345
   ClientWidth     =   4680
   LinkTopic       =   "Form1"
   ScaleHeight     =   3195
   ScaleWidth      =   4680
   StartUpPosition =   3  'Windows Default
   Begin VB.CheckBox chkCalisma
      Caption         =   "Çalýþma zamaný özelliði açýk"
      Height          =   375
      Left            =   240
      TabIndex        =   1
      Top             =   240
      Width           =   4215
   End
   Begin VB.CommandButton cmdTamam
      Caption         =   "Tamam"
      Default         =   -1  'True
      Height          =   495
      Left            =   3240
      TabIndex        =   0
      Top             =   2520
      Width           =   1215
   End
End
Attribute VB_Name = "frmAyarlar"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

Private Sub cmdTamam_Click()
    ' Bayraða atama: gbFlagCalismaZamani Bilinmiyor sayýlmalý ve raporlanmalý
    gbFlagCalismaZamani = (chkCalisma.Value = vbChecked)
    Unload Me
End Sub

Private Sub Form_Load()
    chkCalisma.Value = IIf(gbFlagCalismaZamani, vbChecked, vbUnchecked)

    ' Satýr devamlý koþul
    If FLAG_LOG_DETAY And _
       chkCalisma.Enabled Then
        chkCalisma.ToolTipText = "Ayrýntýlý günlük açýk"
    End If

    ' Parantezli karýþýk koþul
    If (FLAG_A Or FLAG_B) And (Not FLAG_C Or Me.Visible) Then
        Me.Caption = "Ayarlar - geliþtirici"
    End If

    ' Xor / Eqv
    If FLAG_A Xor chkCalisma.Value = vbChecked Then
        Me.BackColor = vbYellow
    End If
    If FLAG_B Eqv Me.Visible Then
        Me.BackColor = vbWhite
    End If

    ' Modül adýyla nitelenmiþ bayrak
    If mDevelopmentFlags.FLAG_GIZLI Then
        Me.Caption = "Gizli mod"
    End If

    ' Yorumlu ve ':' içeren satýrlar korunmalý
    Dim s As String: s = "If FLAG_A Then 'string içinde, dokunulmamalý"
    If FLAG_YEREL_TEST Then ' yerel test
        s = s & ": test"   ' ':' string içinde
    End If ' yerel test sonu
End Sub
