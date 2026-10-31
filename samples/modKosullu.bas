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

Public Sub Karsilastirmalar()
    ' Sabit deðeri ile karþýlaþtýrmalar (VB kurallarý): DEBUG_MODE=1 verildiðinde
    ' 1, 2 ve 4 doðru; 3 yanlýþ olur. DEBUG_MODE=True (-1) verilirse 1 hariç hepsi beklendiði gibi.
#If DEBUG_MODE = 1 Then
    Debug.Print "1: DEBUG_MODE = 1"
#End If
#If DEBUG_MODE <> 0 Then
    Debug.Print "2: DEBUG_MODE <> 0"
#End If
    ' True = -1 olduðu için DEBUG_MODE = 1 iken #Else dalý kalýr
#If DEBUG_MODE = True Then
    Debug.Print "3: DEBUG_MODE = True"
#Else
    Debug.Print "3: DEBUG_MODE True (-1) deðil"
#End If
    ' Not bit düzeyinde çalýþýr: Not 1 = -2 (sýfýrdan farklý) ; DEBUG_MODE = 1 iken de bu dal kalýr
#If Not DEBUG_MODE Then
    Debug.Print "4: Not DEBUG_MODE"
#End If
End Sub
