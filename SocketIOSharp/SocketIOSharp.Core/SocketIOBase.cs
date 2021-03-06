﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;

namespace SocketIOSharp.Core
{
    public abstract class SocketIOBase : ISocketIO
    {
        public enum EngineIOPacketOp
        {
            OPEN,
            CLOSE,
            PING,
            PONG,
            MESSAGE,
            UPGRADE,
            NOOP
        };

        public enum SocketIOPacketOp
        {
            CONNECT,
            DISCONNECT,
            EVENT,
            ACK,
            ERROR,
            BINARY_EVENT,
            BINARY_ACK
        }

        protected string IOConnectOpcode
        {
            get
            {
                return EngineIOPacketOp.MESSAGE.ToString("d") +
                       SocketIOPacketOp.CONNECT.ToString("d");
            }
        }

        protected string IOEventOpcode
        {
            get
            {
                return EngineIOPacketOp.MESSAGE.ToString("d") +
                       SocketIOPacketOp.EVENT.ToString("d");
            }
        }

        protected string Namespace { get; set; }
        protected bool   isConnected { get; set; }

        protected IWebSocket Socket { get; set; }
        protected Dictionary<string, List<Action<IEnumerable<object>>>> EventListenersDict { get; set; }

        public string SocketID { get; set; }
        public UInt64 PingInterval { get; set; }
        public UInt64 PingTimeout { get; set; }

        public SocketIOBase(SocketIOConfigurator config)
        {
            if (config == null)
                config = new SocketIOConfigurator();

            Namespace = config.Namespace;
            EventListenersDict = new Dictionary<string, List<Action<IEnumerable<object>>>>();
        }

        public void Dispose()
        {
            Socket.Dispose();
        }

        public async Task<WebSocketState> ConnectAsync(Uri uri)
        {
            WebSocketState state = GetState();

            if (state != WebSocketState.Open && state != WebSocketState.Connecting)
            {
                await Socket.ConnectAsync(uri, ReceiveMessage);
            }

            return GetState();
        }

        public void On(string type, Action<IEnumerable<object>> callback)
        {
            if (callback == null)
                return;

            List<Action<IEnumerable<object>>> eventListeners = null;

            if (EventListenersDict.TryGetValue(type, out eventListeners))
            {
                eventListeners.Add(callback);
            }
            else
            {
                EventListenersDict.Add(type, new List<Action<IEnumerable<object>>>() { callback });
            }
        }

        public void Off(string type)
        {
            if(EventListenersDict.ContainsKey(type))
                EventListenersDict[type].Clear();
        }

        public void Off(string type, int index)
        {
            if (EventListenersDict.ContainsKey(type))
                EventListenersDict[type].RemoveAt(index);
        }

        public void Off(Action<IEnumerable<object>> callback)
        {
            foreach(var el in EventListenersDict.Values)
            {
                el.Remove(callback);
            }
        }

        public void Off(string type, Action<IEnumerable<object>> callback)
        {
            if (EventListenersDict.ContainsKey(type))
                EventListenersDict[type].Remove(callback);
        }

        public Task EmitAsync(string type, object data)
        {
            return Socket.SendAsync(string.Format("{0}/{1},[\"{2}\",{3}]", IOEventOpcode, Namespace, type, SerializeEmitObject(data)));
        }

        public Task DisconnectAsync()
        {
            isConnected = false;
            return Socket.CloseAsync();
        }

        public bool IsConnected()
        {
            return GetState() == WebSocketState.Open && isConnected;
        }

        public WebSocketState GetState()
        {
            return Socket != null ? Socket.GetState() : WebSocketState.Closed;
        }

        private void ReceiveMessage(byte[] result)
        {
            if (result == null)
                return;

            using (MemoryStream ms = new MemoryStream())
            {
                string jsonStr = null;

                ms.Write(result, 0, result.Length);
                ms.Seek(0, SeekOrigin.Begin);

                var iop = ms.ReadByte() - '0';
                if(iop == (byte)EngineIOPacketOp.OPEN)
                {
                    using (var sr = new StreamReader(ms))
                    {
                        jsonStr = sr.ReadToEnd();
                    }
                    ParseEngineIOInitValues(jsonStr);
                }
                else if (iop == (byte)EngineIOPacketOp.MESSAGE)
                {
                    var siop = ms.ReadByte() - '0';
                    if(siop == (byte)SocketIOPacketOp.EVENT)
                    {
                        //skip "," from packet
                        ms.Seek(ms.Position + Namespace.Length + 2, SeekOrigin.Begin);
                        using (var sr = new StreamReader(ms))
                        {
                            jsonStr = sr.ReadToEnd();
                        }
                        EmitToEventListeners(jsonStr);
                    }
                    else if(!isConnected && siop == (byte)SocketIOPacketOp.CONNECT)
                    {
                        //connect to socket.io
                        Socket.SendAsync(string.Format("{0}/{1}", IOConnectOpcode, Namespace));
                        isConnected = true;
                    }
                }
                else if(iop == (byte)EngineIOPacketOp.PONG)
                {
                    List<Action<IEnumerable<object>>> eventListeners = null;
                    if (EventListenersDict.TryGetValue("pong", out eventListeners))
                    {
                        foreach (var listener in eventListeners)
                        {
                            listener(new List<object>());
                        }
                    }
                }
            }
        }

        protected abstract void ParseEngineIOInitValues(string jsonStr);
        protected abstract void EmitToEventListeners(string jsonStr);
        protected abstract string SerializeEmitObject(object data);
    }
}
