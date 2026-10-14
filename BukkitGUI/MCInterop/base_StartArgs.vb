'=============================================================================
'
' This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
' If a copy of the MPL was not distributed with this file,
' you can obtain one at http://mozilla.org.
' 
' Source and compiled files may only be redistributed if they comply with
' the mozilla MPL2 license, and may not be monetized in any way,
' including but not limited to selling the software or distributing it through ad-sponsored channels.
'
' ©Bertware, visit http://bertware.net
'
'=============================================================================

Imports Net.Bertware.BukkitGUI.Core
Imports Net.Bertware.BukkitGUI.Utilities

Namespace MCInterop
    Public Class base_StartArgs
        Public Jre As Object
        Public MinRam As UInt32
        Public MaxRam As UInt32
        Public JarPath As String
        Public CustomArgs As String
        Public CustomSwitches As String
        Public ServerType As server.McInteropType

        ''' <summary>
        ''' Создает новый экземпляр аргументов запуска, по умолчанию настроенный под Paper
        ''' </summary>
        Public Sub New()
            Me.Jre = Nothing ' Примечание: в настройках GUI пользователя должна быть выбрана актуальная Java (17/21)
            Me.MinRam = 1024
            Me.MaxRam = 2048
            Me.JarPath = "paper.jar"
            Me.CustomArgs = ""
            Me.CustomSwitches = ""
            Me.ServerType = server.McInteropType.paper ' По умолчанию выставляем наш Paper
        End Sub

        Public Sub New(jre As javaAPI.javaVersion, minRam As UInt32, maxRam As UInt32, jarPath As String,
                       Optional ByVal customArgs As String = "", Optional ByVal customSwitches As String = "",
                       Optional ByVal serverType As server.McInteropType = server.McInteropType.paper)
            Me.Jre = jre
            Me.MinRam = minRam
            Me.MaxRam = maxRam
            Me.JarPath = jarPath
            Me.CustomArgs = customArgs
            Me.CustomSwitches = customSwitches
            Me.ServerType = serverType
        End Sub
    End Class
End Namespace
