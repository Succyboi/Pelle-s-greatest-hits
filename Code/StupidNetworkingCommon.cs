using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Telepathy;

namespace Stupid.Netcode
{
    [System.Serializable]
    public class Message
    {
        public Message(MessageType messageType, object contents, UserInfo userInfo)
        {
            this.messageType = messageType;
            this.contents = contents;
            this.userInfo = userInfo;
        }

        public MessageType messageType;
        public UserInfo userInfo;
        public int fromID;
        public object contents;
    }

    [System.Serializable]
    public class Node
    {
        public Node(int port)
        {
            server = new Server();

            this.ip = NetworkTools.GetLocalIPAddress();
            this.port = port;
        }

        public string ip;
        public int port;

        public Server server;

        public void Start()
        {
            if(server == null)
            {
                server = new Server();
            }

            ip = NetworkTools.GetLocalIPAddress();
            server.Start(port);
        }

        public void Stop()
        {
            server.Stop();
        }

        public bool GetNextMessage(out Message message)
        {
            message = null;

            Telepathy.Message msg;
            bool msgAvailable = server.GetNextMessage(out msg);

            if (msgAvailable)
            {
                switch (msg.eventType)
                {
                    case Telepathy.EventType.Connected:
                        msgAvailable = false;
                        break;

                    case Telepathy.EventType.Data:
                        message = NetworkTools.DataToMessage(msg.data);
                        message.fromID = msg.connectionId;
                        break;

                    case Telepathy.EventType.Disconnected:
                        msgAvailable = false;
                        break;
                }
            }

            return msgAvailable;
        }

        private void Send(int id, Message msg)
        {
            server.Send(id, NetworkTools.MessageToData(msg));
        }
    }

    [System.Serializable]
    public class Connection
    {
        public Connection(string ip, int port)
        {
            this.ip = ip;
            this.port = port;

            client = new Client();
        }

        public string ip;
        public int port;
        public UserInfo userInfo;
        public bool connected = false;

        private Client client;

        public void Connect()
        {
            if(client == null)
            {
                client = new Client();
            }

            client.Connect(this.ip, this.port);
        }

        public bool CheckIfDisconnected()
        {
            if (!client.Connected && !client.Connecting)
            {
                return true;
            }

            Telepathy.Message msg;
            if(client.GetNextMessage(out msg))
            {
                if (msg.eventType == Telepathy.EventType.Disconnected)
                {
                    return true;
                }
            }

            return false;
        }

        public void SealConnection(UserInfo nodeInfo)
        {
            if (client.Connected)
            {
                Send(new Message(MessageType.UserConnectionRequest, null, nodeInfo));
                connected = true;
            }
        }

        public void Send(Message msg)
        {
            client.Send(NetworkTools.MessageToData(msg));
        }
    }

    public enum MessageType
    {
        PeerDiscoveryRequest,

        UserConnectionRequest,

        UserConnected,
        UserDisconnected,

        UserInfoRequest,
        userInfo,

        NetworkInfoRequest,
        NetworkInfo,

        ServerMessage,
        UserMessage
    }

    [System.Serializable]
    public class NetworkInfo
    {
        public NetworkInfo(UserInfo[] users)
        {
            this.users = users;
        }

        public UserInfo[] users;
    }

    public enum DefaultPorts
    {
        A = 1337,
        B = 1338,
        C = 1339,
        D = 1340
    }

    public static class NetworkTools
    {
        public static byte[] handShakeData = new byte[] { 68, 69 };
        
        public static int GetAvailablePort()
        {
            System.Net.Sockets.TcpListener listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public static string GetLocalIPAddress()
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());

            string ip = "";

            foreach (var adresss in host.AddressList)
            {
                if (adresss.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    ip = adresss.ToString();
                }
            }

            if(ip != "")
            {
                return ip;
            }
            else
            {
                throw new System.Exception("No network adapters with an IPv4 address in the system!");
            }
        }

        public static byte[] MessageToData(Message msg)
        {
            System.Runtime.Serialization.Formatters.Binary.BinaryFormatter bf = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
            using (var ms = new System.IO.MemoryStream())
            {
                bf.Serialize(ms, (object)msg);
                return ms.ToArray();
            }
        }

        public static Message DataToMessage(byte[] data)
        {
            using (var memStream = new System.IO.MemoryStream())
            {
                var binForm = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                memStream.Write(data, 0, data.Length);
                memStream.Seek(0, System.IO.SeekOrigin.Begin);
                var obj = binForm.Deserialize(memStream);

                try
                {
                    return (Message)obj;
                }
                catch
                {
                    Debug.LogWarning("Could not convert data to message.");
                    return null;
                }
            }
        }
    }
}