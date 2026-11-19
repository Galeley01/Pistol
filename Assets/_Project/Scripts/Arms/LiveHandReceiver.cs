using System;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
#endif

namespace Pistol.Arms
{
    // Debug input: finger angles from an external hand tracking tool, received over UDP
    // (one JSON packet per tracked frame). Listens only while DebugSettings → Live Fingers is on,
    // and only in the editor and Development builds; a normal build contains no networking here.
    //
    // Added by PlayerArms. A background thread keeps the newest packet; it is parsed on the main thread.
    [DefaultExecutionOrder(-90)]
    public class LiveHandReceiver : MonoBehaviour
    {
        // Packet format (protocol 2 and 3): only the fields used here
        [Serializable]
        public class Packet
        {
            public int v;
            public int frame;
            public Hand[] hands;
        }

        [Serializable]
        public class Hand
        {
            public string side;        // "Left" / "Right": the person's own hand
            public float[] angles;     // 20 angles, degrees: thumb (across, away, mcp, ip), then mcp, pip, dip, spread of index..pinky
            public bool calibrated;
            public float[] viewFingers;  // protocol 3: fingers direction as the person sees their hand (x right, y up, z forward)
            public float[] viewBack;     // protocol 3: back-of-hand direction, same axes
            public float[] offset;       // protocol 3: meters the hand moved since its reference, same axes
            public bool Valid => angles != null && angles.Length == AngleCount;
            public bool HasPlacement => viewFingers != null && viewFingers.Length == 3 && viewBack != null && viewBack.Length == 3
                                        && offset != null && offset.Length == 3;
        }

        public const int AngleCount = 20;

        [Tooltip("Сколько секунд без руки в пакетах считать, что рука пропала")]
        public float lostAfter = 0.3f;

        public bool Listening { get; private set; }
        public int Port { get; private set; }

        Hand right, left;
        float rightSeen = -1f, leftSeen = -1f;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        UdpClient client;
        Thread thread;
        volatile bool running;
        readonly object gate = new object();
        byte[] pending;
#endif

        // Opens or closes the socket; called every frame by the arms with the DebugSettings switch
        public void SetListening(bool on, int port)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (on && (!Listening || port != Port)) Open(port);
            else if (!on && Listening) Close();
#endif
        }

        // The newest message of a hand, or null when it hasn't come for lostAfter seconds
        public Hand GetHand(bool rightSide)
        {
            if (!Listening) return null;
            float seen = rightSide ? rightSeen : leftSeen;
            if (seen < 0f || Time.unscaledTime - seen > lostAfter) return null;
            return rightSide ? right : left;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void Open(int port)
        {
            Close();
            try
            {
                client = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                running = true;
                thread = new Thread(Listen) { IsBackground = true, Name = "Live hand receiver" };
                thread.Start();
                Listening = true;
                Port = port;
                Application.runInBackground = true;   // keep receiving while the tracking window has focus
            }
            catch (Exception e)
            {
                Debug.LogWarning($"LiveHandReceiver: can't listen on port {port}: {e.Message}", this);
                client = null;
            }
        }

        void Close()
        {
            running = false;
            client?.Close();   // unblocks Receive
            thread?.Join(200);
            client = null;
            thread = null;
            Listening = false;
            right = left = null;
            rightSeen = leftSeen = -1f;
        }

        void Listen()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    byte[] data = client.Receive(ref any);
                    lock (gate) pending = data;
                }
                catch (SocketException) { if (!running) break; }
                catch (ObjectDisposedException) { break; }
            }
        }

        void Update()
        {
            if (!Listening) return;
            byte[] data;
            lock (gate) { data = pending; pending = null; }

            if (data == null) return;
            Packet packet;
            try { packet = JsonUtility.FromJson<Packet>(Encoding.UTF8.GetString(data)); }
            catch (Exception) { return; }
            if (packet?.hands == null || packet.v < 2) return;
            float now = Time.unscaledTime;
            foreach (var h in packet.hands)
            {
                if (h == null || !h.Valid) continue;
                if (h.side == "Right") { right = h; rightSeen = now; }
                else if (h.side == "Left") { left = h; leftSeen = now; }
            }
        }

        void OnDisable() => Close();
#endif
    }
}
