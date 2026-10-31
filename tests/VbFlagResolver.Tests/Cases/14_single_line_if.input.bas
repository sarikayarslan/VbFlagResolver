Sub Test()
    If T1 Then a = 1
    If F1 Then a = 2
    If T1 Then a = 3 Else a = 4
    If F1 Then a = 5 Else a = 6 ' yorum
    If F1 Or x Then a = 7 Else a = 8
    If T1 Then a = 9: b = 10
    c = 1: If F1 Then d = 2
    c = 2: If T1 Then d = 3
Hata: If F1 Then Resume Next
End Sub
