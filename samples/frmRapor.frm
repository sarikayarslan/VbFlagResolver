VERSION 5.00
Begin VB.Form frmRapor
   Caption         =   "Rapor Önizleme"
   ClientHeight    =   6000
   ClientLeft      =   60
   ClientTop       =   345
   ClientWidth     =   8000
   LinkTopic       =   "Form1"
   ScaleHeight     =   6000
   ScaleWidth      =   8000
   StartUpPosition =   1  'CenterOwner
   Begin VB.ListBox lstSatirlar
      Height          =   5130
      Left            =   120
      TabIndex        =   0
      Top             =   120
      Width           =   7695
   End
End
Attribute VB_Name = "frmRapor"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' Bu formda bayrak koþulu yok; dosya bayt düzeyinde aynen kopyalanmalý.
Private Sub Form_Load()
    Dim i As Integer
    For i = 1 To 10
        If i Mod 2 = 0 Then
            lstSatirlar.AddItem "Çift satýr " & i
        ElseIf i = 7 Then
            lstSatirlar.AddItem "Yedi"
        Else
            lstSatirlar.AddItem "Tek satýr " & i
        End If
    Next i
End Sub
