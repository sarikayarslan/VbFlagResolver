Sub Test()
    For i = 1 To 3
        Select Case i
            Case 1
                a = 1
            Case Else
                With obj
                    .b = 3
                End With
        End Select
    Next i
    Do While x < 10
        If x > 5 Then Exit Do
        x = x + 1
    Loop
End Sub
