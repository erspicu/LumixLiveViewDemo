using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Xml;
using System.Collections.Specialized;
//這是給有興趣對遠端操控Lumix GX7與相關機型的人參考的C#版本專案 , 本專案以GX7為主 ,其他機型可能有部分參數得修改確認
//參考 http://stackoverflow.com/questions/2271183/udp-packet-capturing-in-c-sharp/2387038 一些code取自這邊
// http://www.personal-view.com/talks/discussion/6703/control-your-gh3-from-a-web-browser-now-with-video
// https://github.com/dobrou/iunxtio
// http://baxermux.pixnet.net/blog/post/42370510
namespace LumixLiveViewDemo
{
    public partial class Viewer : Form
    {
        Image image;
        MemoryStream ms;
        Socket UDPSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        int Target_Port = 49152;
        IPEndPoint LocalHostIPEnd;
        int allframe = 0, allbytes = 0;
        public Viewer()
        {
            InitializeComponent();
            try
            {
                //UDP這塊不熟悉,很多參數還無法了解其完整意義,也許有更好更合理的設定監聽方式?
                LocalHostIPEnd = new IPEndPoint(IPAddress.Any, Target_Port);
                UDPSocket.SetSocketOption(SocketOptionLevel.Udp, SocketOptionName.NoDelay, 1);
                UDPSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
                UDPSocket.Bind(LocalHostIPEnd);
                UDPSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 0);
                Console.WriteLine("Starting Recieve");
                IPEndPoint LocalIPEndPoint = new IPEndPoint(IPAddress.Any, Target_Port);
                EndPoint LocalEndPoint = (EndPoint)LocalIPEndPoint;
                StateObject state = new StateObject();
                state.workSocket = UDPSocket;
                Console.WriteLine("Begin Recieve");
                UDPSocket.BeginReceiveFrom(state.buffer, 0, state.BufferSize, 0, ref LocalEndPoint, new AsyncCallback(ReceiveCallback), state);
            }
            catch (Exception ee)
            {
                Console.WriteLine(ee.Message + " " + ee.StackTrace);
            }
        }

        private void Init_Click(object sender, EventArgs e)
        {
            //一些從網路運作觀察到的機器初始化動作 , 在連接上機器後只需要執行一次
            try
            {
                string str1 = GetServerRes("http://192.168.54.1:60606/F02765BACACE/Server0/ddd");
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(str1);
                Console.WriteLine(str1);
                string UDN = xml.ChildNodes[1].ChildNodes[1]["UDN"].InnerText.Split(new char[] { ':' })[1];
                string str2 = GetServerRes("http://192.168.54.1/cam.cgi?mode=accctrl&type=req_acc&value=" + UDN + "&value2=DMC-CM1");
                Console.WriteLine(str2);
                string str3 = GetServerRes("http://192.168.54.1/cam.cgi?mode=camcmd&value=recmode");
                Console.WriteLine(str3);
                string str4 = GetServerRes("http://192.168.54.1/cam.cgi?mode=camcmd&value=capture");
                Console.WriteLine(str4);
                MessageBox.Show("初始化完成");
            }
            catch
            {
                MessageBox.Show("初始化失敗..");
            }

        }

        private void RequestStream_Click(object sender, EventArgs e)
        {
            //若是網路順暢,沒掉封包,一次request後會傳送360張frame,播放率為30fps,共12秒的時間 (數據有所小誤差,但測量起來每次都是接近這樣)
            //如果要連續播放不中斷,就要再最後傳送結束前,再次請求機器提供udp mjepg的串流服務 , 協定規格為單純 mjpeg on udp (非RTP等等進階規格)
            allframe = allbytes = 0;
            string str = GetServerRes("http://192.168.54.1/cam.cgi?mode=startstream&value=49152");
            Console.WriteLine(str);
        }

        public string GetServerRes(string url)
        {
            byte[] response;
            WebClient client = new WebClient();
            client.Proxy = null;
            string str = client.DownloadString(url);
            return str;
        }
        int frame_count = 0;
        private void ReceiveCallback(IAsyncResult ar)
        {
            IPEndPoint LocalIPEndPoint = new
            IPEndPoint(IPAddress.Any, Target_Port);
            EndPoint LocalEndPoint = (EndPoint)LocalIPEndPoint;
            StateObject state = (StateObject)ar.AsyncState;
            Socket client = state.workSocket;
            int bytesRead = client.EndReceiveFrom(ar, ref LocalEndPoint);
            allbytes += bytesRead;
            Console.WriteLine ((allframe++) + " " + bytesRead + " " + allbytes);
            #region image frame parse
            //這裡每一次接收完成動作以一張jpg內容與附帶header為單位,header size不是固定的,存放著一些機器拍攝時的設定參數,但詳細規範不清楚
            //要擷取出每frame正確的jpeg內容,串流內若是出現0xff 0xd8兩個連續bytes,則其位置代表為header結束,jpeg內容開端的辨識bytes,0xff 0xd9則為結束辨識位元
            //我們要做的是剔除把 0xff 0xd8 到 0xff 0xd9 內所有資料擷取出來 (0xff 0xd8 與 0xff d9也是jpeg內容的部分)
            int start = 0;//header size應該不會小於 130byte , 所以從offset 130的位置開始找尋 0xff 0xd8開頭 , header size估計不會超過 180byte,到offset 180結束
            for (int i = 130; i < 180; i++)
                if (state.buffer[i] == 0xff && state.buffer[i + 1] == 0xd8)
                {
                    start = i;
                    break;
                }

            try
            {
                ms = new MemoryStream(state.buffer, start, bytesRead - start);//切割出JPG完整內容
                image = Image.FromStream(ms);
            }
            catch {/*因為一些例外特殊因素,有時候資料會錯誤或是不完整,需要用try catch保護起來*/}
            pictureBox1.Image = image;
            frame_count++;
            #endregion
            client.BeginReceiveFrom(state.buffer, 0, state.BufferSize, 0, ref LocalEndPoint, new AsyncCallback(ReceiveCallback), state);
        }
        public class StateObject
        {
            public int BufferSize = 40000; //要能塞得下完整一張jpg frame , iunxtio專案的預設值30000有overbuffer的可能
            public Socket workSocket;
            public byte[] buffer;
            public StateObject()
            {
                buffer = new byte[BufferSize];
            }
        }
        private void FPS_Tick(object sender, EventArgs e)
        {
            //以一秒為單位,看處理了幾張影格,簡單粗糙但還算可以參考大概播放率的計算概念
            label1.Text = "fps : " + frame_count;
            frame_count = 0;
        }
    }
}
