using DIOControl.Comm;
using DIOControl.Config;
using log4net;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DIOControl.Controller
{
    class SanwaDigitalController : IDIOController, IConnectionReport
    {
        ILog logger = LogManager.GetLogger(typeof(SanwaDigitalController));
        IDIOReport _Report;
        CtrlConfig _Cfg;
        SocketClient conn;
        bool Waiting = false;
        string ReturnMsg = "";
        public string status = "";

        ConcurrentDictionary<string, bool> DIN = new ConcurrentDictionary<string, bool>();
        ConcurrentDictionary<string, bool> DOUT = new ConcurrentDictionary<string, bool>();
        public SanwaDigitalController(CtrlConfig Config, IDIOReport TriggerReport)
        {
            _Cfg = Config;
            _Report = TriggerReport;

            //Connect();

        }

        public void Close()
        {
            try
            {

            }
            catch
            {

            }
            status = "Disconnect";
            _Report.On_Connection_Status_Report(_Cfg.DeviceName, "Disconnect");
        }

        public void Connect()
        {
            try
            {
                conn = new SocketClient(this);
                conn.remoteHost = _Cfg.IPAdress;
                conn.remotePort = _Cfg.Port;
                conn.Vendor = "SANWA";
                
                ThreadPool.QueueUserWorkItem(new WaitCallback(conn.Start));

            }
            catch (Exception e)
            {
                logger.Error(e.StackTrace);
            }
        }

        private void Polling()
        {

            while (true)
            {
                try
                {
                    if (_Cfg.Digital)
                    {
                        bool[] Response = new bool[0];
                        string[] valAry;
                        lock (conn)
                        {
                            Waiting = true;
                            conn.Send("$1GET:BRDIO:17,1,3\r");
                            SpinWait.SpinUntil(() => !Waiting, 50000);
                            if (Waiting && !status.Equals("Timeout"))
                            {
                                logger.Error("DIO polling error: Timeout");
                                _Report.On_Connection_Status_Report(_Cfg.DeviceName, "Timeout");
                                status = "Timeout";
                                continue;
                            }
                            
                            string orgAry = ReturnMsg.Split(':')[2];
                            
                            valAry = orgAry.Split(',');
                            
                        }
                        int Count = Convert.ToInt32(valAry[2]);
                        int idx = 3;
                        int StartNo = Convert.ToInt32(valAry[0]);
                        for (int x = 0; x <  Count; x++)
                        {

                            string hexStr = valAry[idx];
                            string binary = String.Join(String.Empty, hexStr.Select(c => Convert.ToString(Convert.ToInt32(c.ToString(), 16), 2).PadLeft(4, '0')));

                            Response = new bool[binary.Length];
                            int orgIdx = binary.Length - 1;
                            for (int i = 0; i < binary.Length; i++)
                            {
                                Response[orgIdx] = binary[i] == '1' ? true : false;
                                orgIdx--;
                            }
                            for (int i = 0; i < _Cfg.DigitalInputQuantity; i++)
                            {
                                string addr = StartNo.ToString("00")+"," + (i+1).ToString();
                                if (DIN.ContainsKey(addr))
                                {
                                    bool org;
                                    DIN.TryGetValue(addr, out org);
                                    if (!org.Equals(Response[i]))
                                    {
                                        DIN.TryUpdate(addr, Response[i], org);
                                        _Report.On_Data_Chnaged(_Cfg.DeviceName, "DIN", addr, org.ToString(), Response[i].ToString());
                                    }
                                }
                                else
                                {
                                    DIN.TryAdd(addr, Response[i]);
                                    _Report.On_Data_Chnaged(_Cfg.DeviceName, "DIN", addr, "False", Response[i].ToString());
                                }
                            }
                            idx++;
                            StartNo++;
                        }
                    }

                    SpinWait.SpinUntil(() => false, _Cfg.Delay);
                }
                catch (Exception e)
                {
                    logger.Error(e.StackTrace);
                }
            }
        }

        public void SetOut(string Address, string Value)
        {
            try
            {
                string hwid = Address.Split(',')[0];
                string addr = Address.Split(',')[1];
                int adr = Convert.ToUInt16(addr);
                bool boolVal = false;
                if (bool.TryParse(Value, out boolVal))
                {
                    lock (conn)
                    {
                        Waiting = true;
                        conn.Send("$1SET:RELIO:" + hwid + "0" + adr.ToString() + "," + (boolVal ? "1" : "0") + "\r");
                        SpinWait.SpinUntil(() => !Waiting, 5000);
                        if (Waiting && !status.Equals("Timeout"))
                        {
                            logger.Error("DIO SetOut error: Timeout");
                            _Report.On_Connection_Status_Report(_Cfg.DeviceName, "Timeout");
                            status = "Timeout";
                            return;
                        }

                    }


                    bool org;
                    if (DOUT.TryGetValue(Address, out org))
                    {
                        if (!org.Equals(boolVal))
                        {
                            DOUT.TryUpdate(Address, boolVal, org);
                            _Report.On_Data_Chnaged(_Cfg.DeviceName, "DOUT", Address, org.ToString(), boolVal.ToString());
                        }
                    }
                    else
                    {
                        DOUT.TryAdd(Address, boolVal);
                        _Report.On_Data_Chnaged(_Cfg.DeviceName, "DOUT", Address, "N/A", boolVal.ToString());
                    }
                }

            }
            catch (Exception e)
            {
                logger.Error(e.StackTrace);
            }
        }

        public void SetOutWithoutUpdate(string Address, string Value)
        {
            try
            {
                string hwid = Address.Split(',')[0];
                string addr = Address.Split(',')[1];
                ushort adr = Convert.ToUInt16(addr);
                bool boolVal = false;
                if (bool.TryParse(Value, out boolVal))
                {
                    bool org;
                    if (DOUT.TryGetValue(Address, out org))
                    {
                        if (org != bool.Parse(Value))
                        {
                            DOUT.TryUpdate(Address, bool.Parse(Value), org);
                            _Report.On_Data_Chnaged(_Cfg.DeviceName, "DOUT", Address, org.ToString(), bool.Parse(Value).ToString());
                        }
                    }
                    else
                    {
                        DOUT.TryAdd(Address, bool.Parse(Value));
                        _Report.On_Data_Chnaged(_Cfg.DeviceName, "DOUT", Address, "N/A", bool.Parse(Value).ToString());
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error(e.StackTrace);
            }
        }

        private string BinaryStringToHexString(string binary)
        {
            StringBuilder result = new StringBuilder(binary.Length / 8 + 1);

            // TODO: check all 1's or 0's... Will throw otherwise

            int mod4Len = binary.Length % 8;
            if (mod4Len != 0)
            {
                // pad to length multiple of 8
                binary = binary.PadLeft(((binary.Length / 8) + 1) * 8, '0');
            }

            for (int i = 0; i < binary.Length; i += 8)
            {
                string eightBits = binary.Substring(i, 8);
                result.AppendFormat("{0:X2}", Convert.ToByte(eightBits, 2));
            }

            return result.ToString();
        }

        public void UpdateOut()
        {
            try
            {
                Dictionary<string, bool[]> dioList = new Dictionary<string, bool[]>();
                bool[] data;

                foreach (KeyValuePair<string, bool> kvp in DOUT)
                {
                    string hwid = kvp.Key.Split(',')[0];
                    string addr = kvp.Key.Split(',')[1];
                    if (dioList.TryGetValue(hwid, out data))
                    {
                        data[Convert.ToInt32(addr)-1] = kvp.Value;
                    }
                    else
                    {
                        data = new bool[_Cfg.DigitalInputQuantity];
                        for (int i = 0; i < data.Length; i++)
                        {//initial
                            data[i] = false;
                        }
                        data[Convert.ToInt32(addr)-1] = kvp.Value;
                        dioList.Add(hwid, data);
                    }

                }

                lock (conn)
                {
                    
                    foreach (KeyValuePair<string, bool[]> kvp in dioList)
                    {
                        string result = "";
                        foreach (bool each in kvp.Value)
                        {
                            result = (each ? "1" : "0") + result;//反向
                        }

                        Waiting = true;
                        conn.Send("$1SET:BRDIO:" + kvp.Key + "," + BinaryStringToHexString(result) + "\r");
                        SpinWait.SpinUntil(() => !Waiting, 5000);
                        if (Waiting && !status.Equals("Timeout"))
                        {
                            logger.Error("DIO SetOut error: Timeout");
                            _Report.On_Connection_Status_Report(_Cfg.DeviceName, "Timeout");
                            status = "Timeout";
                            return;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error(e.StackTrace);
            }
        }

        public string GetIn(string Address)
        {
            bool result = false;
            try
            {
                //int key = Convert.ToInt32(Address);
                if (DIN.ContainsKey(Address))
                {
                    if (!DIN.TryGetValue(Address, out result))
                    {
                        throw new Exception("DeviceName:" + _Cfg.DeviceName + " Address " + Address + " get fail!");
                    }
                }
                else
                {
                    throw new Exception("DeviceName:" + _Cfg.DeviceName + " Address " + Address + " not exist!");
                }
            }
            catch (Exception e)
            {
                logger.Error(e.StackTrace);
            }
            return result.ToString();
        }

        public string GetOut(string Address)
        {
            string hwid = Address.Split(',')[0];
            string addr = Address.Split(',')[1];
            bool result = false;
            lock (conn)
            {
                Waiting = true;
                conn.Send("$1GET:RELIO:"+ hwid + "0" + (Convert.ToInt16(addr)).ToString() + "\r");
                SpinWait.SpinUntil(() => !Waiting, 5000);
                if (Waiting && !status.Equals("Timeout"))
                {
                    logger.Error("DIO GetOut error: Timeout");
                    _Report.On_Connection_Status_Report(_Cfg.DeviceName, "Timeout");
                    status = "Timeout";
                    throw new Exception("DIO GetOut error: Timeout");
                }
                string boolStr = ReturnMsg.Split(':')[2];
                boolStr = boolStr.Split(',')[1];
                result = boolStr.Equals("1") ? true : false;
            }
            return result.ToString();
        }

        public void On_Connection_Message(object Msg)
        {
            ReturnMsg = Msg.ToString().Replace("\r", "");
            Waiting = false;

        }

        public void On_Connection_Connecting(string Msg)
        {
            _Report.On_Connection_Status_Report(_Cfg.DeviceName, "Connecting");
            status = "Connecting";
        }

        public void On_Connection_Connected(object Msg)
        {
            _Report.On_Connection_Status_Report(_Cfg.DeviceName, "Connected");
            status = "Connected";
            Thread ReceiveTd = new Thread(Polling);
            ReceiveTd.IsBackground = true;
            ReceiveTd.Start();
        }

        public void On_Connection_Disconnected(string Msg)
        {
            _Report.On_Connection_Status_Report(_Cfg.DeviceName, "Disconnected");
            status = "Disconnected";
        }

        public void On_Connection_Error(string Msg)
        {
            _Report.On_Connection_Status_Report(_Cfg.DeviceName, "Error");
            status = "Error";
        }
    }
}
