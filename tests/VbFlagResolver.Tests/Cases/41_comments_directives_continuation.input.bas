Sub Yonergeler()
#If DEBUG_MODE Then ' debug yorumu
    ' debug dalý yorumu
    a = 1
#Else ' release yorumu
    a = 2
#End If ' yönerge sonu
#If YENI Then
    ' silinecek yönerge dalý
#End If
End Sub

Sub Devam()
    ' 1) Satýr devamlý baþlýðýn son satýrýnda yorum
    If T1 And _
       T2 Then ' devamlý baþlýk yorumu
        b = 1
    End If
    ' 2) VB'de "_" ile biten yorum bir sonraki satýra devam eder _
    If F1 Then c = 1
    d = 1
End Sub

Sub IfIcindeYonerge()
    ' 3) Açýlan If bloðunun içinde yorumlu #If; yorumlar bloðu bozmamalý
    If T1 Then
#If DEBUG_MODE Then ' iç debug yorumu
        e = 1
#Else
        e = 2
#End If ' iç yönerge sonu
    End If
    If F1 Then
#If DEBUG_MODE Then ' silinen bloktaki yönerge yorumu
        f = 1
#End If
    End If
End Sub
