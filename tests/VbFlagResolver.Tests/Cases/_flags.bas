Attribute VB_Name = "mFlags"
' Test bayraklari: T1/T2 = True, F1/F2 = False, U1 = Bilinmiyor (kosullu atama)
Option Explicit
Public Const T1 = True
Public Const T2 As Boolean = True
Public Const F1 = False
Global Const F2 As Boolean = False
Public U1 As Boolean

Public Sub InitFlags()
    If Command$ <> "" Then
        U1 = True
    End If
End Sub
