using System.Net;

namespace Pandas.PrintAgent.Core.Backend;

public sealed class BackendRequestException : InvalidOperationException
{
    public BackendRequestException(HttpStatusCode statusCode, string body)
        : base($"Backend respondio {(int)statusCode}: {body}")
    {
        StatusCode = statusCode;
        Body = body;
    }

    public HttpStatusCode StatusCode { get; }
    public string Body { get; }
}
