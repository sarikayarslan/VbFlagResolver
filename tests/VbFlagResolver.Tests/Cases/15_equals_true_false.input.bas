Sub Test()
    If T1 = True Then
        a = 1
    End If
    If U1 = True And T1 Then
        a = 2
    End If
    If U1 = False And T2 Then
        a = 3
    End If
    If U1 <> True Or F1 Then
        a = 4
    End If
    If T1 <> False Then
        a = 5
    End If
    If x = True And T1 Then
        a = 6
    End If
    If F1 = True Then
        a = 7
    End If
End Sub
