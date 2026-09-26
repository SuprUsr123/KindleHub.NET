Imports System.Collections.Generic

Public Class KindleHubException
        Inherits Exception

        Public Sub New(message As String)
            MyBase.New(message)
        End Sub

        Public Sub New(message As String, innerException As Exception)
            MyBase.New(message, innerException)
        End Sub
    End Class

    Public Class AuthenticationException
        Inherits KindleHubException

        Public Sub New(message As String)
            MyBase.New(message)
        End Sub

        Public Sub New(message As String, innerException As Exception)
            MyBase.New(message, innerException)
        End Sub
    End Class

    Public Class NetworkException
        Inherits KindleHubException

        Public Sub New(message As String)
            MyBase.New(message)
        End Sub

        Public Sub New(message As String, innerException As Exception)
            MyBase.New(message, innerException)
        End Sub
    End Class

    Public Class RateLimitException
        Inherits KindleHubException

        Public Property RetryAfter As TimeSpan

        Public Sub New(message As String, retryAfter As TimeSpan)
            MyBase.New(message)
            Me.RetryAfter = retryAfter
        End Sub
    End Class

    Public Class ValidationException
        Inherits KindleHubException

        Public Property FieldErrors As Dictionary(Of String, String)

        Public Sub New(message As String)
            MyBase.New(message)
            Me.FieldErrors = New Dictionary(Of String, String)()
        End Sub
    End Class

    Public Class NotFoundException
        Inherits KindleHubException

        Public Sub New(message As String)
            MyBase.New(message)
        End Sub
    End Class

    Public Class ServerException
        Inherits KindleHubException

        Public Property StatusCode As Integer

        Public Sub New(message As String, statusCode As Integer)
            MyBase.New(message)
            Me.StatusCode = statusCode
        End Sub
    End Class