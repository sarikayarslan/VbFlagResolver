Sub Test()
    If T1 Then Init: x = 1
    If T1 Then 100 Else 200
    If F1 Then 100 Else 200
    If T1 Then Stop: x = 2
    x = 3: If T1 Then Else y = 2
100:
200:
End Sub
