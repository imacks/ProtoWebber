using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProtoWebber
{
    public class WebSocketConnectionManager
    {
        private ConcurrentDictionary<string, WebSocket> _sockets = new ConcurrentDictionary<string, WebSocket>();

        public WebSocket GetSocketById(string id)
        {
            return _sockets.FirstOrDefault(p => p.Key == id).Value;
        }

        public ConcurrentDictionary<string, WebSocket> GetAll()
        {
            return _sockets;
        }

        public string GetId(WebSocket socket)
        {
            return _sockets.FirstOrDefault(p => p.Value == socket).Key;
        }

        public string AddSocket(WebSocket socket)
        {
            string socketId = CreateConnectionId();
            _sockets.TryAdd(socketId, socket);
            return socketId;
        }

        public async Task RemoveSocket(string id)
        {
            WebSocket socket;
            _sockets.TryRemove(id, out socket);

            await socket.CloseAsync(
                closeStatus: WebSocketCloseStatus.NormalClosure,
                statusDescription: "Closed by the WebSocketConnectionManager",
                cancellationToken: CancellationToken.None);
        }

        public async Task SendMessageAsync(WebSocket socket, string message)
        {
            if (socket.State != WebSocketState.Open)
                return;

            await socket.SendAsync(
                buffer: new ArraySegment<byte>(Encoding.UTF8.GetBytes(message), 0, message.Length),
                messageType: WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken: CancellationToken.None);
        }

        public async Task SendMessageAsync(string socketId, string message)
        {
            await SendMessageAsync(GetSocketById(socketId), message);
        }

        public async Task SendMessageToAllAsync(string message)
        {
            foreach (var pair in GetAll())
            {
                if (pair.Value.State == WebSocketState.Open)
                    await SendMessageAsync(pair.Value, message);
            }
        }

        private string CreateConnectionId()
        {
            return Guid.NewGuid().ToString();
        }
    }
}
