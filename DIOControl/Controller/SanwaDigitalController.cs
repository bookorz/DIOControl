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
        ILog logger = LogManager.GetLogger(typeof(ICPconDigitalController));
        IDIOReport _Report;
        CtrlConfig _Cfg;
        SocketClient conn;
        bool Waiting = false;
        string ReturnMsg = "";

        ConcurrentDictionary<int, bool> DIN = new ConcurrentDictionary<int, bool>();
        ConcurrentDictionary<int, bool> DOUT = new ConcurrentDictionary<int, bool>();
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
                conn.Start();
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

                        lock (conn)
                        {
                            conn.Send("$1GET:BRDIO:1,1,1\r");
                            SpinWait.SpinUntil(() => !Waiting, 5000);
                            if (Waiting)
                            {
                                logger.Error("DIO polling error: Timeout");
                                continue;
                            }
                            string hexStr = ReturnMsg.Split(':')[2];
                            hexStr = hexStr.Split(',')[3];
                            string binary = String.Join(String.Empty, hexStr.Select(c => Convert.ToString(Convert.ToInt32(c.ToString(), 16), 2).PadLeft(4, '0')));

                            Response = new bool[binary.Length];

                            for (int i = 0; i < binary.Length; i++)
                            {
                                Response[i] = binary[i] == '1' ? true : false;
                            }
                        }

                        for (int i = 0; i < _Cfg.DigitalInputQuantity; i++)
                        {
                            if (DIN.ContainsKey(i))
                            {
                                bool org;
                                DIN.TryGetValue(i, out org);
                                if (!org.Equals(Response[i]))
                                {
                                    DIN.TryUpdate(i, Response[i], org);
                                    _Report.On_Data_Chnaged(_Cfg.DeviceName, "DIN", i.ToString(), org.ToString(), Response[i].ToString());
                                }
                            }
                            else
                            {
                                DIN.TryAdd(i, Response[i]);
                                _Report.On_Data_Chnaged(_Cfg.DeviceName, "DIN", i.ToString(), "False", Response[i].ToString());
                            }
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

                ushort adr = Convert.ToUInt16(Address);
                bool boolVal = false;
                if (bool.TryParse(Value, out boolVal))
                {
                    bool[] Response;

                    lock (conn)
                    {
                        conn.Send("$1SET:RELIO:17" + Convert.ToInt16(Address).ToString("00") + "," + (boolVal ? "1" : "0") + "\r");
                        SpinWait.SpinUntil(() => !Waiting, 5000);
                        if (Waiting)
                        {
                            logger.Error("DIO SetOut error: Timeout");
                            return;
                        }

                    }


                    bool org;
                    if (DOUT.TryGetValue(adr, out org))
                    {
                        if (!org.Equals(boolVal))
                        {
                            DOUT.TryUpdate(adr, boolVal, org);
                            _Report.On_Data_Chnaged(_Cfg.DeviceName, "DOUT", adr.ToString(), org.ToString(), boolVal.ToString());
                        }
                    }
                    else
                    {
                        DOUT.TryAdd(adr, boolVal);
                        _Report.On_Data_Chnaged(_Cfg.DeviceName, "DOUT", adr.ToString(), "N/A", boolVal.ToString());
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
                ushort adr = Convert.ToUInt16(Address);
                bool boolVal = false;
                if (bool.TryParse(Value, out boolVal))
                {
                    bool org;
                    if (DOUT.TryGetValue(adr, out org))
                    {
                        if (org != bool.Parse(Value))
                        {
                            DOUT.TryUpdate(adr, bool.Parse(Value), org);
                            _Report.On_Data_Chnaged(_Cfg.DeviceName, "DOUT", adr.ToString(), org.ToString(), bool.Parse(Value).ToString());
                        }
                    }
                    else
                    {
                        DOUT.TryAdd(adr, bool.Parse(Value));
                        _Report.On_Data_Chnaged(_Cfg.DeviceName, "DOUT", adr.ToString(), "N/A", bool.Parse(Value).ToString());
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
                bool[] data = new bool[_Cfg.DigitalInputQuantity];
                for (int i = 0; i < _Cfg.DigitalInputQuantity; i++)
                {
                    bool val;
                    if (DOUT.TryGetValue(i, out val))
                    {
                        data[i] = val;
                    }
                    else
                    {
                        data[i] = false;
                    }
                }
                lock (conn)
                {
                    string result = "";
                    foreach(bool each in data)
                    {
                        result += each ? "1" : "0";
                    }


                    conn.Send("$1SET:BRDIO:1,"+ BinaryStringToHexString(result) + "\r");
                    SpinWait.SpinUntil(() => !Waiting, 5000);
                    if (Waiting)
                    {
                        logger.Error("DIO SetOut error: Timeout");
                        return;
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
                int key = Convert.ToInt32(Address);
                if (DIN.ContainsKey(key))
                {
                    if (!DIN.TryGetValue(key, out result))
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

            bool result = false;
            lock (conn)
            {
                conn.Send("GET:RELIO:17" + Convert.ToInt16(Address).ToString("00") + "\r");
                SpinWait.SpinUntil(() => !Waiting, 5000);
                if (Waiting)
                {
                    logger.Error("DIO GetOut error: Timeout");
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
            throw new NotImplementedException();
        }

        public void On_Connection_Connecting(string Msg)
        {
            throw new NotImplementedException();
        }

        public void On_Connection_Connected(object Msg)
        {
            throw new NotImplementedException();
        }

        public void On_Connection_Disconnected(string Msg)
        {
            throw new NotImplementedException();
        }

        public void On_Connection_Error(string Msg)
        {
            throw new NotImplementedException();
        }
    }
}
