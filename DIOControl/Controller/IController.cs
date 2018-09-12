using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DIOControl.Controller
{
    interface IController
    {
        void SetOut(string Address,string Value);
        void SetOutWithoutUpdate(string Address, string Value);
        void UpdateOut();
        string GetIn(string Address);
        string GetOut(string Address);
        void Connect();
        void Close();
    }
}
