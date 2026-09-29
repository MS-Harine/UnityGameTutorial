using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Blocks.Network
{
    public enum NetworkState
    {
        Disconnected,
        Connecting,
        Connected
    }

    public class NetworkManager
    {
        public static NetworkManager Instance { get; } = new NetworkManager();

        public event Action<NetworkState, string> OnNetworkStateChanged;
        public event Action<byte[]> OnDataReceived;

        private TcpClient m_Client = new();
        private IPAddress m_IpAddress;
        private int m_Port;
        private readonly ConcurrentQueue<byte[]> m_ReceiveQueue = new();
        private bool reset_connection = false;
        private CancellationTokenSource m_ReceiveCancellationTokenSource;
    
        public string IpAddress => m_IpAddress?.ToString() ?? string.Empty;
        public int Port => m_Port;

        public async Task Connect(string ipAddress, int port)
        {
            if (IsConnected())
            {
                Disconnect();
            }

            ChangeState(NetworkState.Connecting);

            m_IpAddress = IPAddress.Parse(ipAddress);
            m_Port = port;

            if (m_Client == null)
            {
                m_Client = new TcpClient();
            }

            try
            {
                await m_Client.ConnectAsync(m_IpAddress, m_Port);
                ChangeState(NetworkState.Connected);
                _ = ReceiveData();
            }
            catch (Exception ex)
            {
                ChangeState(NetworkState.Disconnected, ex.Message.Trim());
                Disconnect();
            }
        }

        public void Disconnect()
        {
            if (IsConnected())
            {
                reset_connection = true;
                m_ReceiveCancellationTokenSource?.Cancel();
                ChangeState(NetworkState.Disconnected);
                m_ReceiveQueue.Clear();
            }
            
            m_Client?.Close();
            m_Client = null;
        }

        public bool IsConnected()
        {
            return m_Client != null && m_Client.Connected;
        }

        public async Task SendData(byte[] data)
        {
            if (!IsConnected())
            {
                return;
            }

            NetworkStream stream = m_Client.GetStream();
            try
            {
                await stream.WriteAsync(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                ChangeState(NetworkState.Disconnected, ex.Message.Trim());
                Disconnect();
            }
        }

        public byte[] GetData()
        {
            if (m_ReceiveQueue.Count == 0)
            {
                return Array.Empty<byte>();
            }

            var allData = new List<byte[]>();
            while (m_ReceiveQueue.TryDequeue(out var data))
            {
                allData.Add(data);
            }

            return allData.SelectMany(x => x).ToArray();
        }

        private async Task ReceiveData()
        {
            if (!IsConnected())
            {
                return;
            }

            NetworkStream stream = m_Client.GetStream();
            byte[] buffer = new byte[1024];
            reset_connection = false;
            m_ReceiveCancellationTokenSource = new CancellationTokenSource();

            try
            {
                while (!reset_connection)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, m_ReceiveCancellationTokenSource.Token);
                    if (bytesRead > 0)
                    {
                        byte[] receivedData = new byte[bytesRead];
                        Array.Copy(buffer, receivedData, bytesRead);
                        m_ReceiveQueue.Enqueue(receivedData);
                        OnDataReceived?.Invoke(receivedData);
                    }
                }
            }
            catch (Exception ex)
            {
                ChangeState(NetworkState.Disconnected, ex.Message.Trim());
                Disconnect();
            }

            m_ReceiveCancellationTokenSource.Dispose();
        }

        private void ChangeState(NetworkState newState, string message = "")
        {
            OnNetworkStateChanged?.Invoke(newState, message);
        }
    }
}