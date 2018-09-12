using log4net;
using SANWA.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DIOControl
{
    public class ChangeHisRecord
    {
        static ILog logger = LogManager.GetLogger(typeof(ChangeHisRecord));

        public static void New(string dio_name, string dio_type, string address, string parameter, string new_value, string old_value)
        {

            DBUtil dBUtil = new DBUtil();
            Dictionary<string, object> keyValues = new Dictionary<string, object>();

            try
            {
                DateTime SendTime = DateTime.Now;


                string SQL = @"insert into log_dio_event (dio_name,dio_type,address,parameter,new_value,old_value,event_time,time_stamp)
                                    values(@dio_name,@dio_type,@address,@parameter,@new_value,@old_value,@event_time,@time_stamp)";

                keyValues.Add("@dio_name", dio_name);
                keyValues.Add("@dio_type", dio_type);
                keyValues.Add("@address", address);
                keyValues.Add("@parameter", parameter);
                keyValues.Add("@new_value", new_value.ToUpper());
                keyValues.Add("@old_value", old_value.ToUpper());
                keyValues.Add("@event_time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff"));
                keyValues.Add("@time_stamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff"));

                dBUtil.ExecuteNonQueryAsync(SQL, keyValues);
               

            }
            catch (Exception e)
            {
                logger.Error("New error:" + e.StackTrace);
            }

        }
    }
}
