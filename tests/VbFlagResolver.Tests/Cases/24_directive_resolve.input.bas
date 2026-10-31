Sub Test()
#If DEBUG_MODE Then
    a = 1
#Else
    a = 2
#End If
#If YENI Then
    b = 1
#ElseIf DEBUG_MODE = 1 Then
    b = 2
#Else
    b = 3
#End If
#If YENI Then
    c = 1
#End If
    d = 1
End Sub
