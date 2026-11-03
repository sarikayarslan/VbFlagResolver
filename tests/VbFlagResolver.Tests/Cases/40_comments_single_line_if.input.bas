Sub TekSatir()
    If T1 Then a = 1 ' True: yorum kalýr
    If F1 Then a = 2 Else a = 3 ' False + Else: yorum kalýr
    If F1 Then a = 4 ' False, Else yok: satýr silinir
    If T1 Then a = 5 Else a = 6 ' True + Else
    x = 1: If F1 Then a = 7 ' önek kalýr, yorum kalýr
    If y And T1 Then a = 8 ' sadeleþir
    If T1 Then a = 9: Rem Then kýsmýnda Rem
    If T1 Then ' blok If, tek satýrlýk deðil
        a = 10
    End If
End Sub
