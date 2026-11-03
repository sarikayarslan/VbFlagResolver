Sub Acilan()
    ' 1) True dal: içindeki yorumlar korunur ve girintisi azalýr
    ' açýlýþ yorumu: korunmalý
    ' gövde yorumu (ðüþýöç ÝÐÜÞÖÇ)
    a = 1   ' satýr sonu yorumu
'sütun 0 yorumu: hatalý girinti, dokunulmamalý
	' sekmeli yorum
    '
    ' kapanýþ yorumu: korunmalý
End Sub

Sub Silinen()
    ' 2) False dal: içindeki yorumlar da silinir
    c = 1
End Sub

Sub ElseAcilir()
    ' 3) If False, Else açýlýr: If, Else ve End If yorumlarý korunur
    ' if yorumu
    ' else yorumu
    ' else gövde yorumu
    d = 2
    ' end if yorumu
End Sub

Sub ElseIfAcilir()
    ' 4) Ýlk dal silinir, ElseIf True açýlýr; arada yorum satýrlarý
    ' a
    ' b
    ' T1 dalýnýn ilk yorumu
    e = 2
    ' c
End Sub

Sub SadeceYorum()
    ' 5) Gövdesi yalnýzca yorum olan bloklar
    ' yalnýzca yorum (kalmalý)
End Sub

Sub IcIce()
    ' 6) Ýç içe: dýþ True, iç Bilinmiyor; yorumlar iki seviyede
    ' dýþ
    ' dýþ gövde
    If x Then ' iç (Bilinmiyor, kalýr)
        ' iç gövde
        f = 1
    End If ' iç son
    ' dýþ son
End Sub
