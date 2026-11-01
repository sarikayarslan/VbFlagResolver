Sub Test()
    On Error GoTo Hata
    If F1 Then
        x = 1
Atla:
        Printer.EndDoc
    End If
    Exit Sub
Hata:
    If Err.Number = 482 Then GoTo Atla
End Sub

Sub Diger()
    If F1 Then
        Dim i As Integer
Son:
        i = 1
    End If
    i = 2
End Sub
