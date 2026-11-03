Attribute VB_Name = "modGenel"
Option Explicit

Public Sub Main()
    InitFlags

    If FLAG_KDV_YENI Then
        ' Yeni KDV hesabý
        KdvOraniAyarla 20
    ElseIf FLAG_ESKI_MENU Then
        KdvOraniAyarla 18
    End If

    ' Bayrak koþul dýþýnda kullanýlýyor: rapor uyarýsý
    Debug.Print "Yeni rapor: "; FLAG_YENI_RAPOR

    frmMain.Show
End Sub

Public Sub KdvOraniAyarla(ByVal iOran As Integer)
    If Not FLAG_KDV_YENI Then
        Debug.Print "Eski oran"
    End If
    If Not gbFlagHicAtanmamis Then Debug.Print "Atanmamýþ bayrak False sayýldý"
End Sub

' Formlarý açan yordamlar (kullanýlmayan form analizi bunlara bakar)
Public Sub AyarlariAc()
    frmAyarlar.Show vbModal
End Sub

Public Sub RaporAc()
    frmRapor.Show
End Sub

Public Sub SiparisAc()
    Load frmSiparis
    frmSiparis.Show
End Sub
