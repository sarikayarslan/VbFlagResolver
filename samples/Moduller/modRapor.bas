Attribute VB_Name = "modRapor"
Option Explicit
' Alt klasördeki modül: satýr numaralarý, GoSub, While/Wend, Do Until, #Const, ReDim

#Const RAPOR_TEST = 1

Public Function RaporSatirSayisi(ByVal sDosya As String) As Long
    Dim n As Long, aSatir() As String
10  If FLAG_YENI_RAPOR Then
20      n = 100
30  Else
40      n = 50
50  End If
    If FLAG_ESKI_MENU Then
        ReDim aSatir(1 To n)
        GoSub EskiHesap
    End If
    While n > 0 And FLAG_LOG_DETAY
        If FLAG_B Or (FLAG_A And n Mod 2 = 0) Then n = n - 2 Else n = n - 1
    Wend
    Do
        If Not (FLAG_Y Or FLAG_ESKI_FIYAT) Then
            Exit Do
        End If
    Loop Until n <= 0
#If RAPOR_TEST Then
    If FLAG_ESIT Then Debug.Print "rapor testi"
#Else
    If FLAG_ESIT Then Debug.Print "canlý"
#End If
    RaporSatirSayisi = n
    Exit Function
EskiHesap:
    n = n * 2
    Return
End Function

Public Sub RaporHazirla()
    Dim sBaslik As String
    sBaslik = "Rapor ""If FLAG_A Then"" baþlýðý"   ' çift týrnak içinde
    If FLAG_A Then
        If FLAG_C Then
            If FLAG_X Then
                If FLAG_TURETILMIS Then
                    If gbFlagMusteriV2 Then
                        If gbFlagCalismaZamani Then
                            sBaslik = sBaslik & " (çalýþma zamaný)"
                        End If
                    End If
                End If
            End If
        End If
    End If
    Select Case sBaslik
        Case "": If FLAG_B Then sBaslik = "boþ" Else sBaslik = "-"
        Case Else
            If FLAG_DEBUG_ARAYUZ Then
                Select Case Len(sBaslik)
                    Case Is > 10: sBaslik = Left$(sBaslik, 10)
                End Select
            End If
    End Select
    Rem If FLAG_ESKI_MENU Then bu bir Rem yorumu
    If FLAG_GOLGE Then Debug.Print "gölge: bilinmiyor kalmalý"
End Sub
