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

Imports System.ComponentModel
Imports System.IO
Imports System.Security
Imports System.Text
Imports System.Threading
Imports Microsoft.VisualBasic.FileIO
Imports Net.Bertware.BukkitGUI.Core
Imports Timer = System.Timers.Timer

Namespace MCInterop
    Public Class execStartArgs
        Public executable As String
        Public arguments As String

        Public Sub New(executable As String, arguments As String)
            Me.executable = executable
            Me.arguments = arguments
        End Sub
    End Class

    Public Class javaStartArgs
        Public executable As String = ""
        Public jar As String = ""
        Public switches As String = ""
        Public args As String = ""
        Public custom_arg As String = ""
        Public custom_switch As String = ""

        Public Sub New()
            Me.executable = ""
            Me.args = ""
            Me.jar = ""
            Me.switches = ""
            Me.custom_arg = ""
            Me.custom_switch = ""
        End Sub

        Public Sub New(executable As String, flags As String, jar As String, switches As String)
            Me.executable = executable
            Me.args = flags
            Me.jar = jar
            Me.switches = switches
            Me.custom_arg = ""
            Me.custom_switch = ""
        End Sub

        Public Sub New(jre As Object, minram As UInt32, maxram As UInt32, jar As String,
                       Optional ByVal CustomArgs As String = "", Optional ByVal CustomSwitches As String = "",
                       Optional ByVal server As McInteropType = McInteropType.paper)
            If minram < 16 Then minram = 16
            If maxram < 16 Then maxram = 16

            Me.executable = "java"

            ' Для Paper и флагов Aikar's критически важно, чтобы Xms был равен Xmx
            If server = server.McInteropType.paper Then
                minram = maxram
            End If

            Me.args = "-Xms" & minram & "M -Xmx" & maxram & "M "
            Me.jar = jar
            Me.custom_arg = CustomArgs
            Me.custom_switch = CustomSwitches

            Select Case server
                Case server.McInteropType.paper
                    ' Флаги оптимизации Aikar's Flags для Paper
                    Me.args += "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 " &
                               "-XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC -XX:+AlwaysPreTouch " &
                               "-XX:G1NewSizePercent=30 -XX:G1MaxNewSizePercent=40 -XX:G1HeapRegionSize=8m " &
                               "-XX:G1ReservePercent=20 -XX:G1HeapWastePercent=5 -XX:G1MixedGCCountTarget=4 " &
                               "-XX:InitiatingHeapOccupancyPercent=15 -XX:G1MixedGCLiveThresholdPercent=90 " &
                               "-XX:SurvivingRatio=32 -XX:+PerfDisableSharedMem -XX:MaxTenuringThreshold=1 " &
                               "-Dusing.aikars.flags=https://emc.gs -Daikars.new.flags=true "

                    ' Отключаем ANSI-цвета и JLine, чтобы GUI корректно читал лог
                    Me.args += "-Dterminal.jline=false -Dterminal.ansi=false -Duser.language=en "
                    Me.switches = "nogui"
            End Select
        End Sub

        Public ReadOnly Property buildArgs() As String
            Get
                Return (args & " " & custom_arg & " " & "-jar """ & jar & """ " & Me.switches & " " & custom_switch).
                        Replace("   ", " ").Replace("  ", " ").Trim
            End Get
        End Property
    End Class

    Public Module server
        Public Enum McInteropType
            ' Оставляем только Paper и базовые системные типы
            paper ' Paper server
            remote ' Remote server connection
            exexcutable ' General executable server
            java ' General java server
        End Enum

        Private _running As Boolean,
                _serverout As Stream,
                _serverin As Stream,
                _servererror As Stream,
                _timerunning As TimeSpan,
                _playerlist As List(Of Player)

        Private _current_type As McInteropType
        Private WithEvents tmrcounttime As Timers.Timer
        Private WithEvents _host As Process
        Private csw As StreamWriter

        Private _remote_obj As RemoteServerBase

        ''' <summary>
        '''     Indicates if the server is running or not
        ''' </summary>
        Public ReadOnly Property running As Boolean
            Get
                Return _running
            End Get
        End Property

        ''' <summary>
        '''     The process that hosts the server
        ''' </summary>
        Public ReadOnly Property host As Process
            Get
                Return _host
            End Get
        End Property

        ''' <summary>
        '''     The standardOut stream of the process
        ''' </summary>
        Public ReadOnly Property serverOut As Stream
            Get
                Return _serverout
            End Get
        End Property

        ''' <summary>
        '''     The standardError stream of the server
        ''' </summary>
        Public ReadOnly Property serverError As Stream
            Get
                Return _servererror
            End Get
        End Property

        ''' <summary>
        '''     The standardIn stream of the server, used for sending of commands etc.
        ''' </summary>
        Public ReadOnly Property serverIn As Stream
            Get
                Return _serverin
            End Get
        End Property

        ''' <summary>
        '''     The time that has elapsed since the start of the server
        ''' </summary>
        Public ReadOnly Property timeRunning As TimeSpan
            Get
                Return _timerunning
            End Get
        End Property

        ''' <summary>
        '''     The list of currently online players
        ''' </summary>
        Public Property playerList As List(Of Player)
            Get
                Return _playerlist
            End Get
            Set(value As List(Of Player))
                _playerlist = value
            End Set
        End Property

        Public ReadOnly Property RemoteServerObject() As RemoteServerBase
            Get
                Return _remote_obj
            End Get
        End Property

        Public ReadOnly Property CurrentServerType As McInteropType
            Get
                Return _current_type
            End Get
        End Property

        Public ReadOnly Property playerNameList As List(Of String)
            Get
                If _playerlist Is Nothing Then Return Nothing
                Dim l As New List(Of String)
                For Each pl As Player In _playerlist
                    l.Add(pl.name)
                Next
                Return l
            End Get
        End Property

        Public Function GetPlayerByName(name As String) As Player
            Dim p As Player = Nothing
            For Each Pl As Player In playerList
                If Pl.name = name Then p = Pl
            Next
            Return p
        End Function

        Public Event ServerStarting()
        Public Event ServerStarted()
        Public Event ServerStopped()
        Public Event ServerStopping()
        ''' <summary>
        '''     Start a new Paper server, using Java
        ''' </summary>
        Public Function StartServer(Jsa As javaStartArgs, type As McInteropType) As UInt64
            Dim p As New Process
            Try
                _current_type = type
                Log(livebug.loggingLevel.Fine, "Server", "Starting Paper server (java)", "server")
                Log(loggingLevel.Fine, "Server", "Executable:" & Jsa.executable, "Server-StartJavaServer")
                Log(loggingLevel.Fine, "Server", "Jar:" & Jsa.jar, "Server-StartJavaServer")
                Log(loggingLevel.Fine, "Server", "Options:" & Jsa.args, "Server-StartJavaServer")
                Log(loggingLevel.Fine, "Server", "Flags:" & Jsa.switches, "Server-StartJavaServer")
                Log(loggingLevel.Fine, "Server", "Total argument:" & Jsa.buildArgs, "Server-StartJavaServer")

                ' Проверка пути к Java
                If Jsa.executable Is Nothing OrElse Jsa.executable = "" OrElse Not Jsa.executable.EndsWith(".exe") OrElse
                    FileSystem.FileExists(Jsa.executable) = False Then
                    MessageBox.Show(
                        Lr("The java path could not be found, server start aborted") & vbCrLf &
                        Lr("Please select the correct java version in the superstart tab"), Lr("Could not start server"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Log(loggingLevel.Warning, "Server", "File not found, server start cancelled:" & Jsa.executable)
                    Return 0
                End If

                RaiseEvent ServerStarting()
                Thread.Sleep(50)

                With p.StartInfo
                    .FileName = Jsa.executable
                    .Arguments = Jsa.buildArgs
                    .CreateNoWindow = True
                    .ErrorDialog = False
                    .RedirectStandardError = True
                    .RedirectStandardInput = True
                    .RedirectStandardOutput = True
                    .StandardErrorEncoding = Encoding.UTF8
                    .StandardOutputEncoding = Encoding.UTF8
                    .UseShellExecute = False
                    .WorkingDirectory = ServerRoot
                End With

                Log(loggingLevel.Fine, "Server", "Starting process")
                If p.StartInfo.FileName IsNot Nothing Then
                    p.Start()
                Else
                    MessageBox.Show(Lr("Could not start the process - no executable given"),
                                    Lr("Could not start server"), MessageBoxButtons.OK, MessageBoxIcon.Error)
                End If

                Log(loggingLevel.Fine, "Server", "Process started")
                _host = p
                ServerStart_Common()
                Log(loggingLevel.Fine, "Server", "Paper server started successfully")
                RaiseEvent ServerStarted()

                If p IsNot Nothing AndAlso p.Id > 0 Then
                    Return (p.Id) Else Return 0
            Catch winex As Win32Exception
                MessageBox.Show(Lr("The server could not be started. Try running the GUI as administrator"), Lr("File I/O error"), MessageBoxButtons.OK, MessageBoxIcon.Error)
                Log(loggingLevel.Warning, "Server", "Security error in Startserver (java)! " & winex.Message)
                If p IsNot Nothing Then Return (p.Id) Else Return 0
            Catch ioex As IOException
                MessageBox.Show(Lr("The server could not be started. Try running the GUI as administrator"), Lr("File I/O error"), MessageBoxButtons.OK, MessageBoxIcon.Error)
                Log(loggingLevel.Warning, "Server", "Security error in Startserver (java)! " & ioex.Message)
                If p IsNot Nothing Then Return (p.Id) Else Return 0
            Catch pex As SecurityException
                MessageBox.Show(Lr("The server could not be started. Try running the GUI as administrator"), Lr("Insufficient rights"), MessageBoxButtons.OK, MessageBoxIcon.Error)
                Log(loggingLevel.Warning, "Server", "Security error in Startserver (java)! " & pex.Message)
                If p IsNot Nothing Then Return (p.Id) Else Return 0
            Catch ex As Exception
                Log(loggingLevel.Severe, "Server", "Severe error in Startserver (java)! " & ex.Message)
                If p IsNot Nothing Then Return (p.Id) Else Return 0
            End Try
        End Function

        ''' <summary>
        '''     Start a new executable Paper server (binary)
        ''' </summary>
        Public Function StartServer(Esa As execStartArgs, type As McInteropType) As UInt16
            Try
                _current_type = type
                Log(loggingLevel.Fine, "Server", "Starting Paper server (executable)")

                If Not FileSystem.FileExists(Esa.executable) Then
                    MessageBox.Show(Lr("The program couldn't find this executable: " & Esa.executable & vbCrLf & "Server start cancelled"), Lr("File not found"), MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Log(loggingLevel.Warning, "Server", "File not found, server start cancelled:" & Esa.executable)
                    Return 0
                End If

                RaiseEvent ServerStarting()
                Thread.Sleep(50)

                Dim p As New Process
                With p.StartInfo
                    .FileName = Esa.executable
                    .Arguments = Esa.arguments
                    .CreateNoWindow = True
                    .ErrorDialog = False
                    .RedirectStandardError = True
                    .RedirectStandardInput = True
                    .RedirectStandardOutput = True
                    Console.InputEncoding = Encoding.GetEncoding(ServerEncoding)
                    .StandardErrorEncoding = Encoding.GetEncoding(ServerEncoding)
                    .StandardOutputEncoding = Encoding.GetEncoding(ServerEncoding)
                    .UseShellExecute = False
                    .WorkingDirectory = ServerRoot
                End With
                Log(loggingLevel.Fine, "Server", "Starting process")
                p.Start()
                Log(loggingLevel.Fine, "Server", "Process started")
                _host = p
                ServerStart_Common()
                Log(loggingLevel.Fine, "Server", "Server started successfully")
                RaiseEvent ServerStarted()
                If p IsNot Nothing AndAlso p.Id > 0 AndAlso p.HasExited = False Then Return (p.Id) Else Return 0
            Catch ex As Exception
                Log(loggingLevel.Severe, "Server", "Severe error in Startserver (executable)!", ex.Message)
                Return Process.GetCurrentProcess.Id
            End Try
        End Function

        ''' <summary>
        '''     Start an embedded remote Paper server connection
        ''' </summary>
        Public Function StartServer(Rm As RemoteServerBase) As UInt16
            Try
                _current_type = McInteropType.remote
                Log(loggingLevel.Fine, "Server", "Starting server (Remote)")
                RaiseEvent ServerStarting()

                _remote_obj = Rm
                _running = True
                _host = Process.GetCurrentProcess

                Log(loggingLevel.Fine, "Server", "Initializing handling for new remote server")
                _playerlist = New List(Of Player)
                _serverout = Nothing
                _servererror = Nothing
                _serverin = Nothing
                _timerunning = New TimeSpan(0)

                tmrcounttime = New Timer
                tmrcounttime.Interval = 1000
                tmrcounttime.AutoReset = True
                tmrcounttime.Enabled = True
                tmrcounttime.Start()
                Log(loggingLevel.Fine, "Server", "New remote server initialized")

                Rm.Run()
                csw = Nothing
                RaiseEvent ServerStarted()

                Log(loggingLevel.Fine, "Server", "Server started successfully")
                Return Process.GetCurrentProcess.Id
            Catch ex As Exception
                Log(loggingLevel.Severe, "Server", "Severe error in Startserver (remote)!", ex.Message)
                Return Process.GetCurrentProcess.Id
            End Try
        End Function
        Public Sub StopServer()
            Log(loggingLevel.Info, "server", "StopServer() was called")
            RaiseEvent ServerStopping()
            ' Даем современному Paper время на асинхронное сохранение мира и чанков
            Thread.Sleep(500)
            If CurrentServerType = McInteropType.remote Then
                _ServerStopped()
            Else
                SendCommand("stop", True)
            End If
            Log(loggingLevel.Info, "server", "StopServer() was executed")
        End Sub

        Private Sub _ServerStopped() Handles _host.Exited
            Try
                Log(loggingLevel.Info, "server", "The server has stopped... Cleaning up...")

                If tmrcounttime IsNot Nothing Then
                    tmrcounttime.Enabled = False
                    _running = False
                End If

                _host = Nothing
                _serverout = Nothing
                _servererror = Nothing
                _serverin = Nothing
                _playerlist = Nothing
                _remote_obj = Nothing
                RaiseEvent ServerStopped()
            Catch ex As Exception
                Log(loggingLevel.Severe, "Server", "Error in _serverstopped! " & ex.Message)
            End Try
            Log(loggingLevel.Info, "server", "The server has stopped... Cleanup finished")
        End Sub

        ''' <summary>
        '''     Send a command to the server
        ''' </summary>
        Public Function SendCommand(command As String, Optional suspressErrors As Boolean = False) As Boolean
            Try
                If CurrentServerType <> McInteropType.remote Then
                    If _host IsNot Nothing Then
                        If csw Is Nothing AndAlso _host.Id <> Process.GetCurrentProcess.Id Then _
                            csw = _host.StandardInput

                        Dim buffer() As Byte = Encoding.GetEncoding(ServerEncoding).GetBytes(command & vbCr)
                        csw.BaseStream.Write(buffer, 0, buffer.Length)
                        csw.BaseStream.Flush()

                        Return True
                    Else
                        If Not suspressErrors Then _
                            MessageBox.Show(Lr("The server should be running before you can send commands."), Lr("Could not send command"), MessageBoxButtons.OK, MessageBoxIcon.Error)
                        Return False
                    End If
                Else
                    If RemoteServerObject IsNot Nothing AndAlso RemoteServerObject.StandardIn IsNot Nothing Then _
                        RemoteServerObject.StandardIn.write(command) : Return True
                End If
            Catch ex As Exception
                If Not suspressErrors Then _
                    MessageBox.Show(Lr("The command could not be sent due to an error. See the log file for more information"), Lr("Could not send command"), MessageBoxButtons.OK, MessageBoxIcon.Error)
                Log(loggingLevel.Severe, "Server", "Could not send command """ & command & """ exception", ex.Message)
                Return False
            End Try
            Return False
        End Function

        Private Sub ServerStart_Common()
            Try
                Log(loggingLevel.Fine, "Server", "Initializing handling for new server")
                _playerlist = New List(Of Player)
                _serverout = _host.StandardOutput.BaseStream
                _servererror = _host.StandardError.BaseStream
                _serverin = _host.StandardInput.BaseStream
                _timerunning = New TimeSpan(0)

                tmrcounttime = New Timer
                tmrcounttime.Interval = 1000
                tmrcounttime.AutoReset = True
                tmrcounttime.Enabled = True
                tmrcounttime.Start()

                csw = _host.StandardInput

                Log(loggingLevel.Fine, "Server", "New server initialized")
                _running = True
            Catch ex As Exception
                Log(loggingLevel.Severe, "Server", "Severe error in ServerStart_common! ", ex.Message)
            End Try
        End Sub

        Private Sub Counttime() Handles tmrcounttime.Elapsed
            _timerunning = _timerunning.Add(New TimeSpan(0, 0, 1))
        End Sub
    End Module
End Namespace
