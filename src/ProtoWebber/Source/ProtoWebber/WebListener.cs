using System;
using System.Net;
using System.Threading;

namespace ProtoWebber
{
	public class WebListener
	{
		private readonly HttpListener _listener = new HttpListener();
		private bool _isStarted = false;

		public WebListener(string[] prefix)
		{
			if (!HttpListener.IsSupported)
				throw new NotSupportedException("This program requires Windows XP SP2, Server 2003 or later.");

			// URI prefixes are required, for example 
			// "http://localhost:8080/index/".
			if (prefix == null || prefix.Length == 0)
				throw new ArgumentException("prefix");

			foreach (string s in prefix)
            {
                _listener.Prefixes.Add(s);
            }
		}

		public event EventHandler<ListenerExceptionEventArgs> ListenerException;
		protected virtual void OnListenerException(ListenerExceptionEventArgs e)
		{
			EventHandler<ListenerExceptionEventArgs> handler = ListenerException;
			if (handler != null)
				handler(this, e);
		}

		public event EventHandler ListenerStart;
		protected virtual void OnListenerStart(EventArgs e)
		{
			EventHandler handler = ListenerStart;
			if (handler != null)
				handler(this, e);
		}

		public event EventHandler<WebRequestEventArgs> WebRequest;
		protected virtual void OnWebRequest(WebRequestEventArgs e)
		{
			EventHandler<WebRequestEventArgs> handler = WebRequest;
			if (handler != null)
				handler(this, e);
		}

		public void Start()
		{
			if (_isStarted)
				throw new InvalidOperationException("Listener has already started.");

			_listener.Start();
			_isStarted = true;

			ThreadPool.QueueUserWorkItem((o) =>
			{
				OnListenerStart(EventArgs.Empty);

				try
				{
					while (_listener.IsListening)
					{
						ThreadPool.QueueUserWorkItem((c) =>
						{
							var ctx = c as HttpListenerContext;
							OnWebRequest(new WebRequestEventArgs() {
								Context = ctx
							});
						}, _listener.GetContext());
					}
				}
				catch (Exception ex)
				{
					OnListenerException(new ListenerExceptionEventArgs() {
						InnerException = ex
					});
				}
			});
		}

		public virtual void Stop()
		{
			_listener.Stop();
			_listener.Close();
			_isStarted = false;
		}
	}

	public class ListenerExceptionEventArgs: EventArgs
	{
		public Exception InnerException { get; set; }
	}

	public class WebRequestEventArgs: EventArgs
	{
		public HttpListenerContext Context { get; set; }
	}
}
