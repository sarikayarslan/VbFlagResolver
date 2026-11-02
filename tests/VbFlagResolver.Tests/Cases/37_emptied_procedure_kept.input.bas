Private Sub cmdEski_Click()
    If F1 Then
        MsgBox "eski"
    End If
End Sub

Public Function EskiHesap( _
    ByVal n As Long) As Long
#If YENI Then
    EskiHesap = n
#End If
End Function

Private Sub Bos()
End Sub

Private Sub Dolu()
    If F1 Then x = 1
    y = 2
End Sub
