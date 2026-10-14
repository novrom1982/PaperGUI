'=============================================================================
' This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
' If a copy of the MPL was not distributed with this file, you can obtain one at http://mozilla.org.
' ©Bertware, visit http://bertware.net
'=============================================================================

Imports System.IO
Imports System.Security
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports Microsoft.VisualBasic.FileIO
Imports Microsoft.Win32
Imports Net.Bertware.BukkitGUI.MCInterop
Imports Net.Bertware.BukkitGUI.TaskManager
Imports Net.Bertware.BukkitGUI.Utilities
Imports Net.Bertware.Controls
Imports Net.Bertware.Get
Imports Net.Bertware.Utilities
Imports Timer = System.Timers.Timer
Imports Net.Bertware.BukkitGUI.forms

Namespace Core
    Public Class Mainform
        Private _tmrUpdateStats As Timers.Timer
        Private _threadReadOut As Thread, _threadReadError As Thread
        Private _threadsWork As Boolean = True
        Private _initializeCompleted As Boolean = False

        Public Event ToTray()
        Public Event FromTray()

#If DEBUG Then
        Private ReadOnly _caption As String = "[DEBUG] PaperGUI v" & My.Application.Info.Version.ToString

        Private Sub Debughook(s As Object, e As KeyEventArgs) Handles Me.KeyUp, TabCtrlMain.KeyUp, ARTXTServerOutput.KeyUp
            If e.Control And e.KeyCode = Keys.E Then
                Dim output = InputBox("emulated console output", "debug emulator")
                hnd_handle_output(output)
            End If
        End Sub
#Else
        Private ReadOnly _caption As String = "PaperGUI v" & My.Application.Info.Version.ToString
#End If

        Public Usedfont As Font = New Font("Arial", 10)

        Const WaitCreationTimeout As UInt16 = 100
        Const MaxWaitCreationCycles As Byte = 100

        Private Sub mainform_Load(sender As Object, e As EventArgs) Handles Me.Load
            Me.Text = _caption
            MainWindowHandle = Me.Handle

            If IsRunningLight Then
                TabCtrlMain.TabPages.Remove(TabErrorLogging)
                TabCtrlMain.TabPages.Remove(TabServerOptions)
                TabCtrlMain.TabPages.Remove(TabPlugins)
                TabCtrlMain.TabPages.Remove(TabTaskManager)
                TabCtrlMain.TabPages.Remove(TabBackups)
                PanelPerformanceInfo.Visible = False
                Me.Size = New Size(850, 600)
            End If

            _tmrUpdateStats = New Timer
            set_handles()
            ARTXTServerOutput.AutoScrollDown = True

            Try
                superstart_init()
                info_load()
                Settings_init()
                text_options_init()
                _initializeCompleted = True
                autoserverstart_check()

                For Each argument As String In Environment.GetCommandLineArgs()
                    If (argument.Equals("-minimized")) Then
                        Mainform.TrayAlways = True
                        Mainform.TrayMinimize = True
                        Me.SendToTray()
                    End If
                    If (argument).Equals("-startserver") Then start_server()
                Next
            Catch ex As Exception
                Log(loggingLevel.Severe, "mainform", "Error: Could not initialize mainform", ex.Message)
            End Try
        End Sub
        Private Sub mainform_closing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
            If running Then
                StopServer()
                AddHandler ServerStopped, AddressOf SafeFormClose
                e.Cancel = True
            End If
            If e.Cancel = False Then Me.Tray.Visible = False
        End Sub

        Private Sub mainform_Resize(sender As Object, e As EventArgs) Handles MyBase.Resize
            If TrayMinimize And Me.WindowState = FormWindowState.Minimized Then SendToTray()
        End Sub

        Public Sub SafeFormClose()
            If Me.InvokeRequired Then
                Me.Invoke(New ContextCallback(AddressOf SafeFormClose), New Object())
            Else
                Me.Close()
            End If
        End Sub

        Private Sub set_handles()
            If MainWindowHandle <> Me.Handle Then MainWindowHandle = Me.Handle
            AddHandler Me.FormClosed, AddressOf Common.Dispose
            AddHandler Me.FormClosed, AddressOf KillThreads
            AddHandler _tmrUpdateStats.Elapsed, AddressOf hnd_tmr_update_stats
            AddHandler ServerStarted, AddressOf hnd_server_started
            AddHandler ServerStopped, AddressOf hdn_server_stop
            AddHandler ServerStarting, AddressOf hnd_server_starting
        End Sub
