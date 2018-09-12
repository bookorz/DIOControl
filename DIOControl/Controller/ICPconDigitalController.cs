using DIOControl.Config;

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
    class ICPconDigitalController : IController
    {

        IDIOReport _Report;
        CtrlConfig _Cfg;
        TcpClient tt;
        Modbus.Device.ModbusIpMaster Master;
        ConcurrentDictionary<int, bool> IN = new ConcurrentDictionary<int, bool>();
        ConcurrentDictionary<int, bool> OUT = new ConcurrentDictionary<int, bool>();

        public ICPconDigitalController(CtrlConfig Config, IDIOReport TriggerReport)
        {
            _Cfg = Config;
            _Report = TriggerReport;

            //Connect();

        }

        public void Close()
        {
            try
            {
                tt.Close();
                Master.Dispose();
            }
            catch
            {

            }
            _Report.On_Connection_Status_Report(_Cfg.DeviceName, "Disconnect");
        }

        public void Connect()
        {
            Close();
            Thread CTd = new Thread(ConnectServer);
            CTd.IsBackground = true;
            CTd.Start();
        }

        private void ConnectServer()
        {
            switch (_Cfg.ConnectionType)
            {
                case "Socket":
                    try
                    {
                        _Report.On_Connection_Status_Report(_Cfg.DeviceName, "Connecting");
                        tt = new TcpClient(_Cfg.IPAdress, _Cfg.Port);

                        Master = Modbus.Device.ModbusIpMaster.CreateIp(tt);
                        _Report.On_Connection_Status_Report(_Cfg.DeviceName, "Connected");

                    }
                    catch (Exception e)
                    {
                        _Report.On_Connection_Error(_Cfg.DeviceName, e.StackTrace);
                        _Report.On_Connection_Status_Report(_Cfg.DeviceName, "Connection_Error");
                        return;
                    }
                    break;
            }
            Master.Transport.Retries = _Cfg.Retries;
            Master.Transport.ReadTimeout = _Cfg.ReadTimeout;

            Thread ReceiveTd = new Thread(Polling);
            ReceiveTd.IsBackground = true;
            ReceiveTd.Start();
        }

        private void Polling()
        {
            while (true)
            {
                bool[] Response = new bool[0];
                try
                {
                    lock (Master)
                    {
                        Response = Master.ReadInputs(_Cfg.slaveID, 0, Convert.ToUInt16(_Cfg.DigitalInputQuantity));
                    }
                }
                catch (Exception e)
                {
                    _Report.On_Connection_Error(_Cfg.DeviceName, "Disconnect");
                    break;
                }
                for (int i = 0; i < _Cfg.DigitalInputQuantity; i++)
                {
                    if (IN.ContainsKey(i))
                    {
                        bool org;
                        IN.TryGetValue(i, out org);
                        if (org != Response[i])
                        {
                            IN.TryUpdate(i, Response[i], org);
                            _Report.On_Data_Chnaged(_Cfg.DeviceName, "IN", i.ToString(), org.ToString(), Response[i].ToString());
                        }
                    }
                    else
                    {
                        IN.TryAdd(i, Response[i]);
                        _Report.On_Data_Chnaged(_Cfg.DeviceName, "IN", i.ToString(),"N/A", Response[i].ToString());
                    }
                }

                SpinWait.SpinUntil(() => false, _Cfg.Delay);

            }
        }

        public void SetOut(string Address, string Value)
        {
            bool[] Response;
            ushort adr = Convert.ToUInt16(Address);
            try
            {
                lock (Master)
                {
                    Master.WriteSingleCoil(_Cfg.slaveID, adr, Convert.ToBoolean(Value));
                }
            }
            catch
            {
                throw new Exception(this._Cfg.DeviceName + " connection error!");
            }
            lock (Master)
            {
                 Response = Master.ReadCoils(_Cfg.slaveID, adr, 1);
            }
            bool org;
            if (OUT.TryGetValue(adr, out org))
            {
                if (org != Response[0])
                {
                    OUT.TryUpdate(adr, Response[0], org);
                    _Report.On_Data_Chnaged(_Cfg.DeviceName, "OUT", adr.ToString(), org.ToString(), Response[0].ToString());
                }
            }
            else
            {
                OUT.TryAdd(adr, Response[0]);
                _Report.On_Data_Chnaged(_Cfg.DeviceName, "OUT", adr.ToString(), "N/A", Response[0].ToString());
            }
        }

        public void SetOutWithoutUpdate(string Address, string Value)
        {
            ushort adr = Convert.ToUInt16(Address);
            
            bool org;
            if (OUT.TryGetValue(adr, out org))
            {
                if (org != bool.Parse(Value))
                {
                    OUT.TryUpdate(adr, bool.Parse(Value), org);
                    _Report.On_Data_Chnaged(_Cfg.DeviceName, "OUT", adr.ToString(), org.ToString(), bool.Parse(Value).ToString());
                }
            }
            else
            {
                OUT.TryAdd(adr, bool.Parse(Value));
                _Report.On_Data_Chnaged(_Cfg.DeviceName, "OUT", adr.ToString(), "N/A", bool.Parse(Value).ToString());
            }
        }

        public void UpdateOut()
        {
            bool[] data = new bool[_Cfg.DigitalInputQuantity];
            for(int i = 0;i< _Cfg.DigitalInputQuantity;i++)
            {
                bool val;
                if(OUT.TryGetValue(i,out val))
                {
                    data[i] = val;
                }
                else
                {
                    data[i] = false;
                }
            }
            lock (Master)
            {
                Master.WriteMultipleCoils(_Cfg.slaveID, 0, data);
            }
        }

        public string GetIn(string Address)
        {
            bool result = false;
            int key = Convert.ToInt32(Address);
            if (IN.ContainsKey(key))
            {
                if (!IN.TryGetValue(key, out result))
                {
                    throw new Exception("DeviceName:" + _Cfg.DeviceName + " Address " + Address + " get fail!");
                }
            }
            else
            {
                throw new Exception("DeviceName:" + _Cfg.DeviceName + " Address " + Address + " not exist!");
            }
            return result.ToString();
        }

        public string GetOut(string Address)
        {
            bool result = false;
            lock (Master)
            {
                result = Master.ReadCoils(_Cfg.slaveID, Convert.ToUInt16(Address), 1)[0];
            }

            return result.ToString();
        }


    }
}
