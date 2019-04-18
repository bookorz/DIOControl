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
    class ICPconDigitalController : IDIOController
    {
        ILog logger = LogManager.GetLogger(typeof(ICPconDigitalController));
        IDIOReport _Report;
        CtrlConfig _Cfg;
        TcpClient tt;
        Modbus.Device.ModbusIpMaster Master;
        ConcurrentDictionary<int, ushort> AIN = new ConcurrentDictionary<int, ushort>();
        ConcurrentDictionary<int, ushort> AOUT = new ConcurrentDictionary<int, ushort>();
        ConcurrentDictionary<int, bool> DIN = new ConcurrentDictionary<int, bool>();
        ConcurrentDictionary<int, bool> DOUT = new ConcurrentDictionary<int, bool>();
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
            try
            {
                Close();
                Thread CTd = new Thread(ConnectServer);
                CTd.IsBackground = true;
                CTd.Start();
            }
            catch (Exception e)
            {
                logger.Error(e.StackTrace);
            }
        }

        private void ConnectServer()
        {
            try
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
            catch (Exception e)
            {
                logger.Error(e.StackTrace);
            }
        }

        private void Polling()
        {
            DateTime AnalogRefreshTime = DateTime.Now;
            while (true)
            {
                try
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
                        Master.Dispose();
                        break;
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
                    TimeSpan timeDiff = DateTime.Now - AnalogRefreshTime;
                    if (timeDiff.TotalMilliseconds > 300)
                    {

                        ushort[] Response2 = new ushort[0];
                        try
                        {
                            lock (Master)
                            {
                                Response2 = Master.ReadInputRegisters(_Cfg.slaveID, 0, Convert.ToUInt16(_Cfg.DigitalInputQuantity));
                                AnalogRefreshTime = DateTime.Now;
                            }
                        }
                        catch (Exception e)
                        {
                            _Report.On_Connection_Error(_Cfg.DeviceName, "Disconnect");
                            Master.Dispose();
                            break;
                        }
                        for (int i = 0; i < _Cfg.DigitalInputQuantity; i++)
                        {
                            if (AIN.ContainsKey(i))
                            {
                                ushort org;
                                AIN.TryGetValue(i, out org);
                                if (!org.Equals(Response2[i].ToString()))
                                {
                                    AIN.TryUpdate(i, Response2[i], org);
                                }
                                _Report.On_Data_Chnaged(_Cfg.DeviceName, "AIN", i.ToString(), ((Convert.ToDouble(org) * 10.0 / 32767.0 - 1.0) / 4.0 * 50.0).ToString(), ((Convert.ToDouble(Response2[i]) * 10.0 / 32767.0 - 1.0) / 4.0 * 50.0).ToString().Substring(0, ((Convert.ToDouble(Response2[i]) * 10.0 / 32767.0 - 1.0) / 4.0 * 50.0).ToString().IndexOf(".") + 2));
                            }
                            else
                            {
                                AIN.TryAdd(i, Response2[i]);
                                _Report.On_Data_Chnaged(_Cfg.DeviceName, "AIN", i.ToString(), "0", ((Convert.ToDouble(Response2[i]) * 10.0 / 32767.0 - 1.0) / 4.0 * 50.0).ToString().Substring(0, ((Convert.ToDouble(Response2[i]) * 10.0 / 32767.0 - 1.0) / 4.0 * 50.0).ToString().IndexOf(".") + 2));
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
                    try
                    {
                        lock (Master)
                        {
                            Master.WriteSingleCoil(_Cfg.slaveID, adr, boolVal);
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
                    if (DOUT.TryGetValue(adr, out org))
                    {
                        if (!org.Equals(Response[0]))
                        {
                            DOUT.TryUpdate(adr, Response[0], org);
                            _Report.On_Data_Chnaged(_Cfg.DeviceName, "DOUT", adr.ToString(), org.ToString(), Response[0].ToString());
                        }
                    }
                    else
                    {
                        DOUT.TryAdd(adr, Response[0]);
                        _Report.On_Data_Chnaged(_Cfg.DeviceName, "DOUT", adr.ToString(), "N/A", Response[0].ToString());
                    }
                }
                else
                {
                    ushort[] Response2;
                    try
                    {
                        lock (Master)
                        {
                            Master.WriteSingleRegister(_Cfg.slaveID, adr, Convert.ToUInt16(Value));
                        }
                    }
                    catch
                    {
                        throw new Exception(this._Cfg.DeviceName + " connection error!");
                    }
                    lock (Master)
                    {
                        Response2 = Master.ReadHoldingRegisters(_Cfg.slaveID, adr, 1);
                    }
                    ushort org;
                    if (AOUT.TryGetValue(adr, out org))
                    {
                        if (!org.Equals(Response2[0]))
                        {
                            AOUT.TryUpdate(adr, Response2[0], org);
                            _Report.On_Data_Chnaged(_Cfg.DeviceName, "DOUT", adr.ToString(), org.ToString(), Response2[0].ToString());
                        }
                    }
                    else
                    {
                        AOUT.TryAdd(adr, Response2[0]);
                        _Report.On_Data_Chnaged(_Cfg.DeviceName, "DOUT", adr.ToString(), "N/A", Response2[0].ToString());
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
                else
                {
                    ushort org;
                    if (AOUT.TryGetValue(adr, out org))
                    {
                        if (org != Convert.ToUInt16(Value))
                        {
                            AOUT.TryUpdate(adr, Convert.ToUInt16(Value), org);
                            _Report.On_Data_Chnaged(_Cfg.DeviceName, "AOUT", adr.ToString(), org.ToString(), Value);
                        }
                    }
                    else
                    {
                        AOUT.TryAdd(adr, Convert.ToUInt16(Value));
                        _Report.On_Data_Chnaged(_Cfg.DeviceName, "AOUT", adr.ToString(), "N/A", Value);
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error(e.StackTrace);
            }
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
                lock (Master)
                {
                    Master.WriteMultipleCoils(_Cfg.slaveID, 0, data);
                }

                ushort[] data2 = new ushort[_Cfg.DigitalInputQuantity];
                for (int i = 0; i < _Cfg.DigitalInputQuantity; i++)
                {
                    ushort val;
                    if (AOUT.TryGetValue(i, out val))
                    {
                        data2[i] = Convert.ToUInt16(Convert.ToDouble(val)*32767.0/10.0);
                    }
                    else
                    {
                        data2[i] = 0;
                    }
                }
                lock (Master)
                {
                    Master.WriteMultipleRegisters(_Cfg.slaveID, 0, data2);
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
            try
            {
                if (Master == null)
                {
                    return "";
                }
                lock (Master)
                {
                    result = Master.ReadCoils(_Cfg.slaveID, Convert.ToUInt16(Address), 1)[0];
                }
            }
            catch (Exception e)
            {
                logger.Error(e.StackTrace);
            }
            return result.ToString();
        }


    }
}
