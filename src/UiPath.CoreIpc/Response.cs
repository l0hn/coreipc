﻿using System;
using System.Text;
using Newtonsoft.Json;

namespace UiPath.CoreIpc
{
    public class Response
    {
        [JsonConstructor]
        private Response(string requestId, string data, Error error)
        {
            RequestId = requestId;
            Data = data;
            Error = error;
        }
        public string RequestId { get; }
        public string Data { get; }
        public Error Error { get; }
        public static Response Fail(Request request, string message) => Fail(request, new Exception(message));
        public static Response Fail(Request request, Exception ex) => new Response(request.Id, null, new Error(ex));
        public static Response Success(Request request, string data) => new Response(request.Id, data, null);
        public Response CheckError() => Error == null ? this : throw new RemoteException(Error);
    }

    [Serializable]
    public class Error
    {
        [JsonConstructor]
        private Error(string message, string stackTrace, string type, Error innerError)
        {
            Message = message;
            StackTrace = stackTrace;
            Type = type;
            InnerError = innerError;
        }
        public Error(Exception ex) : this(ex.Message, ex.StackTrace ?? ex.GetBaseException().StackTrace, GetExceptionType(ex),
            ex.InnerException == null ? null : new Error(ex.InnerException))
        {
        }
        public string Message { get; }
        public string StackTrace { get; }
        public string Type { get; }
        public Error InnerError { get; }
        public override string ToString() => new RemoteException(this).ToString();
        private static string GetExceptionType(Exception exception) => (exception as RemoteException)?.Type ?? exception.GetType().FullName;
    }

    [Serializable]
    public class RemoteException : Exception
    {
        public RemoteException(Error error) : base(error.Message, error.InnerError == null ? null : new RemoteException(error.InnerError))
        {
            Type = error.Type;
            StackTrace = error.StackTrace;
        }
        public string Type { get; }
        public override string StackTrace { get; }
        public override string ToString()
        {
            var result = new StringBuilder();
            GatherInnerExceptions(result);
            return result.ToString();
        }
        private void GatherInnerExceptions(StringBuilder result)
        {
            result.Append($"{nameof(RemoteException)} wrapping {Type}: {Message} ");
            if (InnerException == null)
            {
                result.Append("\n");
            }
            else
            {
                result.Append(" ---> ");
                ((RemoteException)InnerException).GatherInnerExceptions(result);
                result.Append("\n\t--- End of inner exception stack trace ---\n");
            }
            result.Append(StackTrace);
        }
        public bool Is<TException>() where TException : Exception => Type == typeof(TException).FullName;
    }

    public enum MessageType : byte { Request, Response }
}