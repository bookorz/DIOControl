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
        ConcurrentDictionary<string, IDIOController> Ctrls;
        ConcurrentDictionary<string, ParamConfig> Params;
        ConcurrentDictionary<string, ParamConfig> Controls;
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
            foreach (IDIOController each in Ctrls.Values)
            {
                each.Connect();
            }
        }

        public void Close()
        {
            foreach (IDIOController each in Ctrls.Values)
            {
                each.Close();
            }
        }

        public void Initial()
        {
            Ctrls = new ConcurrentDictionary<string, IDIOController>();
            Params = new ConcurrentDictionary<string, ParamConfig>();
            Controls = new ConcurrentDictionary<string, ParamConfig>();
            Dictionary<string, object> keyValues = new Dictionary<string, object>();
            keyValues.Add("@equipment_model_id", SystemConfig.Get().SystemMode);



            string Sql = @"select t.dioname DeviceName,t.`type` 'Type',t.address ,upper(t.Parameter) Parameter,t.abnormal,t.error_code  from config_dio_point t
                    where  t.equipment_model_id = @equipment_model_id";
            DataTable dt = dBUtil.GetDataTable(Sql, keyValues);
            string str_json = JsonConvert.SerializeObject(dt, Formatting.Indented);

            List<ParamConfig> ParamList = JsonConvert.DeserializeObject<List<ParamConfig>>(str_json);


            foreach (ParamConfig each in ParamList)
            {
                if (each.Type.ToUpper().Equals("AIN") || each.Type.ToUpper().Equals("DIN"))
                {

                    Params.TryAdd(each.DeviceName + each.Address + each.Type, each);
                }
                else if (each.Type.ToUpper().Equals("AOUT") || each.Type.ToUpper().Equals("DOUT"))
                {

                    each.Status = "N/A";
                    Controls.TryAdd(each.Parameter, each);
                }
            }

            Sql = @"SELECT t.device_name as DeviceName,t.device_type as DeviceType,t.vendor as vendor,
                            case when t.conn_type = 'Socket' then  t.conn_address else '' end as IPAdress ,
                            case when t.conn_type = 'Socket' then  CONVERT(t.conn_port,SIGNED) else 0 end as Port ,
                            case when t.conn_type = 'Comport' then   CONVERT(t.conn_port,SIGNED) else 0 end as BaudRate ,
                            case when t.conn_type = 'Comport' then  t.conn_address else '' end as PortName ,             
                            t.conn_type as ConnectionType,
                            t.enable_flg as Enable
                            FROM config_controller_setting t
                            WHERE t.equipment_model_id = @equipment_model_id
                            AND t.device_type = 'DIO'";

            dt = dBUtil.GetDataTable(Sql, keyValues);
            str_json = JsonConvert.SerializeObject(dt, Formatting.Indented);

            List<CtrlConfig> ctrlList = JsonConvert.DeserializeObject<List<CtrlConfig>>(str_json);
            // List<CtrlConfig> ctrlList = new List<CtrlConfig>();
            foreach (CtrlConfig each in ctrlList)
            {
                IDIOController eachCtrl = null;
                if (each.Enable)
                {
                    switch (each.Vendor)
                    {
                        case "ICPCONDIGITAL":
                            var find = from Param in Params.Values.ToList()
                                       where Param.DeviceName.ToUpper().Equals(each.DeviceName.ToUpper())
                                       select Param;
                            foreach(ParamConfig eachDio in find)
                            {
                                if (eachDio.Type.ToUpper().Equals("DIN") || eachDio.Type.ToUpper().Equals("DOUT"))
                                {
                                    each.Digital = true;
                                }
                                else if (eachDio.Type.ToUpper().Equals("AIN") || eachDio.Type.ToUpper().Equals("AOUT"))
                                {
                                    each.Analog = true;
                                }
                            }

                            find = from Param in Controls.Values.ToList()
                                   where Param.DeviceName.ToUpper().Equals(each.DeviceName.ToUpper())
                                   select Param;
                            foreach (ParamConfig eachDio in find)
                            {
                                if (eachDio.Type.ToUpper().Equals("DIN") || eachDio.Type.ToUpper().Equals("DOUT"))
                                {
                                    each.Digital = true;
                                }
                                else if (eachDio.Type.ToUpper().Equals("AIN") || eachDio.Type.ToUpper().Equals("AOUT"))
                                {
                                    each.Analog = true;
                                }
                            }


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
                Dictionary<string, IDIOController> DIOList = new Dictionary<string, IDIOController>();
                foreach (ParamConfig each in find)
                {
                    IDIOController ctrl;
                    if (Ctrls.TryGetValue(each.DeviceName, out ctrl))
                    {
                        try
                        {
                            ctrl.SetOutWithoutUpdate(each.Address, Current);
                            if (!DIOList.ContainsKey(each.DeviceName))
                            {
                                DIOList.Add(each.DeviceName, ctrl);
                            }
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
                foreach (IDIOController eachDIO in DIOList.Values)
                {
                    eachDIO.UpdateOut();
                }

                if (Current.Equals("TRUE"))
                {
                    Current = "FALSE";
                }
                else
                {
                    Current = "TRUE";
                }

                //var inCfg = from parm in Params.Values.ToList()
                //            where parm.Parameter.Equals("IONIZERALARM")
                //            select parm;


                //ParamConfig Cfg = inCfg.First();

                //string key = Cfg.DeviceName + Cfg.Address + Cfg.Type;
                //if (Cfg.Abnormal.Equals(GetIO("IN", key).ToUpper()))
                //{
                //    _Report.On_Data_Chnaged(Cfg.Parameter, "BLINK");
                //}




                SpinWait.SpinUntil(() => false, 700);
            }
        }

        public void SetIO(string Parameter, string Value)
        {

            try
            {
                ParamConfig ctrlCfg;
                if (Controls.TryGetValue(Parameter.ToUpper(), out ctrlCfg))
                {
                    IDIOController ctrl;
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
            Dictionary<string, IDIOController> DIOList = new Dictionary<string, IDIOController>();
            foreach (string key in Params.Keys)
            {
                string Value = "";
                Params.TryGetValue(key, out Value);
                ParamConfig ctrlCfg;
                if (Controls.TryGetValue(key.ToUpper(), out ctrlCfg))
                {
                    IDIOController ctrl;
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
            foreach (IDIOController eachDIO in DIOList.Values)
            {
                eachDIO.UpdateOut();
            }
        }

        public bool SetBlink(string Parameter, string Value)
        {
            bool result = false;
            try
            {
                ParamConfig ctrlCfg;
                if (Controls.TryGetValue(Parameter, out ctrlCfg))
                {
                    if (Value.ToUpper().Equals("TRUE"))
                    {
                        if (!ctrlCfg.Status.ToUpper().Equals("BLINK"))
                        {
                            ChangeHisRecord.New(ctrlCfg.DeviceName, ctrlCfg.Type, ctrlCfg.Address, ctrlCfg.Parameter, "Blink", ctrlCfg.Status);
                        }
                        ctrlCfg.Status = "Blink";
                        _Report.On_Data_Chnaged(Parameter, "BLINK");
                    }
                    else
                    {
                        if (!ctrlCfg.Status.ToUpper().Equals("FALSE"))
                        {
                            ChangeHisRecord.New(ctrlCfg.DeviceName, ctrlCfg.Type, ctrlCfg.Address, ctrlCfg.Parameter, "False", ctrlCfg.Status);
                        }
                        ctrlCfg.Status = "False";
                        _Report.On_Data_Chnaged(Parameter, "FALSE");
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

        public string GetALL()
        {
            string result = "";
            foreach (ParamConfig outCfg in Controls.Values)
            {
                IDIOController ctrl;
                if (Ctrls.TryGetValue(outCfg.DeviceName, out ctrl))
                {
                    if (!result.Equals(""))
                    {
                        result += ",";
                    }
                    result += outCfg.Parameter + "=" + ctrl.GetOut(outCfg.Address);
                }
            }
            foreach (ParamConfig outCfg in Params.Values)
            {
                IDIOController ctrl;
                if (Ctrls.TryGetValue(outCfg.DeviceName, out ctrl))
                {
                    if (!result.Equals(""))
                    {
                        result += ",";
                    }
                    //result += outCfg.Parameter + "=" + ctrl.GetOut(outCfg.Address);
                    result += outCfg.Parameter + "=" + "True";
                }
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
                    ParamConfig outCfg;
                    if (Controls.TryGetValue(Parameter, out outCfg))
                    {
                        IDIOController ctrl;
                        if (Ctrls.TryGetValue(outCfg.DeviceName, out ctrl))
                        {
                            result = ctrl.GetOut(outCfg.Address);
                        }
                    }
                }
                else
                {

                    var find = from Param in Params.Values.ToList()
                               where Param.Parameter.ToUpper().Equals(Parameter.ToUpper())
                               select Param;


                    if (find.Count() != 0)
                    {
                        ParamConfig inCfg;
                        inCfg = find.First();
                        IDIOController ctrl;
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
                if (param.Type.Equals("DIN") || param.Type.Equals("DOUT"))
                {
                    if (NewValue.ToUpper().Equals(param.Abnormal))
                    {
                        NewValue = "False";
                        if (!param.Error_Code.Equals("N/A"))
                        {
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
                    }
                    else
                    {
                        NewValue = "TRUE";
                    }
                }

                _Report.On_Data_Chnaged(param.Parameter, NewValue);
                //if (Type.Equals("IN"))
                //{
                //    ChangeHisRecord.New(DIOName, Type, Address, param.Parameter, NewValue, OldValue);
                //}

            }

        }

        public void On_Connection_Error(string DIOName, string ErrorMsg)
        {
            //斷線重連
            SpinWait.SpinUntil(() => false, 1000);
            IDIOController dio;
            if (Ctrls.TryGetValue(DIOName, out dio))
            {
                dio.Connect();
            }
            //_Report.On_Connection_Error(DIOName, ErrorMsg);
        }

        public void On_Connection_Status_Report(string DIOName, string Status)
        {
            if (Status.Equals("Connected"))
            {
                var find = from cfg in Controls.Values.ToList()
                           where cfg.DeviceName.Equals(DIOName)
                           select cfg;

                foreach (ParamConfig cfg in find)
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