#End Region
#Region "Server management"

        Private Sub toggle_server() Handles BtnGeneralStartStop.Click
            If running = False Then start_server() Else stop_server()
        End Sub

        Public Sub start_server() Handles BtnSuperStartLaunch.Click
            RemoveHandler ServerStopped, AddressOf start_server
            UTF8Compatibility = ChkTextUTF8.Checked

            If Me.InvokeRequired Then
                Me.Invoke(New ContextCallback(AddressOf start_server), New Object())
            Else
                Tray.Visible = True
                Try
                    ALVPlayersPlayers.Items.Clear()
                    ALVGeneralPlayers.Items.Clear()
                    ARTXTServerOutput.Text = ""

                    Select Case CBSuperstartServerType.SelectedIndex
                        Case 0 ' Paper Server
                            thds_write_output(New thds_pass_servermessage("[GUI] Starting Paper Server...", clrInfo))
                            Dim ja As New javaStartArgs(CBSuperstartJavaJRE.SelectedIndex, TBSuperstartJavaMinRam.Value, TBSuperstartJavaMaxRam.Value, TxtSuperstartJavaJarFile.Text, TxtSuperstartJavaCustomArgs.Text, TxtSuperstartJavaCustomSwitch.Text, McInteropType.paper)
                            StartServer(ja, McInteropType.paper)

                        Case 1 ' Remote connection
                            thds_write_output(New thds_pass_servermessage("[GUI] Connecting to remote Paper server...", clrInfo))
                            Dim rs As New RemoteJSONAPI
                            StartServer(rs)
                    End Select
                    TabCtrlMain.SelectedTab = TabGeneral
                Catch ex As Exception
                    Log(loggingLevel.Severe, "Mainform", "Severe exception at start_server!", ex.Message)
                End Try
            End If
        End Sub
        Private Sub CBSuperstartServerType_SelectedIndexChanged(sender As Object, e As EventArgs) Handles CBSuperstartServerType.SelectedIndexChanged
            Try
                Select Case CBSuperstartServerType.SelectedIndex
                    Case 0 ' Paper
                        GBSuperstartJavaServer.Enabled = True
                        GBSuperStartMaintainance.Enabled = True
                        GBSuperStartRemoteServer.Enabled = False
                        BtnSuperStartGetCurrent.Enabled = False
                        BtnSuperStartDownloadRecommended.Enabled = True
                        lblSuperStartLatestStable.Text = "Paper MC Engine (Aikar's Flags Enabled)"
                        lblSuperStartLatestBeta.Visible = False
                        lblSuperStartLatestDev.Visible = False
                        llblSuperStartsite.Text = "Site: https://papermc.io"
                    Case 1 ' Remote
                        GBSuperstartJavaServer.Enabled = False
                        GBSuperStartMaintainance.Enabled = False
                        GBSuperStartRemoteServer.Enabled = True
                End Select
                If _initializeCompleted Then write("server", CBSuperstartServerType.SelectedIndex, "superstart")
            Catch ex As Exception
                Log(loggingLevel.Severe, "mainform", "Severe exception in CBSuperstartType_SelectedIndexChanged", ex.Message)
            End Try
        End Sub

        Public Sub stop_server()
            thds_write_output(New thds_pass_servermessage("[GUI] Stopping the server safely...", clrInfo))
            If CurrentServerType = McInteropType.remote Then
                If RemoteServerObject IsNot Nothing Then RemoteServerObject.Close()
            Else
                SendCommand("stop")
            End If
        End Sub

        Public Sub restart_server() Handles BtnGeneralRestart.Click
            stop_server()
            AddHandler ServerStopped, AddressOf start_server
        End Sub

        Public Sub reload_server() Handles BtnGeneralReload.Click
            SendCommand("reload", True)
        End Sub

        Private Sub kill_server() Handles BtnGeneralKill.Click
            If host IsNot Nothing AndAlso host.HasExited = False Then host.Kill()
        End Sub

        Private Sub server_send_command_keyhandler(s As Object, e As KeyEventArgs) Handles TxtGeneralServerIn.KeyUp
            If e.KeyCode = Keys.Enter Then server_send_command_override()
        End Sub

        Private Sub server_send_command_buttonhandler() Handles BtnGeneralSendCmd.Click
            server_send_command_override(Me.TxtGeneralServerIn.Text)
        End Sub

        Private Sub server_send_command_override(Optional ByVal command As String = "")
            If command = "" Then command = Me.TxtGeneralServerIn.Text
            SendCommand(command)
            Me.TxtGeneralServerIn.Text = ""
        End Sub

        Private Sub hnd_server_starting()
            thds_setserverstate("Starting server")
        End Sub
        Private Sub hnd_server_started()
            thds_setserverstate("Server started")
            _threadsWork = True
            
            If CurrentServerType <> McInteropType.remote Then
                _threadReadError = New Thread(AddressOf thd_read_error) : _threadReadError.IsBackground = True : _threadReadError.Start()
                _threadReadOut = New Thread(AddressOf thd_read_output) : _threadReadOut.IsBackground = True : _threadReadOut.Start()
                BtnGeneralStartStop.Text = "Stop"
            Else
                _threadReadOut = New Thread(AddressOf thd_read_remote) : _threadReadOut.IsBackground = True : _threadReadOut.Start()
                BtnGeneralStartStop.Text = "Disconnect"
            End If
        End Sub

        Private Sub hdn_server_stop()
            thds_setserverstate("Server stopped")
            _threadsWork = False
            BtnGeneralStartStop.Text = "Start"
            BtnSuperStartLaunch.Enabled = True
        End Sub

        Private Sub KillThreads()
            _threadsWork = False
        End Sub

        Private Sub thd_read_output()
            Dim sr As New StreamReader(serverOut, Encoding.GetEncoding(ServerEncoding))
            While serverOut IsNot Nothing AndAlso running AndAlso _threadsWork
                Dim rc As String = sr.ReadLine()
                If rc IsNot Nothing AndAlso rc <> "" Then hnd_handle_output(rc)
            End While
        End Sub

        Private Sub thd_read_error()
            Dim sr As New StreamReader(server.serverError, Encoding.GetEncoding(ServerEncoding))
            While server.serverError IsNot Nothing AndAlso running AndAlso _threadsWork
                Dim rc As String = sr.ReadLine()
                If rc IsNot Nothing AndAlso rc <> "" Then hnd_handle_output(rc)
            End While
        End Sub

        Private Sub thd_read_remote()
            While RemoteServerObject IsNot Nothing AndAlso running AndAlso _threadsWork
                Dim rcv As String = RemoteServerObject.StandardOut.read
                If rcv IsNot Nothing Then hnd_handle_output(rcv)
            End While
        End Sub

        Private Sub hnd_handle_output(ByVal text As String)
            Dim Type As MessageType = getMessageType(text)
            Dim clr As Color = getMessageColor(Type)
            thds_write_output(New thds_pass_servermessage(text, clr))
        End Sub

        Private Sub thds_write_output(servermsg As thds_pass_servermessage)
            If Me.InvokeRequired Then
                Me.Invoke(New ContextCallback(AddressOf thds_write_output), New Object() {servermsg})
            Else
                ARTXTServerOutput.AppendText(servermsg.fulltext & vbCrLf, servermsg.color)
            End If
        End Sub

        Private Sub thds_setserverstate(state As String)
            If Me.InvokeRequired Then Me.Invoke(New ContextCallback(AddressOf thds_setserverstate), New Object() {state}) Else lblStatusBarServerState.Text = "Status: " & state
        End Sub

        Private Sub hnd_tmr_update_stats()
            If Me.InvokeRequired Then Me.Invoke(New ContextCallback(AddressOf hnd_tmr_update_stats)) Else LblGeneralTimeSinceStartValue.Text = timeRunning.ToString
        End Sub

        ' Вспомогательные заглушки для элементов WinForms
        Private Sub superstart_init() : End Sub
        Private Function superstart_validate(warn As Boolean) As Boolean : Return True : End Function
        Private Sub info_load() : End Sub
        Private Sub Settings_init() : End Sub
        Private Sub text_options_init() : End Sub
        Private Sub autoserverstart_check() : End Sub
        Public Sub SendToTray() : End Sub
        Private Sub hnd_PlayerJoin(e As Object) : End Sub
        Private Sub hnd_PlayerDisconnect(e As Object) : End Sub
        Private Sub LoadInstalledPlugins(e As Object) : End Sub
        Private Sub thds_SetBukGetPlugins(e As Object) : End Sub
        Private Sub hnd_ip_loaded(e As Object) : End Sub
        Private Sub hnd_last_version_loaded(e As Object) : End Sub
        Private Sub errorLVICreated(e As Object) : End Sub
        Private Sub playerlist_CheckUIList(e As Object) : End Sub
    End Class
End Namespace
