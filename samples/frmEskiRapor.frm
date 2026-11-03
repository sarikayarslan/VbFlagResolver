VERSION 5.00
Begin VB.Form frmEskiRapor 
   Caption         =   "Eski Rapor (yalnýzca eski yazýcýyla)"
   ClientHeight    =   2400
   ClientLeft      =   60
   ClientTop       =   345
   ClientWidth     =   4680
   LinkTopic       =   "Form1"
   ScaleHeight     =   2400
   ScaleWidth      =   4680
   StartUpPosition =   1  'CenterOwner
   Begin VB.CommandButton cmdKapat 
      Caption         =   "Kapat"
      Height          =   375
      Left            =   3360
      TabIndex        =   0
      Top             =   1920
      Width           =   1215
   End
End
Attribute VB_Name = "frmEskiRapor"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' Yalnýzca frmSiparis içindeki FLAG_ESKI_YAZICI bloðundan açýlýr; o blok silinince
' "Kullanýlmayan formlarý projeden çýkar" seçeneðiyle projeden çýkarýlmalý.

Private Sub cmdKapat_Click()
    Unload Me
End Sub
