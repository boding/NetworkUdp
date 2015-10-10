using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public interface IMsgPacker
{
    public int pack(ref byte[] buffer , ref usedSize);

    public int unpack(ref byte[] buffer , ref usedSize);
}

public class CSMsgC : IMsgPacker
{

}

public class CSMsgS : IMsgPacker
{
}

public class NetworkUdp : MonoBehaviour
{
    [HideInInspector] public string ip;
    [HideInInspector] public int port;

    [HideInInspector] public bool isAlive;
    [HideInInspector] public bool isClose;
    
    private CircularQueue<CSMsgC> sendQueue;
    private CircularQueue<CSMsgS> recvQueue;
    
    private UdpClient udpClient;
    
    private Thread sendThread;
    private Thread recvThread;
    
    private float recvTime;
    
    private bool terminate;
    private bool reconnect;

    private int retryCount;

    void Awake()
    {
        ip = "127.0.0.1";
        port = 6630;

        sendQueue = new CircularQueue<CSMsgC>(1024);
        recvQueue = new CircularQueue<CSMsgS>(1024);

        Connect();

        isAlive = true;
    }

    void Update()
    {
        if (reconnect || terminate || isClose)
        {
            return;
        }
        
        if (Time.time - recvTime >= 3f)
        {
            recvTime = Time.time;
            Reconnect();
        }
    }

    void OnDestroy()
    {
        Close();
    }
    
    void OnApplicationQuit()
    {
        Close();
    }

    void SendThread()
    {
        byte[] buffer = new byte[4096];
        int usedSize = 0;
        
        while (!terminate)
        {
            try
            {
                CSMsgC msg = sendQueue.Dequeue();

                if (null != msg)
                {
                    TdrError.ErrorType error = msg.pack(ref buffer, ref usedSize);
                    if (TdrError.ErrorType.TDR_NO_ERROR == error)
                    {
                        int ret = udpClient.Send(buffer, usedSize);
                    }
                    else
                    {
                        Debug.LogError("[NetworkUdp.SendThread] pack message failed.");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("[NetworkUdp.SendThread] send exception: " + e.Message);
            }
            
            Thread.Sleep(1);
        }
    }
    
    void RecvThread()
    {
        IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, port);
        
        while (!terminate)
        {
            try
            {
                byte[] buffer = null;
                if (udpClient.Available > 0)
                {
                    buffer = udpClient.Receive(ref anyIP);
                }
                
                int usedSize = 0;
                if (null != buffer)
                {
                    CSMsgS msg = new CSMsgS();
                    TdrError.ErrorType error = msg.unpack(ref buffer, ref usedSize);
                    if (TdrError.ErrorType.TDR_NO_ERROR == error)
                    {
                        if (!recvQueue.Enqueue(msg))
                        {
                            recvQueue.Clear();
                            recvQueue.Enqueue(msg);
                        }
                    }
                    else
                    {
                        Debug.LogError("[NetworkUdp.RecvThread] unpack message failed.");
                    }
                }
            }
            catch(System.Exception e)
            {
                Debug.LogError("[NetworkUdp.RecvThread] recv exception: " + e.Message);
            }
            
            Thread.Sleep(1);
        }
    }
    
    int Connect()
    {
        try
        {
            udpClient = new UdpClient();

            udpClient.Connect(new IPEndPoint(IPAddress.Parse(ip), port));

            sendThread = new Thread(new ThreadStart(SendThread));
            sendThread.IsBackground = true;
            sendThread.Start();
            
            recvThread = new Thread(new ThreadStart(RecvThread));
            recvThread.IsBackground = true;
            recvThread.Start();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[NetworkUdp.Connect] connect exception: " + e.Message);
        }

        recvTime = Time.time;
        
        return 0;
    }

    public int Reconnect()
    {
        if (++retryCount <= 3)
        {
            StopCoroutine("ReconnectCoroutine");
            StartCoroutine(ReconnectCoroutine());
            Debug.LogError("[NetworkUdp.Reconnect] connect retry count: " + retryCount);
        }
        else
        {
            Close();
            Debug.LogError("[NetworkUdp.Reconnect] connect closed.");
        }

        return 0;
    }
    
    IEnumerator ReconnectCoroutine()
    {
        isAlive = false;
        isClose = false;

        terminate = true;
        reconnect = true;
        
        udpClient.Close();
        
        while(sendThread.IsAlive || recvThread.IsAlive)
        {
            yield return null;
        }
        
        terminate = false;

        Connect();

        reconnect = false;
    }
    
    int Close()
    {
        isAlive = false;
        isClose = true;
        
        reconnect = false;
        terminate = true;

        retryCount = 0;
        
        sendThread.Abort();
        recvThread.Abort();
        
        udpClient.Close();
        
        return 0;
    }
    
    public int Send(CSMsgC msg)
    {
        if (!sendQueue.Enqueue(msg))
        {
            sendQueue.Clear();
            sendQueue.Enqueue(msg);
        }
        
        return 0;
    }
    
    public CSMsgS Recv()
    {
        CSMsgS msg = recvQueue.Dequeue();
        if (null != msg)
        {
            recvTime = Time.time;
            retryCount = 0;
            isAlive = true;
        }
        
        return msg;
    }
}
