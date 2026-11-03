Sub Acilan()
    ' 1) True dal: içindeki yorumlar korunur ve girintisi azalýr
    If T1 Then ' açýlýþ yorumu: korunmalý
        ' gövde yorumu (ðüþýöç ÝÐÜÞÖÇ)
        a = 1   ' satýr sonu yorumu
'sütun 0 yorumu: hatalý girinti, dokunulmamalý
	' sekmeli yorum
        '
    End If ' kapanýþ yorumu: korunmalý
End Sub

Sub Silinen()
    ' 2) False dal: içindeki yorumlar da silinir
    If F1 Then ' silinen bloðun baþlýk yorumu
        ' silinecek yorum
        b = 1
    End If ' silinen bloðun kapanýþ yorumu
    c = 1
End Sub

Sub ElseAcilir()
    ' 3) If False, Else açýlýr: If, Else ve End If yorumlarý korunur
    If F1 Then ' if yorumu
        d = 1 ' silinecek
    Else ' else yorumu
        ' else gövde yorumu
        d = 2
    End If ' end if yorumu
End Sub

Sub ElseIfAcilir()
    ' 4) Ýlk dal silinir, ElseIf True açýlýr; arada yorum satýrlarý
    If F1 Then ' a
        e = 1
        ' F1 dalýnýn son yorumu
    ElseIf T1 Then ' b
        ' T1 dalýnýn ilk yorumu
        e = 2
    ElseIf T2 Then ' silinecek dal yorumu
        e = 3
    End If ' c
End Sub

Sub SadeceYorum()
    ' 5) Gövdesi yalnýzca yorum olan bloklar
    If T1 Then
        ' yalnýzca yorum (kalmalý)
    End If
    If F1 Then
        ' yalnýzca yorum (silinmeli)
    End If
End Sub

Sub IcIce()
    ' 6) Ýç içe: dýþ True, iç Bilinmiyor; yorumlar iki seviyede
    If T1 Then ' dýþ
        ' dýþ gövde
        If x Then ' iç (Bilinmiyor, kalýr)
            ' iç gövde
            f = 1
        End If ' iç son
    End If ' dýþ son
End Sub
