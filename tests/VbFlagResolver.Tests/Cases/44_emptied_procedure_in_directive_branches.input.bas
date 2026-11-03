#If YENI Then
Sub X()
End Sub
#Else
Sub X()
    If F1 Then b = 1
End Sub
#End If

#If DEBUG_MODE Then
Sub Y()
End Sub
#Else
Sub Y()
    If F1 Then c = 1
End Sub
#End If
