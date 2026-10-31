Sub Test()
    If Not F1 Then
        a = 1
    End If
    If Not T1 Then
        a = 2
    End If
    If (T1 Or x) And (y Or F1) Then
        a = 3
    End If
    If T1 Xor x > 0 Then
        a = 4
    End If
    If F1 Eqv y Then
        a = 5
    End If
    If Not (T1 And y) Then
        a = 6
    End If
    If F1 Imp x Then
        a = 7
    End If
    If (x Or y) And T1 Then
        a = 8
    End If
End Sub
