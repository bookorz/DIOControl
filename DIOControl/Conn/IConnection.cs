using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DIOControl.Comm
{
    interface IConnection
    {
        bool Send(object Message);
        bool SendHexData(object Message);
        void Start();
        void WaitForData(bool Enable);
        void Reconnect();
    }
}
