using DIOControl.Config;
using DIOControl.Controller;
using log4net;
using Newtonsoft.Json;
using SANWA.Utility;
using SANWA.Utility.Config;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DIOControl
{
    public class DIO : IDIOReport
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(DIO));
        IDIOTriggerReport _Report;
        ConcurrentDictionary<string, IController> Ctrls;
        ConcurrentDictionary<string, ParamConfig> Params;
        ConcurrentDictionary<string, ControlConfig> Controls;
        private static DBUtil dBUtil = new DBUtil();

        public DIO(IDIOTriggerReport ReportTarget)
        {
            _Report = ReportTarget;
            Thread InitTd = new Thread(Initial);
            InitTd.IsBackground = true;
            InitTd.Start();
        }

        public void Connect()
        {
            foreach (IController each in Ctrls.Values)
            {
                each.Connect();
            }
        }

        public void Close()
        {
            foreach (IController each in Ctrls.Values)
            {
                each.Close();
            }
        }

        public void Initial()
        {
            Ctrls = new ConcurrentDictionary<string, IController>();
            Params = new ConcurrentDictionary<string, ParamConfig>();
            Controls = new ConcurrentDictionary<string, ControlConfig>();
            Dictionary<string, object> keyValues = new Dictionary<string, object>();
            string Sql = @"SELECT t.device_name as DeviceName,t.device_type as DeviceType,t.vendor as vendor,
                            case when t.conn_type = 'Socket' then  t.conn_address else '' end as IPAdress ,
                            case when t.conn_type = 'Socket' then  CONVERT(t.conn_prot,SIGNED) else 0 end as Port ,
                            case when t.conn_type = 'Comport' then   CONVERT(t.conn_prot,SIGNED) else 0 end as BaudRate ,
                            case when t.conn_type = 'Comport' then  t.conn_address else '' end as PortName ,
                            t.com_parity_bit as ParityBit,
                            ifnull(CONVERT(t.com_data_bits,SIGNED),0) as DataBits,
                            t.com_stop_bit as StopBit,
                            t.conn_type as ConnectionType,
                            t.enable_flg as Enable
                            FROM config_controller_setting t
                            WHERE t.equipment_model_id = @equipment_model_id
                            AND t.device_type = 'DIO'";
            keyValues.Add("@equipment_model_id", SystemConfig.Get().SystemMode);
            DataTable dt = dBUtil.GetDataTable(Sql, keyValues);
            string str_json = JsonConvert.SerializeObject(dt, Formatting.Indented);

            List<CtrlConfig> ctrlList = JsonConvert.DeserializeObject<List<CtrlConfig>>(str_json);

            foreach (CtrlConfig each in ctrlList)
            {
                IController eachCtrl = null;
                switch (each.Vendor)
                {
                    case "ICPCONDIGITAL":
                        each.slaveID = 1;
                        each.DigitalInputQuantity = 8;
                        each.Delay = 100;
                        each.ReadTimeout = 1000;
                        eachCtrl = new ICPconDigitalController(each, this);

                        break;
                }
                if (eachCtrl != null)
                {
                    Ctrls.TryAdd(each.DeviceName, eachCtrl);
                }
            }


            Sql = @"select t.dioname DeviceName,t.`type` 'Type',t.address ,upper(t.Parameter) Parameter,t.abnormal,t.error_code  from config_dio_point t
                    where t.`type` = 'IN'";
            dt = dBUtil.GetDataTable(Sql, null);
            str_json = JsonConvert.SerializeObject(dt, Formatting.Indented);

            List<ParamConfig> ParamList = JsonConvert.DeserializeObject<List<ParamConfig>>(str_json);


            foreach (ParamConfig each in ParamList)
            {
                Params.TryAdd(each.DeviceName + each.Address + each.Type, each);
            }

            Sql = @"select t.dioname DeviceName,t.`type` 'Type',t.address ,upper(t.Parameter) Parameter,t.abnormal,t.error_code  from config_dio_point t
                    where t.`type` = 'OUT'";
            dt = dBUtil.GetDataTable(Sql, null);
            str_json = JsonConvert.SerializeObject(dt, Formatting.Indented);

            List<ControlConfig> CList = JsonConvert.DeserializeObject<List<ControlConfig>>(str_json);
            foreach (ControlConfig each in CList)
            {
                each.Status = "N/A";
                Controls.TryAdd(each.Parameter, each);
            }


            Thread BlinkTd = new Thread(Blink);
            BlinkTd.IsBackground = true;
            BlinkTd.Start();
        }

        private void Blink()
        {
            string Current = "TRUE";
            while (true)
            {
                var find = from Out in Controls.Values.ToList()
                           where Out.Status.Equals("Blink")
                           select Out;
                foreach (ControlConfig each in find)
                {
                    IController ctrl;
                    if (Ctrls.TryGetValue(each.DeviceName, out ctrl))
                    {
                        try
                        {
                            ctrl.SetOut(each.Address, Current);
                        }
                        catch (Exception e)
                        {
                            logger.Error(e.StackTrace);
                        }
                        _Report.On_Data_Chnaged(each.Parameter, Current);
                    }
                    else
                    {
                        logger.Debug("SetIO:DeviceName is not exist.");
                    }
                }
                if (Current.Equals("TRUE"))
                {
                    Current = "FALSE";
                }
                else
                {
                    Current = "TRUE";
                }

                var inCfg = from parm in Params.Values.ToList()
                            where parm.Parameter.Equals("IONIZERALARM")
                            select parm;


                ParamConfig Cfg = inCfg.First();

                string key = Cfg.DeviceName + Cfg.Address + Cfg.Type;
                if (Cfg.Abnormal.Equals(GetIO("IN", key).ToUpper()))
                {
                    _Report.On_Data_Chnaged(Cfg.Parameter, "BLINK");
                }




                SpinWait.SpinUntil(() => false, 700);
            }
        }

        public void SetIO(string Parameter, string Value)
        {

            try
            {
                ControlConfig ctrlCfg;
                if (Controls.TryGetValue(Parameter, out ctrlCfg))
                {
                    IController ctrl;
                    if (Ctrls.TryGetValue(ctrlCfg.DeviceName, out ctrl))
                    {
                        ChangeHisRecord.New(ctrlCfg.DeviceName, ctrlCfg.Type, ctrlCfg.Address, ctrlCfg.Parameter, Value, ctrlCfg.Status);
                        ctrlCfg.Status = Value;
                        ctrl.SetOut(ctrlCfg.Address, Value);
                        _Report.On_Data_Chnaged(Parameter, Value);
                    }
                    else
                    {
                        logger.Debug("SetIO:DeviceName is not exist.");
                    }
                }
                else
                {
                    logger.Debug("SetIO:Parameter is not exist.");
                }
            }
            catch (Exception e)
            {
                logger.Debug("SetIO:" + e.Message);
            }

        }

        public void SetIO(Dictionary<string, string> Params)
        {
            Dictionary<string, IController> DIOList = new Dictionary<string, IController>();
            foreach (string key in Params.Keys)
            {
                string Value = "";
                Params.TryGetValue(key, out Value);
                ControlConfig ctrlCfg;
                if (Controls.TryGetValue(key.ToUpper(), out ctrlCfg))
                {
                    IController ctrl;
                    if (Ctrls.TryGetValue(ctrlCfg.DeviceName, out ctrl))
                    {
                        if (!Value.Equals(ctrlCfg.Status))
                        {
                            ChangeHisRecord.New(ctrlCfg.DeviceName, ctrlCfg.Type, ctrlCfg.Address, ctrlCfg.Parameter, Value, ctrlCfg.Status);
                        }
                        ctrlCfg.Status = Value;
                        ctrl.SetOutWithoutUpdate(ctrlCfg.Address, Value);
                        _Report.On_Data_Chnaged(key, Value);
                        if (!DIOList.ContainsKey(ctrlCfg.DeviceName))
                        {
                            DIOList.Add(ctrlCfg.DeviceName, ctrl);
                        }
                    }
                    else
                    {
                        logger.Debug("SetIO:DeviceName is not exist.");
                    }
                }
                else
                {
                    logger.Debug("SetIO:Parameter is not exist.");
                }
            }
            foreach (IController eachDIO in DIOList.Values)
            {
                eachDIO.UpdateOut();
            }
        }

        public bool SetBlink(string Parameter, string Value)
        {
            bool result = false;
            try
            {
                ControlConfig ctrlCfg;
                if (Controls.TryGetValue(Parameter, out ctrlCfg))
                {
                    if (Value.ToUpper().Equals("TRUE"))
                    {
                        if (!ctrlCfg.Status.ToUpper().Equals("BLINK"))
                        {
                            ChangeHisRecord.New(ctrlCfg.DeviceName, ctrlCfg.Type, ctrlCfg.Address, ctrlCfg.Parameter, "Blink", ctrlCfg.Status);
                        }
                        ctrlCfg.Status = "Blink";
                    }
                    else
                    {
                        if (!ctrlCfg.Status.ToUpper().Equals("FALSE"))
                        {
                            ChangeHisRecord.New(ctrlCfg.DeviceName, ctrlCfg.Type, ctrlCfg.Address, ctrlCfg.Parameter, "False", ctrlCfg.Status);
                        }
                    }

                }
                else
                {
                    logger.Debug("SetIO:Parameter is not exist.");
                }
            }
            catch (Exception e)
            {
                logger.Debug("SetBlink:" + e.Message);
            }
            return result;
        }

        public string GetIO(string Type, string Parameter)
        {

            string result = "";
            try
            {
                if (Type.Equals("OUT"))
                {
                    ControlConfig outCfg;
                    if (Controls.TryGetValue(Parameter, out outCfg))
                    {
                        IController ctrl;
                        if (Ctrls.TryGetValue(outCfg.DeviceName, out ctrl))
                        {
                            result = ctrl.GetOut(outCfg.Address);
                        }
                    }
                }
                else
                {
                    ParamConfig inCfg;
                    if (Params.TryGetValue(Parameter, out inCfg))
                    {
                        IController ctrl;
                        if (Ctrls.TryGetValue(inCfg.DeviceName, out ctrl))
                        {
                            result = ctrl.GetIn(inCfg.Address);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.Debug("GetIO:" + e.Message);
            }
            return result;
        }

        public void On_Data_Chnaged(string DIOName, string Type, string Address, string OldValue, string NewValue)
        {
            string key = DIOName + Address + Type;
            ParamConfig param;
            if (Params.ContainsKey(key))
            {
                Params.TryGetValue(key, out param);
                if (NewValue.ToUpper().Equals(param.Abnormal))
                {
                    NewValue = "False";
                    if (param.LastErrorHappenTime == null)
                    {
                        param.LastErrorHappenTime = DateTime.Now;
                    }
                    else
                    {
                        TimeSpan t = DateTime.Now - param.LastErrorHappenTime;
                        if (t.TotalSeconds > 1)
                        {
                            param.LastErrorHappenTime = DateTime.Now;
                            _Report.On_Alarm_Happen(param.Parameter, param.Error_Code);
                        }

                    }
                }
                else
                {
                    NewValue = "TRUE";
                }
                _Report.On_Data_Chnaged(param.Parameter, NewValue);
                if (Type.Equals("IN"))
                {
                    ChangeHisRecord.New(DIOName, Type, Address, param.Parameter, NewValue, OldValue);
                }
                
            }

        }

        public void On_Connection_Error(string DIOName, string ErrorMsg)
        {
            _Report.On_Connection_Error(DIOName, ErrorMsg);
        }

        public void On_Connection_Status_Report(string DIOName, string Status)
        {
            if (Status.Equals("Connected"))
            {
                var find = from cfg in Controls.Values.ToList()
                           where cfg.DeviceName.Equals(DIOName)
                           select cfg;

                foreach (ControlConfig cfg in find)
                {
                    if (cfg.DeviceName.Equals(DIOName))
                    {
                        _Report.On_Data_Chnaged(cfg.Parameter, GetIO("OUT", cfg.Parameter));
                    }
                }
            }
            _Report.On_Connection_Status_Report(DIOName, Status);
        }
    }
}
