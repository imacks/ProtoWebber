using System;
using System.Collections.Generic;
using System.Net;

namespace ProtoWebber
{
	public class WebServer
	{
		private string[] _prefix;
		private WebListener _weblistener;
		private IList<IWebServerMiddleware> _middleware;
		private bool _isDisposed = false;

		public WebServer(string[] prefix, IList<IWebServerMiddleware> middleware)
		{
			_prefix = prefix;

			_middleware = middleware;
			foreach (IWebServerMiddleware m in _middleware)
			{
				m.Log = WriteLog;
			}

			_weblistener = new WebListener(prefix);
		}

		public string[] Prefix
		{
			get { return _prefix; }
		}

        public IList<IWebServerMiddleware> Middleware
        {
            get { return _middleware; }
        }

		public void Start()
		{
			if (_isDisposed == true)
				throw new InvalidOperationException("Web server has already been disposed. Create a new instance to start again.");

			_weblistener.ListenerException += ListenerException;
			_weblistener.ListenerStart += ListenerStart;
			_weblistener.WebRequest += WebRequest;
			_weblistener.Start();
		}

		public void Stop()
		{
			WriteLog(string.Format("[VERBOSE] {0}", "Web server is stopping..."));
			_weblistener.Stop();

            WriteLog(string.Format("[VERBOSE] {0}", "Disposing middleware..."));
            foreach (IWebServerMiddleware midware in this.Middleware)
            {
                midware.Dispose();
            }

            WriteLog(string.Format("[VERBOSE] {0}", "Goodbye!"));
            _isDisposed = true;
        }

        protected void WebRequest(object sender, WebRequestEventArgs e)
		{
			if (_isDisposed == true)
				throw new InvalidOperationException("Web server has already been disposed. Create a new instance to start again.");

			HttpListenerContext ctx = e.Context;
			foreach (IWebServerMiddleware m in _middleware)
			{
                if (m.AcceptRequest(ctx))
    				m.ProcessRequest(ctx);

                if (m.StopProcessing)
					break;
			}
		}

		protected virtual void WriteLog(string message)
		{
			Console.WriteLine(message);
		}

		protected virtual void ListenerException(object sender, ListenerExceptionEventArgs e)
		{
			WriteLog(string.Format("[CRITICAL] {0}", e.InnerException));
		}

		protected virtual void ListenerStart(object sender, EventArgs e)
		{
			WriteLog(string.Format("[VERBOSE] {0}", "Hello! ProtoWebber is up and running :)"));
		}
	}
}
