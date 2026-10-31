Attribute VB_Name = "modKosullu"
Option Explicit

' Koþullu derleme örnekleri: Proje.vbp'de CondComp="DEBUG_MODE = 0"
#Const YEREL_LOG = 1

Public Sub Kaydet()
    ' #If/#Else içinde iki farklý If baþlýðý, ortak gövde ve End If
#If DEBUG_MODE Then
    If FLAG_LOG_DETAY And gbFlagDebugPanel Then
#Else
    If FLAG_LOG_DETAY Then
#End If
        Debug.Print "Kayýt baþladý"
    End If

#If DEBUG_MODE Then
    MsgBox "Test sürümü"
#ElseIf YEREL_LOG Then
    Debug.Print "Yerel günlük açýk"
#Else
    ' sessiz
#End If

    ' Tanýmsýz sabit: grup olduðu gibi kalýr, içi yine iþlenir
#If TANIMSIZ_SABIT Then
    If FLAG_ESKI_MENU Then
        Debug.Print "Eski menü"
    End If
    Debug.Print "Tanýmsýz dal"
#End If
End Sub
