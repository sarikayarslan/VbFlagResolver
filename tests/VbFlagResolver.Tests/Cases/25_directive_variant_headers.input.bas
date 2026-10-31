Sub Test()
#If DEBUG_MODE Then
    If T1 And x Then
#Else
    If F1 Then
#End If
        a = 1
    End If
#If YENI Then
    If x Then
#Else
    If T2 Then
#End If
        b = 1
    End If
End Sub
