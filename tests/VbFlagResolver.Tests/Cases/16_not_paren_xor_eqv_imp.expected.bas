Sub Test()
    a = 1
    If y Then
        a = 3
    End If
    If Not (x > 0) Then
        a = 4
    End If
    If Not y Then
        a = 5
    End If
    If Not y Then
        a = 6
    End If
    a = 7
    If (x Or y) Then
        a = 8
    End If
End Sub
