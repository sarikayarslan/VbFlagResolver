VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
  Persistable = 0  'NotPersistable
  DataBindingBehavior = 0  'vbNone
  DataSourceBehavior  = 0  'vbNone
  MTSTransactionMode  = 0  'NotAnMTSObject
END
Attribute VB_Name = "clsYazici"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = True
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

' Yazýcý sýnýfý: Property, Enum, Type ve olay içinde bayraklar
Public Enum YaziciTipi
    ytMatris = 0
    ytLazer = 1
End Enum

Private Type YaziciAyar
    Ad As String
    Kopya As Integer
End Type

Private mAyar As YaziciAyar
Public Event Yazildi(ByVal Sayfa As Long)

Public Property Get Tip() As YaziciTipi
    If FLAG_ESKI_YAZICI Then Tip = ytMatris Else Tip = ytLazer
End Property

Public Property Let KopyaSayisi(ByVal n As Integer)
    If FLAG_STOK_UYARI Then
        If n < 1 Then Err.Raise 5, "clsYazici", "Kopya sayýsý en az 1 olmalý"
    End If
    mAyar.Kopya = n
End Property

Public Sub Yaz(ByVal sMetin As String)
    Dim nSayfa As Long
    If FLAG_ESKI_YAZICI Then
        ' eski matris yazýcý yolu
        Dim sPort As String
        sPort = "LPT1"
        Open sPort For Output As #1
        Print #1, sMetin
        Close #1
    ElseIf gbFlagRaporV3 Then
        Printer.Print sMetin
        nSayfa = Printer.Page
        Printer.EndDoc
    Else
        Printer.Print sMetin: Printer.EndDoc
    End If
    RaiseEvent Yazildi(nSayfa)
End Sub

Private Sub Class_Initialize()
    mAyar.Ad = "Varsayýlan"
    If FLAG_KARMA = False Then mAyar.Kopya = 1
End Sub
