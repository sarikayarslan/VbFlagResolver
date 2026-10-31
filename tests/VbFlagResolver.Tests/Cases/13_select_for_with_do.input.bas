Sub Test()
    For i = 1 To 3
        Select Case i
            Case 1
                If T1 Then
                    a = 1
                End If
            Case Else
                With obj
                    If F1 Then
                        .b = 2
                    Else
                        .b = 3
                    End If
                End With
        End Select
    Next i
    Do While x < 10
        If T2 And x > 5 Then Exit Do
        x = x + 1
    Loop
End Sub
