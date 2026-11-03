Sub Kosullar()
    ' 1) Yorum içinde If / End If / Else geçer: blok sayýlmamalý
    ' If F1 Then
    ' End If
    Rem If F1 Then Else End If
    rem küçük harfli Rem
    REM büyük harfli REM
    x = 1: Rem ':' sonrasý Rem
    ' 2) Bilinmiyor koþul sadeleþir; satýr sonu yorumu yerinde kalýr
    If T1 And y Then ' sadeleþen koþul yorumu
        g = 1
    End If
    ' 3) Ýlk dal silinir, ElseIf -> If; ElseIf yorumu kalýr
    If F1 Then ' silinen ilk dal
        h = 1
    ElseIf z Then ' elseif yorumu If'e taþýnmalý
        h = 2
    End If
    ' 4) Bilinmiyor daldan sonra True ElseIf -> Else; yorumu kalýr
    If w Then ' bilinmiyor dal
        i = 1
    ElseIf T1 Then ' else olacak dal yorumu
        i = 2
    ElseIf T2 Then ' silinecek
        i = 3
    End If
End Sub

Sub Stringler()
    ' 5) Kesme iþareti string içinde: yorum deðil
    If T1 Then s = "it's" ' gerçek yorum "týrnaklý"
    If T1 Then
        t = "a ' b" ' yorum: If F1 Then
        u = """" ' çift týrnak
    End If
    ' 6) Rem ile baþlayan tanýmlayýcý yorum deðil
    If T1 Then lst.RemoveItem 0
    If F1 Then lst.RemoveItem 1 Else Remark = 2
End Sub
