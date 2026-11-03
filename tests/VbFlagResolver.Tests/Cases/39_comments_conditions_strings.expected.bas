Sub Kosullar()
    ' 1) Yorum içinde If / End If / Else geçer: blok sayýlmamalý
    ' If F1 Then
    ' End If
    Rem If F1 Then Else End If
    rem küçük harfli Rem
    REM büyük harfli REM
    x = 1: Rem ':' sonrasý Rem
    ' 2) Bilinmiyor koþul sadeleþir; satýr sonu yorumu yerinde kalýr
    If y Then ' sadeleþen koþul yorumu
        g = 1
    End If
    ' 3) Ýlk dal silinir, ElseIf -> If; ElseIf yorumu kalýr
    If z Then ' elseif yorumu If'e taþýnmalý
        h = 2
    End If
    ' 4) Bilinmiyor daldan sonra True ElseIf -> Else; yorumu kalýr
    If w Then ' bilinmiyor dal
        i = 1
    Else ' else olacak dal yorumu
        i = 2
    End If
End Sub

Sub Stringler()
    ' 5) Kesme iþareti string içinde: yorum deðil
    s = "it's" ' gerçek yorum "týrnaklý"
    t = "a ' b" ' yorum: If F1 Then
    u = """" ' çift týrnak
    ' 6) Rem ile baþlayan tanýmlayýcý yorum deðil
    lst.RemoveItem 0
    Remark = 2
End Sub
