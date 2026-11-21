Imports System.Collections.ObjectModel

''' <summary>
''' Shared, in-memory room state so Topics (Community), Messages and Games can hand a
''' room to the chat surface without a full navigation/messaging service.
''' </summary>
Public Class RoomRegistry
    Public Shared ReadOnly Rooms As New ObservableCollection(Of Group)()
    Private Shared _active As Group
    Public Shared Event ActiveRoomChanged As EventHandler

    Public Shared Property ActiveRoom As Group
        Get
            Return _active
        End Get
        Set(value As Group)
            If _active IsNot Nothing AndAlso value IsNot Nothing AndAlso _active.Code = value.Code Then Return
            _active = value
            RaiseEvent ActiveRoomChanged(Nothing, EventArgs.Empty)
        End Set
    End Property

    Public Shared Sub AddRoom(g As Group)
        If g Is Nothing OrElse String.IsNullOrEmpty(g.Code) Then Return
        For Each r In Rooms
            If String.Equals(r?.Code, g.Code, StringComparison.Ordinal) Then Return
        Next
        Rooms.Add(g)
    End Sub

    Public Shared Sub Open(g As Group)
        AddRoom(g)
        ActiveRoom = g
    End Sub

    Public Shared Sub Remove(code As String)
        If String.IsNullOrWhiteSpace(code) Then Return
        For i = Rooms.Count - 1 To 0 Step -1
            If String.Equals(Rooms(i)?.Code, code, StringComparison.Ordinal) Then Rooms.RemoveAt(i)
        Next
        If String.Equals(_active?.Code, code, StringComparison.Ordinal) Then ActiveRoom = Nothing
    End Sub
End Class
