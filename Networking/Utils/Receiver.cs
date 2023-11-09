﻿using System;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Networking.Events;
using Networking.Models;
using Networking.Queues;
using Networking.Serialization;

namespace Networking.Utils
{
    public class Receiver
    {
        private Queue _recvQueue = new();
        private Thread _recvThread;
        private Thread _recvQueueThread;
        private Dictionary<string, NetworkStream> _clientIDToStream;
        Dictionary<string, string> senderIDToClientID;
        private Dictionary<string, IEventHandler> _moduleEventMap;
        private bool _stopThread = false;

        public Receiver(Dictionary<string, NetworkStream> clientIDToStream, Dictionary<string, IEventHandler> moduleEventMap, Dictionary<string, string> senderIDToClientID)
        {
            Console.WriteLine("[Receiver] Init");
            this.senderIDToClientID = senderIDToClientID;
            _clientIDToStream = clientIDToStream;
            _moduleEventMap = moduleEventMap;
            _recvThread = new Thread(Receive);
            _recvQueueThread = new Thread(RecvLoop);
            _recvThread.Start();
            _recvQueueThread.Start();
            this.senderIDToClientID = senderIDToClientID;
        }

        public void Stop()
        {
            Console.WriteLine("[Receiver] Stop");
            _stopThread = true;
            _recvQueue.Enqueue(new Message(stop: true), 10 /* TODO */);
            _recvThread.Join();
            _recvQueueThread.Join();
        }

        void Receive()
        {
            Console.WriteLine("[Receiver] Receive starts");
            while (!_stopThread)
            {
                bool ifAval = false;
                foreach (var item in _clientIDToStream)
                {
                    if (item.Value.DataAvailable == true)
                    {
                        ifAval = true;
                        // Read the size of the incoming message
                        byte[] sizeBytes = new byte[sizeof(int)];
                        int sizeBytesRead = item.Value.Read(sizeBytes, 0, sizeof(int));
                        System.Diagnostics.Trace.Assert((sizeBytesRead == sizeof(int)));

                        int messageSize = BitConverter.ToInt32(sizeBytes, 0);

                        // Now read the actual message
                        byte[] receiveData = new byte[messageSize];
                        int totalBytesRead = 0;

                        while (totalBytesRead < messageSize)
                        {
                            sizeBytesRead = item.Value.Read(receiveData, totalBytesRead, messageSize - totalBytesRead);
                            if (sizeBytesRead == 0)
                            { 
                                // Handle the case where the stream is closed or no more data is available
                                break;
                            }
                            totalBytesRead += sizeBytesRead;
                        }

                        System.Diagnostics.Trace.Assert((totalBytesRead == messageSize));

                        string receivedMessage = Encoding.ASCII.GetString(receiveData);
                        Message message = Serializer.Deserialize<Message>(receivedMessage);
                        if (message.EventType == EventType.ClientRegister())
                        {
                            message.Data = item.Key;
                        }
                        _recvQueue.Enqueue(message, Priority.GetPriority(message.EventType) /* fix it */);
                    }
                }
                if (ifAval == false)
                    Thread.Sleep(200);
            }
            Console.WriteLine("[Receiver] Receive stops");
        }

        private void handleMessage(Message message)
        {
            foreach (KeyValuePair<string, IEventHandler> pair in _moduleEventMap)
            {
                MethodInfo method = typeof(IEventHandler).GetMethod(message.EventType);
                if (method != null)
                {

                    object[] parameters = new object[] { message };
                    if (message.EventType==EventType.ClientRegister())
                    {
                        parameters = new object[] { message ,_clientIDToStream, senderIDToClientID };
                    }
                    try
                    {
                        method.Invoke(pair.Value, parameters);
                    }
                    catch (Exception) { }
                }
                else
                    Console.WriteLine("Method not found");
            }
        }

        private void RecvLoop()
        {
            while (true)
            {
                if (!_recvQueue.canDequeue())
                {
                    // wait for some time
                    Thread.Sleep(500);
                    continue;
                }

                // Get the next message to send
                Message message = _recvQueue.Dequeue();

                // If the message is a stop message, break out of the loop
                if (message.StopThread)
                    break;

                handleMessage(message);
                
            }
        }
    }
}
