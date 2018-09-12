using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DIOControl.Controller
{
    public interface IDIOReport
    {
        void On_Data_Chnaged(string DIOName, string Type, string Address, string OldValue, string NewValue);
        void On_Connection_Error(string DIOName, string ErrorMsg);
        void On_Connection_Status_Report(string DIOName, string Status);
        
    }
}
