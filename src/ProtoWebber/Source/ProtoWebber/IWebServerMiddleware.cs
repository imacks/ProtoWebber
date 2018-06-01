using System;
using System.Net;

namespace ProtoWebber
{
    public interface IWebServerMiddleware : IDisposable
    {
        bool AcceptRequest(HttpListenerContext context);
        void ProcessRequest(HttpListenerContext context);
        bool StopProcessing { get; set; }
        Action<string> Log { get; set; }
    }
}
